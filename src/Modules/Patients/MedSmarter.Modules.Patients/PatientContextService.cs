using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Patients;

/// <summary>Builds the minimised, consent-filtered <see cref="PatientContext"/> (see docs/phase5). Reads the database itself; callers (AI) only ever see the result.</summary>
public sealed class PatientContextService(IDbContextFactory<PatientsDbContext> factory, IClock clock, IPurposeConsentEvaluator consent, IMedicationService reference) : IPatientContextService
{
    private static readonly (PatientDataCategory Category, string Scope)[] Categories =
    [
        (PatientDataCategory.Profile, DataScopes.Profile),
        (PatientDataCategory.Medications, DataScopes.Medications),
        (PatientDataCategory.Allergies, DataScopes.Allergies),
        (PatientDataCategory.Conditions, DataScopes.Conditions),
        (PatientDataCategory.Symptoms, DataScopes.Symptoms),
    ];

    public async Task<PatientOutcome<PatientContext>> BuildAsync(Guid subjectId, PatientContextPurpose purpose, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<PatientContext>(PatientError.NotFound, "patient.not_found");
        }

        if (patient.Status != PatientStatus.Active)
        {
            return PatientOutcome.Fail<PatientContext>(PatientError.Forbidden, "patient.inactive");
        }

        var purposeName = purpose == PatientContextPurpose.AiAssistant ? ConsentPurposes.AiProcessing : ConsentPurposes.Monitoring;
        var allowed = new HashSet<PatientDataCategory>();
        var excluded = new List<ExcludedCategory>();
        foreach (var (category, scope) in Categories)
        {
            var decision = await consent.EvaluateAsync(new PurposeConsentCheck(subjectId, purposeName, [scope]), ct);
            if (decision.Allowed)
            {
                allowed.Add(category);
            }
            else
            {
                excluded.Add(new ExcludedCategory(category, "consent_required"));
            }
        }

        var externalScopes = Categories.Where(c => allowed.Contains(c.Category)).Select(c => c.Scope).ToArray();
        var external = externalScopes.Length > 0 && (await consent.EvaluateAsync(new PurposeConsentCheck(subjectId, ConsentPurposes.AiExternalProcessing, externalScopes), ct)).Allowed;
        var now = clock.UtcNow;
        var profile = allowed.Contains(PatientDataCategory.Profile) ? await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.PatientId == patient.Id, ct) : null;

        var meds = new List<ContextMedication>();
        if (allowed.Contains(PatientDataCategory.Medications))
        {
            foreach (var m in await db.Medications.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null && x.Status != PatientMedicationStatus.Stopped).ToListAsync(ct))
            {
                string name;
                if (m.MedicationId is { } id)
                {
                    var r = await reference.GetAsync(id, true, ct);
                    name = r.Succeeded ? r.Value!.Name.En ?? r.Value.Name.Fa ?? "unknown" : "unknown";
                }
                else
                {
                    name = m.UnregisteredName ?? "unknown";
                }

                var dose = m.DoseAmount is { } a ? $"{a:0.####} {m.DoseUnit}" : m.DoseText;
                var freq = m.Frequency switch { FrequencyKind.TimesPerDay => $"{m.FrequencyValue}x/day", FrequencyKind.EveryNHours => $"every {m.FrequencyValue} h", FrequencyKind.AsNeeded => "as needed", _ => "other" };
                meds.Add(new ContextMedication(m.MedicationId, name, dose, freq, m.Status.ToString(), m.MedicationId is not null));
            }
        }

        var allergies = allowed.Contains(PatientDataCategory.Allergies)
            ? (await db.Allergies.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null).ToListAsync(ct)).Select(a => new ContextAllergy(a.Substance, a.Severity.ToString(), a.Reaction)).ToList()
            : [];
        var conditions = allowed.Contains(PatientDataCategory.Conditions)
            ? (await db.Conditions.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null).ToListAsync(ct)).Select(c => new ContextCondition(c.Name, c.Status.ToString())).ToList()
            : [];
        var since = now.AddDays(-30);
        var symptoms = allowed.Contains(PatientDataCategory.Symptoms)
            ? (await db.Symptoms.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null && x.OnsetAt >= since).OrderByDescending(x => x.OnsetAt).Take(20).ToListAsync(ct))
                .Select(s => new ContextSymptom(s.Text, s.Severity.ToString(), DateOnly.FromDateTime(s.OnsetAt.UtcDateTime))).ToList()
            : [];

        var meta = (await db.DataCategories.AsNoTracking().Where(x => x.PatientId == patient.Id).ToListAsync(ct)).Where(x => allowed.Contains(x.Category)).ToDictionary(x => x.Category.ToString(), x => (DateTimeOffset?)x.LastUpdatedAt);
        var purposes = new List<string>();
        foreach (var p in new[] { ConsentPurposes.AiProcessing, ConsentPurposes.AiExternalProcessing, ConsentPurposes.Monitoring })
        {
            if ((await consent.EvaluateAsync(new PurposeConsentCheck(subjectId, p, []), ct)).Allowed)
            {
                purposes.Add(p);
            }
        }

        var context = new PatientContext(
            allowed.Contains(PatientDataCategory.Profile) ? AgeBands.Of(profile?.YearOfBirth, now.Year) : "unknown",
            allowed.Contains(PatientDataCategory.Profile) && profile?.Sex is { } sex ? sex.ToString() : "unknown",
            meds, allergies, conditions, symptoms, meta, purposes, excluded, external,
            patient.IsDemo ? Support.DemoNotice : "Built from the categories the patient consented to. Names, account ids, contact details, birth year and free-text notes are not included.");
        return PatientOutcome.Ok(context);
    }
}
