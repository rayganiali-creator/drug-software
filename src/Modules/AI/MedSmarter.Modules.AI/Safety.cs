using System.Text.RegularExpressions;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI.Contracts;

namespace MedSmarter.Modules.AI;

/// <summary>
/// Text that tries to instruct the assistant ("ignore previous instructions", fake system tags...). It is treated as an attack when it comes from the
/// person, and as a reason to quarantine when it comes from retrieved evidence. A pattern list can never be complete: it is one layer, next to
/// keeping evidence as data, a model without tools or database access, and checking the output. It is not a guarantee.
/// </summary>
public static class InjectionPatterns
{
    private static readonly string[] Phrases =
    [
        "ignore previous", "ignore all previous", "ignore the previous", "ignore the above", "ignore above instructions", "ignore your instructions", "ignore these instructions",
        "disregard previous", "disregard the above", "disregard your instructions", "forget your instructions", "forget previous instructions", "forget all previous", "new instructions",
        "system prompt", "reveal your prompt", "reveal your instructions", "show your instructions", "you are now", "from now on you", "pretend to be", "pretend you are", "act as if",
        "developer mode", "jailbreak", "do anything now", "override your", "override the safety", "bypass your", "bypass the safety", "without restrictions", "no restrictions",
        "دستورات قبلی", "دستورالعمل های قبلی", "دستور قبلی", "دستورات بالا", "دستورهای قبلی", "قوانین را نادیده", "دستورات را نادیده", "دستورالعمل ها را نادیده", "پرامپت سیستم", "دستور سیستم", "دستورالعمل های سیستم",
        "از این پس تو", "حالا تو", "وانمود کن", "بدون هیچ محدودیت",
    ];

    private static readonly string[] Markers = ["<system", "</system", "[system]", "[inst]", "<|", "|>", "### instruction", "### system", "<<sys"];

    private static readonly string[] NormalizedPhrases = [.. Phrases.Select(QueryNormalizer.Normalize)];

    public static bool Looks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = TextFolding.Fold(text).ToLowerInvariant();
        if (Markers.Any(m => lower.Contains(m, StringComparison.Ordinal)))
        {
            return true;
        }

        var padded = $" {QueryNormalizer.Normalize(text)} ";
        return NormalizedPhrases.Any(p => padded.Contains($" {p} ", StringComparison.Ordinal));
    }
}

public enum QuestionClass
{
    None,
    /// <summary>Possible warning signs: answered with a fixed urgent message, never by a model.</summary>
    Emergency,
    /// <summary>The text tries to change the assistant's rules.</summary>
    PolicyOverride,
    /// <summary>Asks whether to start, stop or change a medicine or its dose: a decision for the person and their prescriber.</summary>
    MedicationChange,
    /// <summary>Asks the assistant to say what illness the person has.</summary>
    Diagnosis,
}

/// <summary>
/// Deterministic screening of the question BEFORE any retrieval or model call. The emergency list is a conservative keyword screen written for this
/// phase: it errs towards escalating (a negated phrase such as "no chest pain" also escalates), it is not clinical triage, and it will not catch every
/// emergency. The independent clinical safety engine belongs to Phase 7 and must not be replaced by this list or by any prompt.
/// </summary>
public static partial class QuestionScreen
{
    private static readonly string[] EmergencyPhrases =
    [
        "can t breathe", "cannot breathe", "couldn t breathe", "can t catch my breath", "trouble breathing", "difficulty breathing", "hard to breathe", "short of breath", "shortness of breath", "stopped breathing",
        "chest pain", "chest tightness", "pain in my chest", "swelling of the face", "swollen face", "swelling of the throat", "throat swelling", "swollen tongue", "swollen lips", "anaphylaxis", "allergic shock",
        "fainted", "fainting", "passed out", "unconscious", "not waking", "won t wake", "seizure", "convulsion", "overdose", "took too many", "took too much", "took an extra",
        "suicid", "kill myself", "end my life", "want to die", "harm myself", "hurt myself", "severe bleeding", "bleeding heavily", "vomiting blood", "vomited blood", "coughing blood", "blood in my vomit",
        "stroke", "face drooping", "slurred speech", "sudden weakness",
        "تنگی نفس", "نفس نمی توانم", "نمی توانم نفس", "نمیتوانم نفس", "نمی تونم نفس", "نمیتونم نفس", "سخت نفس", "نفس کشیدن سخت", "درد قفسه سینه", "درد سینه", "تورم صورت", "تورم گلو", "تورم زبان", "تورم لب",
        "آنافیلاکسی", "شوک حساسیتی", "غش کرد", "غش کردم", "غش می کنم", "بیهوش", "به هوش نمی", "تشنج", "مصرف بیش از حد", "اوردوز", "اور دوز", "بیش از حد دارو خوردم", "خودکشی", "خودم را بکشم", "می خواهم بمیرم", "میخوام بمیرم",
        "خونریزی شدید", "استفراغ خون", "خون استفراغ", "سرفه خونی", "سکته", "کج شدن صورت", "حرف زدن مشکل", "ضعف ناگهانی",
    ];

