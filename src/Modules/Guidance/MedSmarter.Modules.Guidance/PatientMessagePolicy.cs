using System.Text.RegularExpressions;
using MedSmarter.Modules.Guidance.Contracts;

namespace MedSmarter.Modules.Guidance;

/// <summary>
/// The rules every patient-facing message must satisfy (docs/phase5/05). The policy cannot make a message good; it makes the worst
/// failures impossible to ship: missing parts, scare words, a definite diagnosis, an instruction to change medicines, invented numbers.
/// Honesty is not weakened: an urgent message must still say what to do and which signs need urgent action.
/// </summary>
public static partial class PatientMessagePolicy
{
    public const int MaxPartLength = 500;

    // Words that frighten without helping. The calm alternative ("needs attention", "worth a check") is always available.
    private static readonly string[] ForbiddenEn =
    [
        "deadly", "fatal", "lethal", "life-threatening", "dangerous", "catastrophic", "terrifying", "horrible", "toxic", "poison", "dying", "you will die", "irreversible", "panic", "alarming", "severe damage",
    ];

    private static readonly string[] ForbiddenFa =
    [
        "کشنده", "مرگبار", "مرگ", "خطرناک", "فاجعه", "وحشتناک", "ترسناک", "سمی", "مسموم", "غیرقابل جبران", "هولناک", "بمیرید", "جان‌تان در خطر", "جانتان در خطر",
    ];

    // A definite diagnosis is for a professional to make.
    private static readonly string[] DiagnosisEn = ["you have been diagnosed", "diagnosed with", "you are suffering from", "you definitely", "this is caused by", "the cause is", "you suffer from"];

    private static readonly string[] DiagnosisFa = ["مبتلا هستید", "مبتلا شده‌اید", "تشخیص قطعی", "قطعاً", "حتماً به دلیل", "علت آن", "از بیماری"];

    // Starting, stopping or changing a medicine is a decision for the patient and their prescriber.
    private static readonly string[] TreatmentEn = ["stop taking", "stop your", "discontinue", "do not take", "don't take", "increase the dose", "decrease the dose", "reduce the dose", "double the dose", "take more", "skip your", "switch to", "start taking"];

    private static readonly string[] TreatmentFa = ["مصرف را قطع", "دارو را قطع", "قطع کنید", "مصرف نکنید", "دوز را افزایش", "دوز را کاهش", "دو برابر", "تعویض کنید", "شروع کنید به مصرف"];

    [GeneratedRegex(@"\d+([.,٫]\d+)?\s*(%|٪|percent|درصد)", RegexOptions.IgnoreCase)]
    private static partial Regex Percent();

    [GeneratedRegex(@"\b(chance|probability|odds|risk of)\b|احتمال|شانس", RegexOptions.IgnoreCase)]
    private static partial Regex Probability();

    [GeneratedRegex(@"\b[A-Z]{4,}\b")]
    private static partial Regex ShoutedWord();

    public static IReadOnlyList<PolicyViolation> Validate(PatientGuidanceContent c, GuidanceLevel level, string locale)
    {
        var violations = new List<PolicyViolation>();
        var parts = new (string Name, string? Text)[]
        {
            ("observed", c.Observed), ("why", c.WhyItMatters), ("action", c.SuggestedAction), ("consult", c.WhenToConsult), ("urgent", c.UrgentSigns), ("basis", c.BasisAndConfidence),
        };

        foreach (var (name, text) in parts)
        {
            var required = name != "urgent";
            if (string.IsNullOrWhiteSpace(text))
            {
                if (required || level == GuidanceLevel.Urgent)
                {
                    violations.Add(new PolicyViolation(name == "urgent" ? "urgent_signs.required" : "part.missing", name));
                }

                continue;
            }

            if (name == "urgent" && level != GuidanceLevel.Urgent)
            {
                violations.Add(new PolicyViolation("urgent_signs.not_needed", name)); // the section exists only when it is truly needed
            }

            if (text.Length > MaxPartLength)
            {
                violations.Add(new PolicyViolation("part.too_long", name));
            }

            var lower = text.ToLowerInvariant();
            Add(violations, name, "scare_word", ForbiddenEn.Concat(ForbiddenFa), lower);
            Add(violations, name, "diagnosis", DiagnosisEn.Concat(DiagnosisFa), lower);
            Add(violations, name, "treatment_instruction", TreatmentEn.Concat(TreatmentFa), lower);
            if (Percent().IsMatch(text) || Probability().IsMatch(text))
            {
                violations.Add(new PolicyViolation("unsupported_probability", name)); // no numbers or odds without a basis; Phase 5 has none
            }

            // DEMO and MOCK are the labels that must stay visible on fictional content; any other long all-capitals word is shouting.
            if (text.Contains('!', StringComparison.Ordinal) || text.Contains('‼', StringComparison.Ordinal) || ShoutedWord().Matches(text).Any(m => m.Value is not ("DEMO" or "MOCK")))
            {
                violations.Add(new PolicyViolation("tone.shouting", name));
            }

            if (lower.Contains("contraindicat", StringComparison.Ordinal) || lower.Contains("منع مصرف", StringComparison.Ordinal))
            {
                violations.Add(new PolicyViolation("professional_jargon", name));
            }
        }

        if (!locale.StartsWith("fa", StringComparison.OrdinalIgnoreCase) && !locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new PolicyViolation("locale.unsupported", "locale"));
        }

        return violations;
    }

    private static void Add(List<PolicyViolation> violations, string part, string code, IEnumerable<string> terms, string lowerText)
    {
        foreach (var term in terms)
        {
            if (lowerText.Contains(term, StringComparison.Ordinal))
            {
                violations.Add(new PolicyViolation(code, part));
                return;
            }
        }
    }
}
