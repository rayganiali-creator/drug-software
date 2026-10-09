using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedSmarter.Modules.Guidance.Contracts;

namespace MedSmarter.Modules.Guidance;

public sealed partial class GuidanceComposer : IGuidanceComposer
{
    private sealed record PartTexts(string Observed, string Why, string Action, string Consult, string Urgent, string Basis);

    private sealed record ProfessionalTexts(string Summary, string Detail);

    private sealed record Template(Dictionary<string, PartTexts> Patient, Dictionary<string, ProfessionalTexts> Professional, Dictionary<string, string> SeverityLabel);

    private sealed record TemplateFile(Dictionary<string, string> Basis, Dictionary<string, Dictionary<string, string>> Confidence, Dictionary<string, Template> Templates);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TemplateFile _file;

    public GuidanceComposer()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("guidance-templates.json") ?? throw new InvalidOperationException("guidance templates are not embedded");
        _file = JsonSerializer.Deserialize<TemplateFile>(stream, Json) ?? throw new InvalidOperationException("guidance templates are invalid");
    }

    public IReadOnlyList<string> TemplateKeys => [.. _file.Templates.Keys.Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    public GuidanceComposition Compose(GuidanceRequest request)
    {
        var locale = request.Locale.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "fa" : "en";
        // With too little data nothing reliable can be said: the message says so instead of guessing.
        var key = request.HasSufficientData ? request.TemplateKey : "data.insufficient";
        var parameters = request.HasSufficientData ? request.Parameters : new Dictionary<string, string>(request.Parameters) { ["topic"] = request.Parameters.GetValueOrDefault("topic", request.Parameters.GetValueOrDefault("medication", locale == "fa" ? "این موضوع" : "this topic")) };
        var level = request.HasSufficientData ? request.Level : GuidanceLevel.Information;
        var confidence = request.HasSufficientData ? request.Confidence : GuidanceConfidence.NotAssessable;
        if (!_file.Templates.TryGetValue(key, out var template))
        {
            throw new ArgumentException($"unknown guidance template '{request.TemplateKey}'", nameof(request));
        }

        var p = template.Patient[locale];
        var confidenceWord = _file.Confidence[locale][confidence.ToString()];
        var basisLine = Fill(_file.Basis[locale], new Dictionary<string, string> { ["basis"] = request.HasSufficientData ? request.BasisLabel : p.Basis, ["confidence"] = confidenceWord });
        var patient = new PatientGuidanceContent(
            Fill(p.Observed, parameters), Fill(p.Why, parameters), Fill(p.Action, parameters), Fill(p.Consult, parameters),
            level == GuidanceLevel.Urgent && !string.IsNullOrWhiteSpace(p.Urgent) ? Fill(p.Urgent, parameters) : null,
            basisLine);
        var pro = template.Professional[locale];
        var professional = new ProfessionalGuidanceContent(Fill(pro.Summary, parameters), Fill(pro.Detail, parameters), Fill(template.SeverityLabel[locale], parameters), request.BasisLabel, confidence);
        return new GuidanceComposition(patient, professional, level, PatientMessagePolicy.Validate(patient, level, locale));
    }

    /// <summary>Replaces {name} with the parameter value. Values are cleaned so a parameter can never inject structure or markup.</summary>
    private static string Fill(string text, IReadOnlyDictionary<string, string> parameters) =>
        Placeholder().Replace(text, m => parameters.TryGetValue(m.Groups[1].Value, out var v) ? Clean(v) : "…");

    private static string Clean(string value)
    {
        var cleaned = new string([.. value.Where(c => !char.IsControl(c) && c is not '<' and not '>' and not '{' and not '}')]).Trim();
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
