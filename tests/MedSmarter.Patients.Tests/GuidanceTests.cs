using System.Text.Json;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Guidance;
using MedSmarter.Modules.Guidance.Contracts;

namespace MedSmarter.Patients.Tests;

public class GuidancePolicyTests
{
    private static readonly GuidanceComposer Composer = new();

    private static PatientGuidanceContent Good() => new(
        "You marked a dose as skipped.", "Taking a medicine regularly is how it is meant to work.", "You could set a reminder in the app.",
        "Please mention it to your doctor or pharmacist at your next contact.", null, "Based on: your own log. How sure we are: moderate.");

    private static IEnumerable<string> Codes(PatientGuidanceContent c, GuidanceLevel level = GuidanceLevel.FollowUp, string locale = "en") =>
        PatientMessagePolicy.Validate(c, level, locale).Select(v => v.Code);

    [Fact]
    public void A_complete_calm_message_passes() => Assert.Empty(Codes(Good()));

    [Theory]
    [InlineData("observed")]
    [InlineData("why")]
    [InlineData("action")]
    [InlineData("consult")]
    [InlineData("basis")]
    public void Every_required_part_must_be_present(string part)
    {
        var c = part switch
        {
            "observed" => Good() with { Observed = " " },
            "why" => Good() with { WhyItMatters = "" },
            "action" => Good() with { SuggestedAction = "" },
            "consult" => Good() with { WhenToConsult = "" },
            _ => Good() with { BasisAndConfidence = "" },
        };
        Assert.Contains("part.missing", Codes(c));
    }

    [Fact]
    public void Urgent_messages_must_name_the_signs_and_other_messages_must_not_have_the_section()
    {
        Assert.Contains("urgent_signs.required", Codes(Good(), GuidanceLevel.Urgent));
        Assert.Empty(Codes(Good() with { UrgentSigns = "If you have trouble breathing, call your local emergency number." }, GuidanceLevel.Urgent));
        Assert.Contains("urgent_signs.not_needed", Codes(Good() with { UrgentSigns = "x" }, GuidanceLevel.FollowUp));
    }

    [Theory]
    [InlineData("This is dangerous.")]
    [InlineData("A deadly mix.")]
    [InlineData("Life-threatening outcome.")]
    [InlineData("It is toxic to you.")]
    [InlineData("این ترکیب خطرناک است.")]
    [InlineData("ممکن است کشنده باشد.")]
    public void Scare_words_are_refused_in_either_language(string text) => Assert.Contains("scare_word", Codes(Good() with { Observed = text }));

    [Theory]
    [InlineData("You have been diagnosed with a condition.")]
    [InlineData("You definitely have it.")]
    [InlineData("شما مبتلا هستید.")]
    public void A_diagnosis_is_not_made(string text) => Assert.Contains("diagnosis", Codes(Good() with { Observed = text }));

    [Theory]
    [InlineData("Stop taking Demopril.")]
    [InlineData("Please do not take it with food.")]
    [InlineData("Increase the dose to two.")]
    [InlineData("Skip your evening tablet.")]
    [InlineData("مصرف را قطع کنید.")]
    [InlineData("دوز را افزایش دهید.")]
    public void Starting_stopping_or_changing_a_medicine_is_never_advised(string text) => Assert.Contains("treatment_instruction", Codes(Good() with { SuggestedAction = text }));

    [Theory]
    [InlineData("There is a 20% chance of this.")]
    [InlineData("Probability is high.")]
    [InlineData("risk of 5 percent")]
    [InlineData("احتمال آن ۲۰ درصد است.")]
    [InlineData("شانس زیادی دارد.")]
    public void Numbers_and_odds_without_a_basis_are_refused(string text) => Assert.Contains("unsupported_probability", Codes(Good() with { WhyItMatters = text }));

    [Theory]
    [InlineData("Act now!")]
    [InlineData("THIS NEEDS ATTENTION")]
    public void The_tone_stays_calm(string text) => Assert.Contains("tone.shouting", Codes(Good() with { Observed = text }));

    [Fact]
    public void Professional_jargon_stays_out_of_the_patient_view() => Assert.Contains("professional_jargon", Codes(Good() with { Observed = "This is contraindicated." }));

    [Fact]
    public void Parts_have_a_length_limit() => Assert.Contains("part.too_long", Codes(Good() with { Observed = new string('a', PatientMessagePolicy.MaxPartLength + 1) }));

    [Fact]
    public void Unsupported_languages_are_refused() => Assert.Contains("locale.unsupported", Codes(Good(), locale: "de"));