    private static readonly string[] NormalizedEmergency = [.. EmergencyPhrases.Select(QueryNormalizer.Normalize)];

    // "stop / skip / double / increase / reduce / change ... my dose / medication" and "should I stop ..." in English, and the usual Persian phrasings.
    [GeneratedRegex(@"\b(stop|stopping|skip|skipping|discontinue|discontinuing|quit|quitting|double|doubling|increase|increasing|reduce|reducing|lower|lowering|halve|change|changing|adjust|adjusting|switch|switching|swap|swapping|come off)\b.{0,30}\b(dose|dosage|medication|medications|medicine|medicines|meds|pills|tablet|tablets|treatment|prescription|drug)\b")]
    private static partial Regex ChangeEn();

    [GeneratedRegex(@"\b(can|should|may|could) i (stop|skip|quit|double|increase|reduce|lower|halve|change|take more|take less|start)\b")]
    private static partial Regex ChangeEnQuestion();

    [GeneratedRegex(@"(قطع (کنم|کردن|مصرف)|مصرف (دارو )?را قطع|دوز(ش)? را (زیاد|کم|بیشتر|کمتر|دو برابر|تغییر)|دارو را (عوض|کم|زیاد|قطع|ترک)|تغییر دوز|مصرف نکنم|نخورم|ادامه ندهم|ادامه ندم|کم کنم|زیاد کنم)")]
    private static partial Regex ChangeFa();

    [GeneratedRegex(@"\b(do i have(?! to\b)|do you think i have|what do i have|what illness do i have|what disease do i have|diagnose me|diagnose my|diagnose this|am i suffering from|what is wrong with me|is it cancer)\b")]
    private static partial Regex DiagnosisEn();

    [GeneratedRegex(@"(تشخیص (بده|بدهید|بدین)|چه بیماری (دارم|ای دارم)|مبتلا (شده ام|هستم)|مشکل من چیست|بیماری من چیست)")]
    private static partial Regex DiagnosisFa();

    public static QuestionClass Classify(string question)
    {
        var normalized = QueryNormalizer.Normalize(question);
        var padded = $" {normalized} ";
        if (NormalizedEmergency.Any(p => padded.Contains($" {p}", StringComparison.Ordinal)))
        {
            return QuestionClass.Emergency; // first: nothing outranks a possible emergency, not even an attempt to change the rules
        }

        if (InjectionPatterns.Looks(question))
        {
            return QuestionClass.PolicyOverride;
        }

        if (ChangeEn().IsMatch(normalized) || ChangeEnQuestion().IsMatch(normalized) || ChangeFa().IsMatch(normalized))
        {
            return QuestionClass.MedicationChange;
        }

        return DiagnosisEn().IsMatch(normalized) || DiagnosisFa().IsMatch(normalized) ? QuestionClass.Diagnosis : QuestionClass.None;
    }
}

