using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Medications;

/// <summary>
/// Loads FICTIONAL medications (the same invented names as the Phase 2 prototype) so search and detail screens have something to show.
/// Everything here is flagged <c>IsDemo</c>, comes from a Demo-type source, carries no official identifier and no invented clinical
/// claim beyond the fictional sentences already used in the Phase 2 demo data. It can never be marked "verified".
/// </summary>
public sealed class DemoMedicationSeeder(IMedicationRepository repo, IMedicationAdminService admin, IClock clock, IOptions<MedicationsOptions> options) : IDemoMedicationSeeder
{
    public static readonly Guid SeedActor = Guid.Empty;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!options.Value.SeedDemoData)
        {
            throw new InvalidOperationException("Demo medications can only be seeded when Medications:SeedDemoData is enabled.");
        }

        if ((await repo.VisibleMedicationIdsAsync(true, ct)).Count > 0)
        {
            return; // already seeded
        }

        var source = new KnowledgeSource
        {
            Id = Guid.CreateVersion7(), Name = "DEMO seed (fictional)", Publisher = "AI MedSmarter test fixtures", Type = SourceType.Demo, Version = "demo-0.1",
            LicenseName = "Fictional test data - no external licence", RedistributionAllowed = true, UsageRestrictions = "NOT FOR CLINICAL USE",
            ReceivedAt = clock.UtcNow, Validation = ValidationStatus.Demo,
        };
        await repo.AddSourceAsync(source, ct);
        var rev = new KnowledgeRevision { Id = Guid.CreateVersion7(), SourceId = source.Id, Label = "demo-0.1", ReceivedAt = clock.UtcNow, Status = RevisionStatus.Draft, Notes = "Fictional fixtures" };
        await repo.AddRevisionAsync(rev, ct);

        async Task<ReferenceTermDto> Term(ReferenceKind k, string code, string en, string fa) => (await admin.CreateReferenceTermAsync(SeedActor, new NewReferenceTerm(k, code, new LocalizedText(en, fa)), ct)).Value!;
        await Term(ReferenceKind.DosageForm, "tablet", "Tablet", "قرص");
        await Term(ReferenceKind.DosageForm, "capsule", "Capsule", "کپسول");
        await Term(ReferenceKind.DosageForm, "inhaler", "Inhaler", "اسپری استنشاقی");
        await Term(ReferenceKind.Route, "oral", "Oral", "خوراکی");
        await Term(ReferenceKind.Route, "inhalation", "Inhalation", "استنشاقی");
        await Term(ReferenceKind.TherapeuticClass, "DEMO-HEART", "Demo heart-health class", "گروه نمایشی سلامت قلب");
        await Term(ReferenceKind.TherapeuticClass, "DEMO-METABOLIC", "Demo metabolic class", "گروه نمایشی متابولیک");
        await Term(ReferenceKind.TherapeuticClass, "DEMO-BREATHING", "Demo breathing-support class", "گروه نمایشی حمایت تنفسی");
        await Term(ReferenceKind.TherapeuticClass, "DEMO-SLEEP", "Demo sleep-support class", "گروه نمایشی حمایت خواب");
        await Term(ReferenceKind.TherapeuticClass, "DEMO-SUPPLEMENT", "Demo supplement class", "گروه نمایشی مکمل");

        var maker = (await admin.CreateManufacturerAsync(SeedActor, new NewManufacturer(new LocalizedText("DemoPharma A (fictional)", "دموفارما الف (فرضی)"), null, null), ct)).Value!;
        async Task<Guid> Ing(string en, string fa, params string[] syn) =>
            (await admin.CreateIngredientAsync(SeedActor, new NewIngredient(new LocalizedText(en, fa), null, syn), ct)).Value!.Id;
        async Task<Guid> Brand(string en, string fa) => (await admin.CreateBrandAsync(SeedActor, new NewBrand(new LocalizedText(en, fa), maker.Id), ct)).Value!.Id;

        var demoprilate = await Ing("demoprilate (fictional)", "دموپریلات (فرضی)");
        var glycanorin = await Ing("glycanorin (fictional)", "گلیکانورین (فرضی)", "glycanorine");
        var respirexol = await Ing("respirexol (fictional)", "رسپیرکسول (فرضی)");
        var nocturamide = await Ing("nocturamide (fictional)", "نوکتورامید (فرضی)");
        var solvitol = await Ing("solvitol (fictional)", "سولویتول (فرضی)");

        NewStatement Warning(string en, string fa) => new(StatementKind.Warning, new LocalizedText(en, fa), "info", null, null, rev.Id);
        NewStatement Admin(string en, string fa) => new(StatementKind.Administration, new LocalizedText(en, fa), null, null, null, rev.Id);

        async Task<Guid> Add(string en, string fa, Guid? brand, string form, string route, string cls, (Guid Id, decimal V, string U, string? Per)[] ing, NewStatement[] statements, string[]? synonyms = null, LifecycleStatus lifecycle = LifecycleStatus.Active)
        {
            var draft = new MedicationDraft(new LocalizedText(en, fa), brand, maker.Id, form, [route], [cls],
                [.. ing.Select(i => new NewIngredientRef(i.Id, i.V, i.U, i.Per))], synonyms ?? [], [], statements, true);
            var created = await admin.CreateAsync(SeedActor, draft, "seed", null, ct);
            var detail = created.Value!;
            var active = await admin.SetLifecycleAsync(SeedActor, detail.Id, detail.Version, lifecycle, "seed", null, ct);
            return active.Value!.Id;
        }

        await Add("Demopril", "دموپریل", await Brand("Demopril", "دموپریل"), "tablet", "oral", "DEMO-HEART", [(demoprilate, 10, "mg", null)],
            [Warning("Fictional warning: may cause demo dizziness when standing up quickly.", "هشدار فرضی: ممکن است هنگام برخاستن ناگهانی سرگیجهٔ نمایشی ایجاد کند."),
             Admin("Take one tablet each morning with a glass of water.", "هر صبح یک قرص همراه یک لیوان آب مصرف شود.")]);
        await Add("Demoprilate 10 mg tablet", "قرص دموپریلات ۱۰ میلی‌گرم", null, "tablet", "oral", "DEMO-HEART", [(demoprilate, 10, "mg", null)], [], ["generic demoprilate"]);
        await Add("Duodemo", "دودمو", await Brand("Duodemo", "دودمو"), "tablet", "oral", "DEMO-HEART", [(demoprilate, 10, "mg", null), (nocturamide, 5, "mg", null)], [], ["demoprilate nocturamide combination"]);
        await Add("Glycanor", "گلیکانور", await Brand("Glycanor", "گلیکانور"), "tablet", "oral", "DEMO-METABOLIC", [(glycanorin, 500, "mg", null)],
            [Warning("Fictional warning: take with food to reduce demo stomach upset.", "هشدار فرضی: همراه غذا مصرف شود تا ناراحتی نمایشی معده کمتر شود."),
             Admin("Take one tablet with breakfast and one with dinner.", "یک قرص همراه صبحانه و یک قرص همراه شام مصرف شود.")]);
        await Add("Respirex", "رسپیرکس", await Brand("Respirex", "رسپیرکس"), "inhaler", "inhalation", "DEMO-BREATHING", [(respirexol, 100, "mcg", "puff")],
            [Warning("Fictional warning: do not exceed the demo daily puffs.", "هشدار فرضی: از تعداد پاف نمایشی روزانه بیشتر نشود."),
             Admin("Two puffs in the morning. Rinse your mouth afterwards.", "صبح دو پاف مصرف شود. پس از مصرف دهان شسته شود.")]);
        await Add("Nocturin", "نوکتورین", await Brand("Nocturin", "نوکتورین"), "tablet", "oral", "DEMO-SLEEP", [(nocturamide, 5, "mg", null)],
            [Warning("Fictional warning: may cause demo drowsiness. Avoid driving after taking.", "هشدار فرضی: ممکن است خواب‌آلودگی نمایشی ایجاد کند. پس از مصرف رانندگی نکنید."),
             Admin("Take one tablet at bedtime.", "یک قرص هنگام خواب مصرف شود.")]);
        await Add("Solvita", "سولویتا", await Brand("Solvita", "سولویتا"), "capsule", "oral", "DEMO-SUPPLEMENT", [(solvitol, 1000, "IU", null)],
            [Warning("Fictional warning: demo supplement, keep out of reach of children.", "هشدار فرضی: مکمل نمایشی؛ دور از دسترس کودکان نگه دارید."),
             Admin("Take one capsule with lunch.", "یک کپسول همراه ناهار مصرف شود.")]);
        await Add("Oldmed (inactive demo)", "اولدمد (نمایشی غیرفعال)", null, "capsule", "oral", "DEMO-SUPPLEMENT", [(solvitol, 500, "IU", null)], [], null, LifecycleStatus.Inactive);

        await admin.UpsertInteractionAsync(SeedActor, new NewInteraction(demoprilate, nocturamide, InteractionSeverity.Moderate,
            new LocalizedText("Fictional: taken together, dizziness may feel stronger.", "فرضی: مصرف هم‌زمان ممکن است سرگیجه را بیشتر کند."),
            new LocalizedText("Pharmacist review suggested. Do not change doses on your own.", "بازبینی توسط داروساز توصیه می‌شود. دوزها را خودسرانه تغییر ندهید."), rev.Id), "seed", null, ct);
        await admin.UpsertInteractionAsync(SeedActor, new NewInteraction(glycanorin, solvitol, InteractionSeverity.Minor,
            new LocalizedText("Fictional: minor timing note, no action needed.", "فرضی: نکتهٔ جزئی دربارهٔ زمان مصرف؛ اقدامی لازم نیست."),
            new LocalizedText("Informational only.", "صرفاً اطلاعاتی."), rev.Id), "seed", null, ct);
    }
}