    [Theory]
    [InlineData("en")]
    [InlineData("fa")]
    public void Every_template_in_every_language_and_level_satisfies_the_policy(string locale)
    {
        foreach (var key in Composer.TemplateKeys)
        {
            foreach (var level in Enum.GetValues<GuidanceLevel>())
            {
                var c = Composer.Compose(new GuidanceRequest(key, level, locale, new Dictionary<string, string>
                {
                    ["medication"] = "Demopril", ["medicationA"] = "Demopril", ["medicationB"] = "Nocturin", ["count"] = "3", ["days"] = "7", ["symptom"] = "headache", ["topic"] = "this", ["severity"] = "Moderate",
                }, GuidanceConfidence.Moderate, "DEMO", true));
                // A template without "urgent signs" text cannot be sent as Urgent: the policy refuses it (that is the rule working).
                var onlyMissingUrgentSigns = level == GuidanceLevel.Urgent && c.Violations.All(v => v.Code == "urgent_signs.required");
                Assert.True(c.IsValid || onlyMissingUrgentSigns, $"{key}/{level}/{locale}: {string.Join(", ", c.Violations.Select(v => v.Code + "@" + v.Part))}");
                Assert.DoesNotContain("{", string.Join("", c.Patient.Observed, c.Patient.WhyItMatters, c.Patient.SuggestedAction, c.Patient.WhenToConsult, c.Patient.BasisAndConfidence), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_samples_cover_every_level_and_both_languages_and_are_all_valid()
    {
        var svc = new GuidanceService(null!, new FakeClock(), null!, Composer, null!);
        foreach (var locale in new[] { "en", "fa" })
        {
            var samples = svc.Samples(locale);
            Assert.All(samples, s => Assert.True(s.IsValid, string.Join(", ", s.Violations.Select(v => v.Code))));
            Assert.Equal(Enum.GetValues<GuidanceLevel>().Order(), samples.Select(s => s.Level).Distinct().Order());
            Assert.All(samples, s => Assert.Contains("DEMO", s.Professional.Basis, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_urgent_message_is_calm_and_still_tells_the_truth()
    {
        var c = Composer.Compose(new GuidanceRequest("symptom.severe_followup", GuidanceLevel.Urgent, "en", new Dictionary<string, string> { ["symptom"] = "shortness of breath" }, GuidanceConfidence.NotAssessable, "DEMO", true));
        Assert.True(c.IsValid);
        Assert.NotNull(c.Patient.UrgentSigns);
        Assert.Contains("today", c.Patient.SuggestedAction, StringComparison.OrdinalIgnoreCase); // a concrete, small action
        Assert.Contains("emergency", c.Patient.UrgentSigns!, StringComparison.OrdinalIgnoreCase); // the real signs are named
        Assert.Contains("we cannot tell yet", c.Patient.BasisAndConfidence, StringComparison.Ordinal); // confidence stated honestly
        Assert.DoesNotContain("!", c.Patient.Observed, StringComparison.Ordinal);
    }

    [Fact]
    public void With_too_little_data_the_message_says_so_instead_of_guessing()
    {
        var c = Composer.Compose(new GuidanceRequest("interaction.review", GuidanceLevel.Urgent, "en", new Dictionary<string, string> { ["topic"] = "your medicines" }, GuidanceConfidence.High, "DEMO", HasSufficientData: false));
        Assert.True(c.IsValid);
        Assert.Equal(GuidanceLevel.Information, c.Level); // an unsupported alarm is downgraded
        Assert.Contains("not have enough information", c.Patient.Observed, StringComparison.Ordinal);
        Assert.Null(c.Patient.UrgentSigns);
        Assert.Equal(GuidanceConfidence.NotAssessable, c.Professional.Confidence);
        Assert.Equal("Not assessed", c.Professional.SeverityLabel);
    }

    [Fact]
    public void The_professional_view_is_separate_technical_and_carries_the_real_severity()
    {
        var c = Composer.Compose(new GuidanceRequest("interaction.review", GuidanceLevel.ReviewSoon, "en", new Dictionary<string, string> { ["medicationA"] = "Demopril", ["medicationB"] = "Nocturin", ["severity"] = "Major" }, GuidanceConfidence.Moderate, "reference X", true));
        Assert.Equal("Interaction entry: Major", c.Professional.SeverityLabel); // the patient text never says "Major"
        Assert.DoesNotContain("Major", JsonSerializer.Serialize(c.Patient), StringComparison.Ordinal);
        Assert.Contains("reference X", c.Professional.Basis, StringComparison.Ordinal);
        Assert.Contains("Clinical relevance for this patient was not assessed", c.Professional.TechnicalDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameters_cannot_smuggle_markup_or_unbounded_text()
    {
        var c = Composer.Compose(new GuidanceRequest("symptom.followup", GuidanceLevel.FollowUp, "en", new Dictionary<string, string> { ["symptom"] = "<script>alert(1)</script>" + new string('x', 200), ["medication"] = "{medication}" }, GuidanceConfidence.Low, "DEMO", true));
        Assert.DoesNotContain("<", c.Patient.Observed, StringComparison.Ordinal);
        Assert.DoesNotContain("{", c.Patient.Observed, StringComparison.Ordinal);
        Assert.True(c.Patient.Observed.Length < 200);
    }

    [Fact]
    public void An_unknown_template_is_an_error_not_a_guess() =>
        Assert.Throws<ArgumentException>(() => Composer.Compose(new GuidanceRequest("made.up", GuidanceLevel.Information, "en", new Dictionary<string, string>(), GuidanceConfidence.Low, "x", true)));
}

public class GuidanceServiceTests
{
    private static GuidanceRequest Skipped(PEnv env, string medication = "Demopril", GuidanceLevel level = GuidanceLevel.FollowUp) =>
        new("adherence.skipped_pattern", level, "en", new Dictionary<string, string> { ["medication"] = medication, ["count"] = "2", ["days"] = "5" }, GuidanceConfidence.Moderate, "DEMO sample", true);

    [Fact]
    public async Task A_message_follows_the_life_cycle_and_every_change_is_audited()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var pharmacist = env.UserId("demo-pharmacist");
        var m = (await env.Guidance.CreateAsync(id, Skipped(env), true, "t", null)).Value!;
        Assert.Equal(GuidanceStatus.Sent, m.Status);
        Assert.True(m.IsDemo);
        Assert.Null(m.Professional); // never in the patient's copy

        Assert.Equal(GuidanceStatus.Seen, (await env.Guidance.SetStatusAsync(id, id, m.Id, GuidanceStatus.Seen, false, "t", null)).Value!.Status);
        Assert.Equal(GuidanceError.Forbidden, (await env.Guidance.SetStatusAsync(id, id, m.Id, GuidanceStatus.Reviewed, false, "t", null)).Error); // a patient cannot mark it as professionally reviewed
        var reviewed = await env.Guidance.SetStatusAsync(pharmacist, id, m.Id, GuidanceStatus.Reviewed, true, "t", null);
        Assert.NotNull(reviewed.Value!.Professional); // professionals see their own view
        Assert.NotNull(reviewed.Value.ReviewedAt);
        Assert.Equal(GuidanceStatus.Referred, (await env.Guidance.SetStatusAsync(pharmacist, id, m.Id, GuidanceStatus.Referred, true, "t", null)).Value!.Status);
        Assert.Equal(GuidanceStatus.Resolved, (await env.Guidance.SetStatusAsync(id, id, m.Id, GuidanceStatus.Resolved, false, "t", null)).Value!.Status);
        Assert.Equal(GuidanceError.Conflict, (await env.Guidance.SetStatusAsync(pharmacist, id, m.Id, GuidanceStatus.Seen, true, "t", null)).Error);

        var audits = (await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Action: AuditActions.GuidanceStatusChanged, Take: 20))).Select(e => e.ReasonCode).Reverse().ToList();
        Assert.Equal(["Seen", "Reviewed", "Referred", "Resolved"], audits);
    }

    [Fact]
    public async Task A_message_that_breaks_the_policy_is_refused_and_never_stored()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        // a parameter that turns the text into advice to stop a medicine
        var r = await env.Guidance.CreateAsync(id, Skipped(env, "Demopril. Stop taking it and it is dangerous"), false, "t", null);
        Assert.Equal(GuidanceError.Validation, r.Error);
        Assert.Contains("treatment_instruction", r.Detail, StringComparison.Ordinal);
        Assert.Contains("scare_word", r.Detail, StringComparison.Ordinal);
        Assert.Empty((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.Equal(GuidanceError.Validation, (await env.Guidance.CreateAsync(id, Skipped(env) with { TemplateKey = "nope" }, false, "t", null)).Error);
    }

    [Fact]
    public async Task Patients_see_only_their_own_messages_and_cannot_change_anyone_elses()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = await env.Patient("demo-patient-2");
        var m = (await env.Guidance.CreateAsync(a, Skipped(env), false, "t", null)).Value!;
        Assert.Empty((await env.Guidance.ListForPatientAsync(b)).Value!);
        Assert.Equal(GuidanceError.NotFound, (await env.Guidance.SetStatusAsync(b, b, m.Id, GuidanceStatus.Seen, false, "t", null)).Error);
        Assert.Single((await env.Guidance.ListForPatientAsync(a)).Value!);
    }

    [Fact]
    public async Task Messages_in_persian_use_persian_text_and_the_same_structure()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var m = (await env.Guidance.CreateAsync(id, Skipped(env) with { Locale = "fa" }, false, "t", null)).Value!;
        Assert.Equal("fa", m.Locale);
        Assert.Contains("انجام نشده", m.Patient.Observed, StringComparison.Ordinal);
        Assert.Contains("مبنا", m.Patient.BasisAndConfidence, StringComparison.Ordinal);
        Assert.Contains("متوسط", m.Patient.BasisAndConfidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_seeded_demo_messages_are_labelled_and_seeding_is_repeatable()
    {
        using var env = new PEnv(seedPatients: true);
        var sara = env.UserId("demo-patient");
        var list = (await env.Guidance.ListForPatientAsync(sara)).Value!;
        Assert.Equal(2, list.Count);
        Assert.All(list, m => Assert.Equal("DEMO DATA - NOT FOR CLINICAL USE", m.Notice));
        await env.Get<IDemoGuidanceSeeder>().SeedAsync();
        Assert.Equal(2, (await env.Guidance.ListForPatientAsync(sara)).Value!.Count);
    }
}