/// <summary>
/// Checks generated text before it reaches a patient: calm, non-diagnostic, no unsupported numbers, no instruction to start/stop/change a medicine,
/// no contact data, and no citation of evidence that was not supplied. A violation withholds the text (the evidence is still shown). It does not
/// make text clinically correct; it only enforces the boundaries this product promises.
/// </summary>
public static partial class AnswerSafetyPolicy
{
    private static readonly string[] ScareEn = ["deadly", "fatal", "lethal", "life-threatening", "catastrophic", "terrifying", "horrible", "horrifying", "dying", "you will die", "you could die", "you may die", "irreversible", "panic", "alarming", "severe damage"];
    private static readonly string[] ScareFa = ["کشنده", "مرگبار", "مرگ", "فاجعه", "وحشتناک", "ترسناک", "غیرقابل جبران", "هولناک", "بمیرید", "جان‌تان در خطر", "جانتان در خطر"];
    private static readonly string[] DiagnosisEn = ["you have been diagnosed", "diagnosed with", "you are suffering from", "you suffer from", "you definitely have", "you probably have", "you likely have", "this is caused by", "the cause is", "you have an infection", "you have a disease"];
    private static readonly string[] DiagnosisFa = ["مبتلا هستید", "مبتلا شده‌اید", "مبتلا شده اید", "شما دچار", "تشخیص قطعی", "علت آن", "بیماری شما"];
    private static readonly string[] CertaintyEn = ["definitely", "certainly", "guaranteed", "guarantee", "without doubt", "absolutely safe", "completely safe", "totally safe", "no risk", "risk-free", "100%"];
    private static readonly string[] CertaintyFa = ["قطعاً", "حتماً", "بدون هیچ خطر", "کاملاً بی‌خطر", "کاملا بی خطر", "تضمین"];

    [GeneratedRegex(@"\b(stop|stopping|skip|skipping|discontinue|quit|double|increase|reduce|lower|halve|change|adjust|switch|swap)\b.{0,30}\b(dose|dosage|medication|medications|medicine|medicines|pills|tablet|tablets|treatment|prescription)\b|\b(start|begin) (taking|using)\b|\btake (more|less|extra)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeEn();

    [GeneratedRegex(@"(مصرف (دارو )?را (قطع|متوقف)|دارو را (قطع|متوقف|عوض|کم|زیاد)|قطع کنید|دوز(ش)? را (افزایش|کاهش|زیاد|کم|دو برابر|تغییر)|شروع کنید به مصرف|شروع به مصرف)")]
    private static partial Regex ChangeFa();

    [GeneratedRegex(@"\d+([.,٫]\d+)?\s*(%|٪|percent|per cent|درصد)|\b\d+\s*(in|out of)\s*\d+\b|\b(odds|chance of|probability|likelihood)\b|احتمال \d|شانس \d", RegexOptions.IgnoreCase)]
    private static partial Regex Numbers();

    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex Citation();

    public const int MaxLength = 4000;

    public static IReadOnlyList<string> Check(string? text, IReadOnlyCollection<string> evidenceIds)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ["empty"];
        }

        if (text.Length > MaxLength)
        {
            violations.Add("too_long");
        }

        var lower = text.ToLowerInvariant();
        Add(violations, "scare_language", ScareEn.Concat(ScareFa), lower);
        Add(violations, "diagnosis", DiagnosisEn.Concat(DiagnosisFa), lower);
        Add(violations, "overconfidence", CertaintyEn.Concat(CertaintyFa), lower);
        if (ChangeEn().IsMatch(text) || ChangeFa().IsMatch(text))
        {
            violations.Add("medication_change_instruction");
        }

        if (Numbers().IsMatch(text))
        {
            violations.Add("unsupported_numbers");
        }

        if (FreeTextGuard.Problem(text, "answer", int.MaxValue) is not null || InjectionPatterns.Looks(text))
        {
            violations.Add("contact_or_instruction_text");
        }

        if (Citation().Matches(text).Any(m => !evidenceIds.Contains(m.Groups[1].Value)))
        {
            violations.Add("unknown_citation");
        }

        return violations;
    }

    private static void Add(List<string> violations, string code, IEnumerable<string> words, string lower)
    {
        if (words.Any(w => lower.Contains(w, StringComparison.Ordinal)))
        {
            violations.Add(code);
        }
    }
}

