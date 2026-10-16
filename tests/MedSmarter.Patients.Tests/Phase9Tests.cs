using System.Reflection;
using System.Text.Json;
using MedSmarter.Modules.ADR;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 9 at service level: the internal adverse-event record, its conservative classification, human review, separation of sources and audit.</summary>
public class Phase9Tests
{
    private static CreateAdverseEventInput Report(string text = "Itchy rash on both arms", IReadOnlyList<SuspectedMedicationInput>? meds = null, DateTimeOffset? onset = null, DateTimeOffset? resolved = null, string? details = null, string? outcome = null, string? cid = null) =>
        new(text, onset, resolved, meds ?? [], outcome, details, cid);

    private static async Task<(PEnv Env, IAdverseEventService Svc, Guid Sara, Guid Doctor)> Setup()
    {
        var env = new PEnv();
        return (env, env.Get<IAdverseEventService>(), await env.Patient("demo-patient"), env.UserId("demo-physician"));
    }

    [Fact]
    public async Task A_new_record_is_unclassified_submitted_internal_and_never_submitted_anywhere()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var created = await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(), "test", null);
        Assert.True(created.Succeeded, created.Detail);
        var e = created.Value!.Event;
        Assert.Equal(ReportOrigin.PatientReported, e.Origin);
        Assert.Equal(ReviewStatus.Submitted, e.Status);
        Assert.Equal(AdrNotices.ExternalNotSubmitted, e.ExternalStatus);
        Assert.Contains("not proof", e.SignalNotice, StringComparison.Ordinal);
        Assert.Contains("not been submitted", e.InternalRecordNotice, StringComparison.Ordinal);
        Assert.Equal(WordingStatus.AwaitingClinicalReview, e.PersianWordingStatus);
        Assert.Null(e.Category); // a patient does not see a professional label

        var asDoctor = (await svc.GetAsync(e.Id, ReporterRole.Physician)).Value!.Event;
        Assert.Equal(EventCategory.Unclassified, asDoctor.Category);
        Assert.Contains("onset.unknown", asDoctor.MissingInformation);
        Assert.Contains("medication.none_named", asDoctor.MissingInformation);
    }

    [Fact]
    public async Task A_professional_entered_record_is_labelled_as_such()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var e = (await svc.CreateAsync(doctor, ReporterRole.Physician, sara, Report(), "test", null)).Value!.Event;
        Assert.Equal(ReportOrigin.ProfessionalEntered, e.Origin);
        Assert.Equal(ReporterRole.Physician, e.ReporterRole);
        // a patient role can only report for themselves
        Assert.Equal(AdrError.Forbidden, (await svc.CreateAsync(doctor, ReporterRole.Patient, sara, Report(), "test", null)).Error);
    }

    [Fact]
    public async Task The_set_of_categories_is_exactly_the_five_defined_terms_plus_unclassified_and_nothing_else()
    {
        Assert.Equal(["AdverseDrugEvent", "AdverseDrugReaction", "AdverseEvent", "MedicationError", "NearMiss", "Unclassified"], Enum.GetNames<EventCategory>().Order().ToArray());
        Assert.DoesNotContain(Enum.GetNames<EventCategory>(), n => n.Contains("Quality", StringComparison.OrdinalIgnoreCase) || n.Contains("Effect", StringComparison.OrdinalIgnoreCase) && n != "AdverseEvent");
        // every defined term is documented with its supplementary source, none claims to be a lecture slide, and the Persian wording is awaiting review
        Assert.Equal(5, AdrDefinitions.All.Count);
        Assert.All(AdrDefinitions.All, d =>
        {
            Assert.False(d.Source.IsLectureSlide);
            Assert.Equal("SupplementaryDocument", d.Source.Kind);
            Assert.Equal(WordingStatus.AwaitingClinicalReview, d.PersianWordingStatus);
            Assert.False(string.IsNullOrWhiteSpace(d.Source.Page));
        });
        Assert.Contains(AdrDefinitions.All, d => d.Category == EventCategory.AdverseEvent && d.Definition.Contains("does not necessarily have a causal relationship", StringComparison.Ordinal));
        Assert.Contains(AdrDefinitions.All, d => d.Category == EventCategory.NearMiss && d.Definition.Contains("intercepted and corrected", StringComparison.Ordinal));
    }

    [Fact]
    public void There_is_no_seriousness_grade_causality_score_or_submission_schema_anywhere_in_the_contract()
    {
        var types = typeof(IAdverseEventService).Assembly.GetTypes();
        var names = types.SelectMany(t => t.GetProperties().Select(p => p.Name).Concat(t.IsEnum ? Enum.GetNames(t) : [])).Concat(types.Select(t => t.Name)).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Serious", StringComparison.OrdinalIgnoreCase) || n.Contains("Causal", StringComparison.OrdinalIgnoreCase) || n.Contains("Naranjo", StringComparison.OrdinalIgnoreCase) || n.Contains("E2B", StringComparison.OrdinalIgnoreCase) || n.Contains("MedDRA", StringComparison.OrdinalIgnoreCase) || n.Contains("Deadline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_module_has_no_outbound_path_it_references_no_integration_product_trace_or_http_assembly()
    {
        var refs = typeof(AdverseEventService).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(refs, n => n.Contains("Integrations", StringComparison.Ordinal) || n.Contains("ProductTrace", StringComparison.Ordinal) || n.Contains("System.Net.Http", StringComparison.Ordinal) || n.Contains("AI", StringComparison.Ordinal) && n.EndsWith(".AI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_original_report_is_never_changed_by_anything_that_follows()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var id = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(onset: null), "test", null)).Value!.Event.Id;
        var before = (await svc.GetAsync(id, ReporterRole.Physician)).Value!.Event.Original;
        env.Clock.Advance(TimeSpan.FromHours(1));
        await svc.AddFollowUpAsync(sara, ReporterRole.Patient, id, "It got worse on the weekend", "test", null);
        await svc.AddSupplementAsync(sara, ReporterRole.Patient, id, new SupplementInput(env.Clock.UtcNow.AddDays(-3), null, "still itchy", "remembered the date"), "test", null);
        await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, id, EntryKind.ProfessionalAssessment, "Needs a look at the medicine list", "test", null);
        var v = (await svc.GetAsync(id, ReporterRole.Physician)).Value!.Event.Version;
        await svc.ClassifyAsync(doctor, ReporterRole.Physician, id, EventCategory.AdverseEvent, "Occurred during treatment; no causal link assumed", v, "test", null);
        var after = (await svc.GetAsync(id, ReporterRole.Physician)).Value!;
        Assert.Equal(before, after.Event.Original);
        Assert.Null(after.Event.Original.OnsetAt);
        Assert.NotNull(after.Event.Effective.OnsetAt.Value); // shown beside the original, with its source
        Assert.Equal("SupplementByPatient", after.Event.Effective.OnsetAt.Provenance);
        Assert.Equal("NotRecorded", after.Event.Effective.ResolvedAt.Provenance);
        Assert.DoesNotContain("onset.unknown", after.Event.MissingInformation);
    }

    [Fact]
    public async Task A_supplement_only_fills_what_is_unknown_and_never_replaces_a_recorded_value()
    {
        var (env, svc, sara, _) = await Setup();
        using var _env = env;
        var onset = env.Clock.UtcNow.AddDays(-5);
        var id = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(onset: onset), "test", null)).Value!.Event.Id;
        Assert.Equal(AdrError.Conflict, (await svc.AddSupplementAsync(sara, ReporterRole.Patient, id, new SupplementInput(onset.AddDays(1), null, null, null), "test", null)).Error);
        Assert.Equal(AdrError.Validation, (await svc.AddSupplementAsync(sara, ReporterRole.Patient, id, new SupplementInput(null, null, null, null), "test", null)).Error);
        Assert.Equal(AdrError.Validation, (await svc.AddSupplementAsync(sara, ReporterRole.Patient, id, new SupplementInput(null, onset.AddDays(-1), null, null), "test", null)).Error); // ends before it starts
        Assert.True((await svc.AddSupplementAsync(sara, ReporterRole.Patient, id, new SupplementInput(null, null, "settled by itself", null), "test", null)).Succeeded);
        Assert.Equal(AdrError.Conflict, (await svc.AddSupplementAsync(sara, ReporterRole.Patient, id, new SupplementInput(null, null, "something else", null), "test", null)).Error);
    }

    [Fact]
    public async Task Unknown_medicines_stay_unknown_and_a_link_is_only_made_to_the_patients_own_record()
    {
        var (env, svc, sara, _) = await Setup();
        using var _env = env;
        var mine = await env.TakeMedication(sara);
        var other = await env.Patient("demo-patient-2");
        var theirs = await env.TakeMedication(other);

        var named = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(meds: [new(null, "a white tablet from my cousin")]), "test", null)).Value!.Event;
        var m = Assert.Single(named.SuspectedMedications);
        Assert.Equal(MedicationReferenceState.ReportedNameOnly, m.State);
        Assert.Null(m.DisplayName);
        Assert.Null(m.PatientMedicationId);
        Assert.Contains("medication.not_linked", named.MissingInformation);

        Assert.Equal("medication.not_found", (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(meds: [new(theirs.Id, null)]), "test", null)).Detail); // someone else's record
        Assert.Equal("medication.invalid", (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(meds: [new(null, "  ")]), "test", null)).Detail);

        var linked = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(meds: [new(mine.Id, null)]), "test", null)).Value!.Event;
        Assert.Equal(MedicationReferenceState.Linked, linked.SuspectedMedications[0].State);
        Assert.Equal(mine.DisplayName, linked.SuspectedMedications[0].DisplayName);

        // the linked record disappears: the report keeps the name given at the time and says the link is unavailable
        Assert.True((await env.Meds.RemoveAsync(sara, sara, mine.Id, "test", null)).Succeeded);
        var later = (await svc.GetAsync(linked.Id, ReporterRole.Physician)).Value!.Event;
        Assert.Equal(MedicationReferenceState.LinkedUnavailable, later.SuspectedMedications[0].State);
        Assert.Equal(mine.DisplayName, later.SuspectedMedications[0].NameAsReported);
        Assert.Contains("medication.reference_unavailable", later.MissingInformation);
        var help = (await svc.AssistAsync(env.UserId("demo-physician"), ReporterRole.Physician, linked.Id, "test", null)).Value!;
        Assert.Contains(help.Uncertainty, u => u.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classification_is_professional_reasoned_reversible_versioned_and_keeps_every_earlier_value()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var e = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(), "test", null)).Value!.Event;
        Assert.Equal(AdrError.Forbidden, (await svc.ClassifyAsync(sara, ReporterRole.Patient, e.Id, EventCategory.AdverseDrugReaction, "x", e.Version, "test", null)).Error);
        Assert.Equal(AdrError.Validation, (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.AdverseDrugReaction, " ", e.Version, "test", null)).Error);
        Assert.Equal(AdrError.Validation, (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, (EventCategory)99, "reason", e.Version, "test", null)).Error);
        Assert.Equal(AdrError.Conflict, (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.AdverseDrugReaction, "reason", e.Version + 5, "test", null)).Error);
        Assert.Equal(AdrError.Conflict, (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.Unclassified, "reason", e.Version, "test", null)).Error); // nothing to change

        var one = (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.AdverseEvent, "Happened during treatment; cause not established", e.Version, "test", null)).Value!;
        Assert.Equal(EventCategory.AdverseEvent, one.Event.Category);
        Assert.Equal(AdrError.Conflict, (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.MedicationError, "reason", e.Version, "test", null)).Error); // stale version
        var back = (await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.Unclassified, "Not enough information after all", one.Event.Version, "test", null)).Value!;
        Assert.Equal(EventCategory.Unclassified, back.Event.Category);
        var steps = back.Entries.Where(x => x.Kind == EntryKind.Classification).ToList();
        Assert.Equal([(EventCategory.Unclassified, EventCategory.AdverseEvent), (EventCategory.AdverseEvent, EventCategory.Unclassified)], steps.Select(s => (s.FromCategory!.Value, s.ToCategory!.Value)).ToArray());
        Assert.All(steps, s => Assert.Equal(ReporterRole.Physician, s.AuthorRole));
    }

    [Fact]
    public async Task The_review_status_follows_fixed_moves_closing_needs_a_reason_and_a_patient_answer_reopens_review()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var e = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(), "test", null)).Value!.Event;
        Assert.Equal(AdrError.Forbidden, (await svc.SetStatusAsync(sara, ReporterRole.Patient, e.Id, ReviewStatus.InReview, null, e.Version, "test", null)).Error);
        Assert.Equal(AdrError.Conflict, (await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.Assessed, null, e.Version, "test", null)).Error); // Submitted -> Assessed is not a move
        var inReview = (await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.InReview, null, e.Version, "test", null)).Value!.Event;
        var asked = (await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.InformationRequest, "When did the rash start?", "test", null)).Value!;
        Assert.Equal(ReviewStatus.InformationRequested, asked.Event.Status);
        var answered = (await svc.AddFollowUpAsync(sara, ReporterRole.Patient, e.Id, "Last Tuesday", "test", null)).Value!;
        Assert.Equal(ReviewStatus.InReview, answered.Event.Status);
        Assert.Contains(answered.Entries, x => x.Kind == EntryKind.FollowUp && x.FromStatus == ReviewStatus.InformationRequested && x.ToStatus == ReviewStatus.InReview);
        Assert.Equal(AdrError.Validation, (await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.Closed, " ", answered.Event.Version, "test", null)).Error);
        var closed = (await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.Closed, "Duplicate of another record", answered.Event.Version, "test", null)).Value!.Event;
        Assert.Equal(AdrError.Validation, (await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.InReview, null, closed.Version, "test", null)).Error); // reopening also needs a reason
        Assert.True((await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.InReview, "Reopened: new information", closed.Version, "test", null)).Succeeded);
    }

    [Fact]
    public async Task What_the_patient_wrote_what_a_professional_noted_and_what_the_template_produced_are_kept_apart()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var e = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(), "test", null)).Value!.Event;
        await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.ReviewNote, "INTERNAL-NOTE-TEXT", "test", null);
        await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.ProfessionalAssessment, "INTERNAL-ASSESSMENT-TEXT", "test", null);
        await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.InformationRequest, "VISIBLE-REQUEST", "test", null);
        await svc.AddFollowUpAsync(sara, ReporterRole.Patient, e.Id, "PATIENT-FOLLOWUP", "test", null);

        var patient = (await svc.GetAsync(e.Id, ReporterRole.Patient)).Value!;
        var json = JsonSerializer.Serialize(patient);
        Assert.DoesNotContain("INTERNAL-NOTE-TEXT", json, StringComparison.Ordinal);
        Assert.DoesNotContain("INTERNAL-ASSESSMENT-TEXT", json, StringComparison.Ordinal);
        Assert.Contains("VISIBLE-REQUEST", json, StringComparison.Ordinal);
        Assert.All(patient.Entries, x => Assert.Null(x.AuthorUserId));
        Assert.Null(patient.Event.Category);
        var pro = (await svc.GetAsync(e.Id, ReporterRole.Physician)).Value!;
        Assert.Contains("INTERNAL-NOTE-TEXT", JsonSerializer.Serialize(pro), StringComparison.Ordinal);
        Assert.Equal("PatientReported", pro.Entries.Single(x => x.Kind == EntryKind.FollowUp).Provenance);
        Assert.Equal("ProfessionalReview", pro.Entries.First(x => x.Kind == EntryKind.ReviewNote).Provenance);
        // a patient cannot write review entries, and another patient cannot follow up on this record
        Assert.Equal(AdrError.Forbidden, (await svc.AddReviewEntryAsync(sara, ReporterRole.Patient, e.Id, EntryKind.ReviewNote, "x", "test", null)).Error);
        Assert.Equal(AdrError.Forbidden, (await svc.AddFollowUpAsync(env.UserId("demo-patient-2"), ReporterRole.Patient, e.Id, "x", "test", null)).Error);
        Assert.Equal(AdrError.Validation, (await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.Classification, "x", "test", null)).Error); // classification only through the classify call
    }

    [Fact]
    public async Task The_assistance_is_a_labelled_template_over_the_record_never_stored_and_never_a_conclusion()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var e = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report("Headache after the new tablet", meds: [new(null, "blue tablet")]), "test", null)).Value!.Event;
        Assert.Equal(AdrError.Forbidden, (await svc.AssistAsync(sara, ReporterRole.Patient, e.Id, "test", null)).Error);
        var before = (await svc.GetAsync(e.Id, ReporterRole.Physician)).Value!;
        var a = (await svc.AssistAsync(doctor, ReporterRole.Physician, e.Id, "test", null)).Value!;
        var after = (await svc.GetAsync(e.Id, ReporterRole.Physician)).Value!;
        Assert.Equal(before.Entries.Count, after.Entries.Count); // nothing was written into the record
        Assert.Equal(before.Event.Version, after.Event.Version);
        Assert.Equal(AdverseEventAssistance.OriginLabel, a.Origin);
        Assert.Contains("not a conclusion", a.Label, StringComparison.Ordinal);
        Assert.Contains("not a conclusion", a.Notice, StringComparison.Ordinal);
        Assert.Contains(a.Summary, l => l.FromSection == "original.eventDescription" && l.Text.Contains("Headache after the new tablet", StringComparison.Ordinal));
        Assert.Contains(a.Uncertainty, u => u.Contains("does not show whether any medicine caused", StringComparison.Ordinal));
        Assert.Contains(a.Uncertainty, u => u.Contains("not been verified", StringComparison.Ordinal));
        Assert.Contains("onset.unknown", a.MissingInformation);
        // it asserts nothing the record does not say
        var text = string.Join(' ', a.Summary.Where(l => l.FromSection != "original.eventDescription").Select(l => l.Text)) + string.Join(' ', a.Uncertainty);
        foreach (var banned in new[] { "was caused by", "is caused by", "confirmed", "diagnos", "serious", "defective", "must be reported", "should be submitted", "probable", "definite" })
        {
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Validation_rejects_bad_input_with_codes_and_writes_nothing()
    {
        var (env, svc, sara, _) = await Setup();
        using var _env = env;
        var now = env.Clock.UtcNow;
        async Task<string?> Code(CreateAdverseEventInput i) => (await svc.CreateAsync(sara, ReporterRole.Patient, sara, i, "test", null)).Detail;
        Assert.Contains("description.required", await Code(Report(" ")) ?? "");
        Assert.Contains("description.too_long", await Code(Report(new string('x', 2001))) ?? "");
        Assert.Contains("description.invalid_characters", await Code(Report("bad\u0000text")) ?? "");
        Assert.Contains("onset.future", await Code(Report(onset: now.AddDays(1))) ?? "");
        Assert.Contains("resolved.before_onset", await Code(Report(onset: now.AddDays(-1), resolved: now.AddDays(-2))) ?? "");
        Assert.Contains("medications.too_many", await Code(Report(meds: [.. Enumerable.Range(0, 11).Select(i => new SuspectedMedicationInput(null, "m" + i))])) ?? "");
        Assert.Empty((await svc.ListForSubjectAsync(sara, ReporterRole.Physician, 50)).Value!);
        Assert.Equal(AdrError.NotFound, (await svc.CreateAsync(env.UserId("demo-pharmacy-admin"), ReporterRole.Pharmacist, Guid.NewGuid(), Report(), "test", null)).Error); // no such patient
    }

    [Fact]
    public async Task A_retried_request_returns_the_record_it_already_made()
    {
        var (env, svc, sara, _) = await Setup();
        using var _env = env;
        var a = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(cid: "req-1"), "test", null)).Value!.Event;
        var b = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(cid: "req-1"), "test", null)).Value!.Event;
        Assert.Equal(a.Id, b.Id);
        Assert.Single((await svc.ListForSubjectAsync(sara, ReporterRole.Patient, 50)).Value!);
        Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.AdrRecordCreated)));
    }

    [Fact]
    public async Task Audit_records_who_did_what_with_ids_and_names_only_never_the_text_of_a_report()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var e = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report("Zzzsecretrash on my back", meds: [new(null, "Zzzsecretol")]), "test", "corr-1")).Value!.Event;
        await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.ReviewNote, "Zzzsecretnote", "test", null);
        await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.AdverseEvent, "Zzzsecretreason", e.Version + 1, "test", null);
        await svc.SetStatusAsync(doctor, ReporterRole.Physician, e.Id, ReviewStatus.InReview, null, e.Version + 2, "test", null);
        await svc.AssistAsync(doctor, ReporterRole.Physician, e.Id, "test", null);
        var all = JsonSerializer.Serialize(await env.Audit.QueryAsync(new AuditQuery(Take: 1000)));
        Assert.DoesNotContain("Zzzsecret", all, StringComparison.Ordinal);
        foreach (var action in new[] { AuditActions.AdrRecordCreated, AuditActions.AdrEntryAdded, AuditActions.AdrClassified, AuditActions.AdrStatusChanged, AuditActions.AdrAssistanceGenerated })
        {
            Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: action)), x => x.SubjectUserId == sara && x.ResourceId == e.Id.ToString());
        }
    }

    [Fact]
    public async Task Records_of_one_patient_never_appear_in_another_patients_list()
    {
        var (env, svc, sara, _) = await Setup();
        using var _ = env;
        var other = await env.Patient("demo-patient-2");
        await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(), "test", null);
        Assert.Empty((await svc.ListForSubjectAsync(other, ReporterRole.Patient, 50)).Value!);
        Assert.Single((await svc.ListForSubjectAsync(sara, ReporterRole.Patient, 50)).Value!);
        Assert.Null(await svc.SubjectOfAsync(Guid.NewGuid()));
        Assert.Equal(AdrError.NotFound, (await svc.GetAsync(Guid.NewGuid(), ReporterRole.Physician)).Error);
    }

    [Fact]
    public void The_status_machine_has_no_move_that_skips_review()
    {
        Assert.False(AdverseEventService.MoveAllowed(ReviewStatus.Submitted, ReviewStatus.Assessed));
        Assert.False(AdverseEventService.MoveAllowed(ReviewStatus.Closed, ReviewStatus.Assessed));
        Assert.False(AdverseEventService.MoveAllowed(ReviewStatus.Assessed, ReviewStatus.Submitted));
        Assert.True(AdverseEventService.MoveAllowed(ReviewStatus.InReview, ReviewStatus.Assessed));
    }

    [Fact]
    public async Task Dose_and_other_medicines_are_kept_as_reported_unknown_stays_unknown_and_not_named_is_not_none_taken()
    {
        var (env, svc, sara, doctor) = await Setup();
        using var _ = env;
        var bare = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(), "test", null)).Value!.Event;
        Assert.Contains("dose.not_stated", bare.MissingInformation);
        Assert.Contains("concomitant.not_stated", bare.MissingInformation);
        Assert.Empty(bare.ConcomitantMedications);
        var help = (await svc.AssistAsync(doctor, ReporterRole.Physician, bare.Id, "test", null)).Value!;
        Assert.Contains(help.Summary, l => l.Text.Contains("none named (this is not the same as none taken)", StringComparison.Ordinal));

        var mine = await env.TakeMedication(sara);
        var theirs = await env.TakeMedication(await env.Patient("demo-patient-2"));
        var full = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report(cid: "d-1") with { DoseAndAdministrationAsReported = "one tablet in the morning, I think", ConcomitantMedications = [new(mine.Id, null), new(null, "a herbal tea")] }, "test", null)).Value!.Event;
        Assert.Equal("one tablet in the morning, I think", full.Original.DoseAndAdministrationAsReported);
        Assert.Equal(2, full.ConcomitantMedications.Count);
        Assert.Equal(MedicationReferenceState.Linked, full.ConcomitantMedications[0].State);
        Assert.Equal(MedicationReferenceState.ReportedNameOnly, full.ConcomitantMedications[1].State);
        Assert.DoesNotContain("dose.not_stated", full.MissingInformation);
        Assert.DoesNotContain("concomitant.not_stated", full.MissingInformation);
        // concomitant medicines are not "suspected": the two lists are separate, and the other patient's record can never be linked
        Assert.Empty(full.SuspectedMedications);
        Assert.Equal("medication.not_found", (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report() with { ConcomitantMedications = [new(theirs.Id, null)] }, "test", null)).Detail);
        Assert.Contains("concomitant.too_many", (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report() with { ConcomitantMedications = [.. Enumerable.Range(0, 11).Select(i => new SuspectedMedicationInput(null, "m" + i))] }, "test", null)).Detail ?? "");
        Assert.Contains("dose.too_long", (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report() with { DoseAndAdministrationAsReported = new string('x', 501) }, "test", null)).Detail ?? "");
        // the original (including the dose text) never changes afterwards
        await svc.AddFollowUpAsync(sara, ReporterRole.Patient, full.Id, "More later", "test", null);
        Assert.Equal(full.Original, (await svc.GetAsync(full.Id, ReporterRole.Physician)).Value!.Event.Original);
    }

    [PostgresFact]
    public async Task On_PostgreSQL_records_and_their_history_survive_a_restart_and_the_unique_request_id_holds()
    {
        using var first = new PEnv();
        var svc = first.Get<IAdverseEventService>();
        var sara = await first.Patient("demo-patient");
        var doctor = first.UserId("demo-physician");
        var e = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report("Persisted rash", cid: "pg-1"), "t", null)).Value!.Event;
        var same = (await svc.CreateAsync(sara, ReporterRole.Patient, sara, Report("Persisted rash", cid: "pg-1"), "t", null)).Value!.Event;
        Assert.Equal(e.Id, same.Id);
        await svc.AddReviewEntryAsync(doctor, ReporterRole.Physician, e.Id, EntryKind.ReviewNote, "kept", "t", null);
        var v = (await svc.GetAsync(e.Id, ReporterRole.Physician)).Value!.Event.Version;
        await svc.ClassifyAsync(doctor, ReporterRole.Physician, e.Id, EventCategory.AdverseEvent, "reason", v, "t", null);

        using var second = first.Restart();
        var after = (await second.Get<IAdverseEventService>().GetAsync(e.Id, ReporterRole.Physician)).Value!;
        Assert.Equal("Persisted rash", after.Event.Original.EventDescription);
        Assert.Equal(EventCategory.AdverseEvent, after.Event.Category);
        Assert.Equal(2, after.Entries.Count);
        Assert.Equal(sara, await second.Get<IAdverseEventService>().SubjectOfAsync(e.Id));
    }
}
