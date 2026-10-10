using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>Small builders for synthetic snapshots (used by the demonstration rules' own test cases and by tests). All data is fictional.</summary>
public static class SnapshotFactory
{
    public static readonly DateTimeOffset FixtureNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    public static InputState Input(string category, int count = 1, bool stale = false, InputAvailability availability = InputAvailability.Available) =>
        new(category, availability, availability == InputAvailability.Available ? FixtureNow.AddDays(-1) : null, count, stale, 180, "fixture");

    public static IReadOnlyList<InputState> AllInputs(bool stale = false) => [Input("medications", 2, stale), Input("allergies", 1, stale), Input("symptoms", 1, stale), Input("profile", 1, stale)];

    public static SnapshotMedication Med(int n, string name, int[]? ingredients = null, bool registered = true, bool referenceFound = true, bool active = true) =>
        new(G(n), registered ? G(1000 + n) : null, name, registered, active, referenceFound, [.. (ingredients ?? [100 + n]).Select(G)]);

    public static SnapshotAllergy Allergy(int n, string kind, int? referenceMedication, int[]? ingredients = null, bool referenceFound = true) =>
        new(G(2000 + n), kind, referenceMedication is null ? null : G(1000 + referenceMedication.Value), referenceFound, [.. (ingredients ?? []).Select(G)], "fictional substance", "Moderate");

    public static SnapshotInteraction Interaction(int ingredientA, int ingredientB, string severity, string validation = "Validated") =>
        new(G(ingredientA), G(ingredientB), severity, validation, new EvidenceRef("fixture-reference", "Fictional reference fixture", "0", null, validation == "Validated" ? EvidenceValidation.Validated : EvidenceValidation.Demo, "none", null));

    public static AssessmentSnapshot Make(IReadOnlyList<InputState>? inputs = null, IReadOnlyList<SnapshotMedication>? meds = null, IReadOnlyList<SnapshotAllergy>? allergies = null,
        IReadOnlyList<SnapshotSymptom>? symptoms = null, IReadOnlyList<SnapshotInteraction>? interactions = null, int? ageYears = 40, DateTimeOffset? at = null) =>
        new(at ?? FixtureNow, ageYears, inputs ?? AllInputs(), meds ?? [], allergies ?? [], symptoms ?? [], interactions ?? []);

    public static SnapshotSymptom Symptom(int n, string severity, int daysAgo, bool resolved = false) =>
        new(G(3000 + n), severity, FixtureNow.AddDays(-daysAgo), resolved ? FixtureNow.AddDays(-daysAgo + 1) : null);
}

/// <summary>
/// The built-in DEMONSTRATION rules. They exist to show the engine, the findings and the screens working end to end. They are NOT clinical knowledge:
/// they have no real source, no reviewer and no approval, they can never become approved, and they only run where the environment explicitly allows
/// demonstration rules. Each rule's evidence is the single honest entry "demo fixture, no source".
/// </summary>
public static class DemoRules
{
    public const string Author = "demo-seed";

    public static readonly EvidenceRef NoSource = new("demo-fixture", "DEMO rule (no clinical source - not clinically validated)", "0", null, EvidenceValidation.Demo, "none", null);

    private static readonly string[] Common = ["DEMO rule: not clinically validated.", "Works only on information recorded in this system."];

    public static IReadOnlyList<RuleRecord> All(DateTimeOffset now) => [.. new[] { Interaction(), Allergy(), Duplicate(), Unregistered(), Symptom() }.Select(d =>
        new RuleRecord(d, new RuleLifecycle(RuleStatus.Draft, true, Author, now, null, null, null, null, null)))];

    private static RuleDefinition Make(string id, string title, string purpose, RuleDomain domain, string[] inputs, RuleCriteria criteria, FindingSeverity sev, FindingUrgency urg, string template, string[] limits, RuleTestCase[] tests) =>
        new(id, 1, title, purpose, domain, new RulePopulation(null, null, ["Children and pregnancy are not specially handled."]), inputs, criteria, sev, urg, template, null, [NoSource], [.. Common, .. limits], tests);

    private static RuleDefinition Interaction()
    {
        return Make("DEMO-INT-001", "DEMO: interaction entry listed in the reference", "Notes when two medicines the patient takes are listed as interacting in the medication reference.",
            RuleDomain.Interaction, ["medications"], new RuleCriteria(RuleCriteriaKind.ReferenceInteraction, "Moderate"), FindingSeverity.Moderate, FindingUrgency.Soon, "safety.interaction",
            ["Matches reference entries by ingredient only; clinical relevance for this patient is not assessed."],
            [
                new RuleTestCase("listed pair matches", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11]), SnapshotFactory.Med(2, "Nocturin", [12])], interactions: [SnapshotFactory.Interaction(11, 12, "Moderate")]), RuleOutcome.Matched, 1),
                new RuleTestCase("pair without entry does not match", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11]), SnapshotFactory.Med(2, "Nocturin", [12])]), RuleOutcome.NoMatch, 0),
                new RuleTestCase("unregistered medicine is missing data, not no-match", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11]), SnapshotFactory.Med(2, "Unknownol", registered: false)]), RuleOutcome.MissingData, 0),
                new RuleTestCase("never recorded medications are missing data", SnapshotFactory.Make(inputs: [SnapshotFactory.Input("medications", 0, availability: InputAvailability.NeverRecorded)]), RuleOutcome.MissingData, 0),
            ]);
    }

    private static RuleDefinition Allergy()
    {
        return Make("DEMO-ALG-001", "DEMO: recorded allergy matches a medicine taken", "Notes when an allergy linked to a reference medicine is the same as, or shares an ingredient with, a medicine the patient takes.",
            RuleDomain.AllergyConflict, ["allergies", "medications"], new RuleCriteria(RuleCriteriaKind.AllergyMedicationMatch), FindingSeverity.Major, FindingUrgency.Soon, "safety.allergy_conflict",
            ["Only allergies linked to a reference medicine can be matched; free-text allergies are not checked."],
            [
                new RuleTestCase("same reference medicine matches", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11])], allergies: [SnapshotFactory.Allergy(1, "Medication", 1)]), RuleOutcome.Matched, 1),
                new RuleTestCase("shared ingredient matches", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11])], allergies: [SnapshotFactory.Allergy(1, "Medication", 7, [11])]), RuleOutcome.Matched, 1),
                new RuleTestCase("different medicine does not match", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11])], allergies: [SnapshotFactory.Allergy(1, "Medication", 7, [77])]), RuleOutcome.NoMatch, 0),
                new RuleTestCase("only free-text allergy cannot be checked", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11])], allergies: [SnapshotFactory.Allergy(1, "Other", null)]), RuleOutcome.InsufficientEvidence, 0),
            ]);
    }

    private static RuleDefinition Duplicate()
    {
        return Make("DEMO-DUP-001", "DEMO: two medicines share an ingredient", "Notes when two medicines the patient takes contain the same ingredient according to the reference.",
            RuleDomain.DuplicateIngredient, ["medications"], new RuleCriteria(RuleCriteriaKind.DuplicateIngredient), FindingSeverity.Moderate, FindingUrgency.Routine, "safety.duplicate_ingredient",
            ["Matches by ingredient id only; names are never compared."],
            [
                new RuleTestCase("shared ingredient matches", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11]), SnapshotFactory.Med(2, "Demopril Plus", [11, 12])]), RuleOutcome.Matched, 1),
                new RuleTestCase("distinct ingredients do not match", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11]), SnapshotFactory.Med(2, "Nocturin", [12])]), RuleOutcome.NoMatch, 0),
            ]);
    }

    private static RuleDefinition Unregistered()
    {
        return Make("DEMO-DQ-001", "DEMO: medicine could not be checked", "Notes medicines that are not in the medication reference, so other checks could not cover them.",
            RuleDomain.DataQuality, ["medications"], new RuleCriteria(RuleCriteriaKind.UnregisteredMedication), FindingSeverity.Informational, FindingUrgency.Routine, "safety.unregistered_medication",
            ["This is a data-quality note; it says nothing about the medicine itself."],
            [
                new RuleTestCase("unregistered medicine is noted", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Unknownol", registered: false)]), RuleOutcome.Matched, 1),
                new RuleTestCase("registered medicines are not noted", SnapshotFactory.Make(meds: [SnapshotFactory.Med(1, "Demopril", [11])]), RuleOutcome.NoMatch, 0),
            ]);
    }

    private static RuleDefinition Symptom()
    {
        return Make("DEMO-SYM-001", "DEMO: severe symptom recorded recently", "Notes a symptom the patient recorded as severe and has not marked as resolved. The engine does not interpret it.",
            RuleDomain.ReportedSymptom, ["symptoms"], new RuleCriteria(RuleCriteriaKind.SevereSymptomRecent, WithinDays: 7), FindingSeverity.Moderate, FindingUrgency.Soon, "safety.symptom_reported",
            ["Reports the patient's own rating only; it is not a triage or an assessment of cause."],
            [
                new RuleTestCase("recent severe symptom matches", SnapshotFactory.Make(symptoms: [SnapshotFactory.Symptom(1, "Severe", 2)]), RuleOutcome.Matched, 1),
                new RuleTestCase("old severe symptom does not match", SnapshotFactory.Make(symptoms: [SnapshotFactory.Symptom(1, "Severe", 30)]), RuleOutcome.NoMatch, 0),
                new RuleTestCase("mild symptom does not match", SnapshotFactory.Make(symptoms: [SnapshotFactory.Symptom(1, "Mild", 1)]), RuleOutcome.NoMatch, 0),
                new RuleTestCase("symptoms not readable is missing data", SnapshotFactory.Make(inputs: [SnapshotFactory.Input("symptoms", 0, availability: InputAvailability.NotAuthorized)]), RuleOutcome.MissingData, 0),
            ]);
    }
}