/// <summary>Messages written by the backend (reviewed text, fa/en), used where a model must not decide the wording: refusals, escalation, no evidence. No numbers are invented: the person is pointed to their local emergency number.</summary>
public static class SafeMessages
{
    public static string NoEvidence(string locale) => IsFa(locale)
        ? "درباره‌ی این پرسش اطلاعاتی در پایگاه دانش دارویی ما پیدا نشد، بنابراین چیزی نمی‌توانم بگویم که بر منبع تکیه داشته باشد. بهتر است از داروساز یا پزشک خود بپرسید."
        : "I could not find anything about this in our medication knowledge base, so I can't say anything that rests on a source. A pharmacist or doctor is the best person to ask.";

    public static string Emergency(string locale) => IsFa(locale)
        ? "آنچه نوشته‌اید می‌تواند نشانه‌ی وضعیتی باشد که به کمک فوری نیاز دارد. لطفاً همین حالا با شماره‌ی اورژانس محل زندگی‌تان تماس بگیرید یا به نزدیک‌ترین مرکز اورژانس بروید. اگر کسی کنار شماست، از او بخواهید همراهتان بماند. این دستیار نمی‌تواند وضعیت اورژانسی را ارزیابی کند."
        : "What you describe can be a sign of something that needs help right away. Please call your local emergency number now, or go to the nearest emergency service. If someone is with you, ask them to stay with you. This assistant cannot assess an emergency.";

    public static string MedicationChange(string locale) => IsFa(locale)
        ? "شروع، قطع یا تغییر دارو و دوز آن تصمیمی است که باید با پزشک یا داروساز خودتان بگیرید؛ من نمی‌توانم در این‌باره راهنمایی کنم. اگر دارو برایتان مشکلی ایجاد کرده، آن را یادداشت کنید و در اولین فرصت به آن‌ها بگویید. اگر حالتان ناگهان بد شد، با اورژانس تماس بگیرید."
        : "Whether to take a medicine, and at what dose, is a decision to make with your doctor or pharmacist, so I can't advise on it. If a medicine is causing you trouble, note it down and tell them as soon as you can. If you suddenly feel very unwell, call your local emergency number.";

    public static string Diagnosis(string locale) => IsFa(locale)
        ? "تشخیص بیماری کار پزشک است و من نمی‌توانم آن را انجام دهم. می‌توانم اطلاعات مرجع دارویی را نشان دهم؛ برای ارزیابی وضعیت خودتان لطفاً با پزشک یا داروساز صحبت کنید."
        : "Working out what is causing symptoms is a job for a doctor, and I can't do it. I can show reference information about a medicine; to look at your own situation, please talk to a doctor or pharmacist.";

    public static string PolicyOverride(string locale) => IsFa(locale)
        ? "نمی‌توانم قواعد ایمنی این دستیار را تغییر دهم. اگر پرسشی درباره‌ی یک دارو دارید، با کمال میل از روی منابع پاسخ می‌دهم."
        : "I can't change the safety rules of this assistant. If you have a question about a medicine, I'm happy to answer it from the sources.";

    public static string Withheld(string locale) => IsFa(locale)
        ? "پاسخ تولیدشده از بررسی ایمنی ما نگذشت و نمایش داده نمی‌شود. منابعی که پیدا شد در زیر آمده است؛ برای تفسیر آن‌ها لطفاً با داروساز یا پزشک خود صحبت کنید."
        : "The generated answer did not pass our safety check, so it is not shown. The sources that were found are listed below; please talk to a pharmacist or doctor to interpret them.";

    public static string Unavailable(string locale) => IsFa(locale)
        ? "در حال حاضر نمی‌توانم پاسخ تولید کنم. منابعی که پیدا شد در زیر آمده است."
        : "I can't generate an answer right now. The sources that were found are listed below.";

    public static string ExternalNotAuthorized(string locale) => IsFa(locale)
        ? "برای تولید پاسخ با سرویس بیرونی، شرایط مجوز (از جمله رضایت شما) برقرار نیست؛ بنابراین چیزی ارسال نشد. منابعی که پیدا شد در زیر آمده است."
        : "The conditions for using an outside service (including your consent) are not met, so nothing was sent. The sources that were found are listed below.";

    private static bool IsFa(string locale) => locale.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
}
