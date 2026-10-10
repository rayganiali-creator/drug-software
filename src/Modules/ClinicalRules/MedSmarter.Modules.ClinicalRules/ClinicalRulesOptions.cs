namespace MedSmarter.Modules.ClinicalRules;

public sealed class ClinicalRulesOptions
{
    public const string Section = "ClinicalRules";

    /// <summary>Whether built-in DEMONSTRATION rules are evaluated (their findings are never actionable). Development and Testing only; the host refuses it elsewhere.</summary>
    public bool AllowDemonstrationRules { get; set; }

    /// <summary>Whether the demonstration rules are written to the rule store at start-up. Development and Testing only.</summary>
    public bool SeedDemoRules { get; set; }
}
