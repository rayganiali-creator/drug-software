using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.AI;

public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary>Disabled (default) | Mock | External | Local.</summary>
    public string Provider { get; set; } = nameof(AiProviderKind.Disabled);

    /// <summary>External gateway base URL. Secret-free; the key below comes from the environment/secret store only.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Never committed, never sent to a client, never logged.</summary>
    public string? ApiKey { get; set; }

    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxTokens { get; set; } = 512;

    /// <summary>The Mock provider is a development tool. Outside Development/Testing it only runs with this explicit opt-in.</summary>
    public bool AllowMockInProduction { get; set; }

    /// <summary>Explicit approval to send questions and medication documents to an external provider. Default off.</summary>
    public bool AllowExternalDataTransfer { get; set; }

    public AiProviderKind Kind => Enum.TryParse<AiProviderKind>(Provider, true, out var k) ? k : AiProviderKind.Disabled;
}

public static class AiGuard
{
    /// <summary>
    /// Refuses unsafe configurations. Returns a warning to log when the Mock provider is deliberately allowed outside development (for a
    /// demo environment); in "Production" it is never allowed, whatever the flag says.
    /// </summary>
    public static string? EnsureSafe(string environmentName, AiOptions options)
    {
        var devLike = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        var production = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
        if (options.AllowMockInProduction && production)
        {
            throw new InvalidOperationException("Ai:AllowMockInProduction must not be enabled in the Production environment: the Mock provider produces no real analysis. Use Ai:Provider=Disabled, or a real provider once approved. Refusing to start.");
        }

        if (options.Kind == AiProviderKind.Mock && !devLike && !options.AllowMockInProduction)
        {
            throw new InvalidOperationException($"Ai:Provider=Mock is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start.");
        }

        if (!Enum.TryParse<AiProviderKind>(options.Provider, true, out _))
        {
            throw new InvalidOperationException("Ai:Provider must be Disabled, Mock, External or Local.");
        }

        return options.Kind == AiProviderKind.Mock && !devLike
            ? $"AI MOCK PROVIDER IS ACTIVE in environment '{environmentName}' (Ai:AllowMockInProduction=true). Answers are NOT produced by a language model. Use only for demonstrations; never for real patients."
            : null;
    }
}

/// <summary>
/// Deterministic, clearly labelled test provider. It performs NO text generation: it lists what the supplied documents say,
/// with their validation status, and says "no information" for anything else.
/// </summary>
public sealed class MockAIProvider : IAIProvider
{
    public const string Label = "[MOCK AI - DEMO ONLY - NOT FOR CLINICAL USE]";
    public string Name => "mock";
    public AiProviderKind Kind => AiProviderKind.Mock;

    public Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.Append(Label).Append(" No language model was used; this lists the supplied source records.\n");
        if (request.Patient is { } patient)
        {
            sb.Append("[Patient context received: ").Append(patient.Medications.Count).Append(" medicines, ").Append(patient.Allergies.Count).Append(" allergies, ").Append(patient.Conditions.Count)
              .Append(" conditions, ").Append(patient.RecentSymptoms.Count).Append(" recent symptoms. No clinical analysis is performed on it.]\n");
        }

        foreach (var d in request.Context)
        {
            sb.Append("\n- ").Append(string.Join(" / ", d.Names)).Append(" (").Append(d.DosageForm).Append(", ").Append(d.Strength.Length == 0 ? "strength not recorded" : d.Strength)
              .Append(") [status: ").Append(d.IsDemo ? "DEMO" : d.Validation.ToString()).Append("]\n");
            sb.Append("  Active ingredients: ").Append(d.Ingredients.Count == 0 ? "information not available" : string.Join(", ", d.Ingredients)).Append('\n');
            if (d.Statements.Count == 0)
            {
                sb.Append("  Warnings, instructions and other statements: information not available.\n");
            }

            foreach (var s in d.Statements)
            {
                sb.Append("  ").Append(s.Kind).Append(": ").Append(s.Text).Append(" [").Append(s.Validation).Append("]\n");
            }

            foreach (var i in d.Interactions)
            {
                sb.Append("  Interaction with ").Append(i.WithIngredient).Append(" (").Append(i.Severity).Append("): ").Append(i.Summary).Append(" [").Append(i.Validation).Append("]\n");
            }
        }

        return Task.FromResult(AiResult.Ok(new AiCompletion(sb.ToString().TrimEnd(), Name, "mock-deterministic-1", true)));
    }
}

/// <summary>
/// Adapter to an external text-generation gateway speaking the neutral MedSmarter JSON contract
/// (POST {BaseUrl}/complete {model, max_tokens, locale, question, documents[]} -> {text, model}). Vendor-specific APIs are reached by
/// putting a small adapter service in front, so no vendor shape leaks into this code. No real call is made unless fully configured AND approved.
/// </summary>
public sealed partial class ExternalAIProvider(HttpClient http, IOptions<AiOptions> options, ILogger<ExternalAIProvider> logger) : IAIProvider
{
    public string Name => "external";
    public AiProviderKind Kind => AiProviderKind.External;

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.AllowExternalDataTransfer)
        {
            return AiResult.Fail(AiErrorCode.NotConfigured, "Sending data to an external provider has not been approved.");
        }

        if (string.IsNullOrWhiteSpace(o.BaseUrl) || string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.Model)
            || !Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http"))
        {
            return AiResult.Fail(AiErrorCode.NotConfigured, "The external AI provider is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 1, 120)));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "complete"))
            {
                Content = JsonContent.Create(new GatewayRequest(o.Model, Math.Min(request.MaxTokens, o.MaxTokens), request.Locale, request.Question, request.Context, request.Patient)),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
            using var response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogStatus(logger, (int)response.StatusCode);
                return (int)response.StatusCode is 408 or 429 or >= 500
                    ? AiResult.Fail(AiErrorCode.Unavailable, "The AI provider is temporarily unavailable.")
                    : AiResult.Fail(AiErrorCode.Rejected, "The AI provider rejected the request.");
            }

            var body = await response.Content.ReadFromJsonAsync<GatewayResponse>(timeout.Token);
            return string.IsNullOrWhiteSpace(body?.Text) || body.Text.Length > 20_000
                ? AiResult.Fail(AiErrorCode.InvalidResponse, "The AI provider returned an unusable response.")
                : AiResult.Ok(new AiCompletion(body.Text, Name, body.Model ?? o.Model, false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiResult.Fail(AiErrorCode.Timeout, "The AI provider did not answer in time.");
        }
        catch (HttpRequestException)
        {
            return AiResult.Fail(AiErrorCode.Unavailable, "The AI provider could not be reached.");
        }
        catch (JsonException)
        {
            return AiResult.Fail(AiErrorCode.InvalidResponse, "The AI provider returned an unusable response.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "External AI provider answered HTTP {Status}")]
    private static partial void LogStatus(ILogger logger, int status);

    private sealed record GatewayRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("locale")] string Locale,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("documents")] IReadOnlyList<MedicationKnowledgeDocument> Documents,
        [property: JsonPropertyName("patient"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MedSmarter.Modules.Patients.Contracts.PatientContext? Patient);

    private sealed record GatewayResponse([property: JsonPropertyName("text")] string? Text, [property: JsonPropertyName("model")] string? Model);
}

/// <summary>Runtime that can run a model on this machine (e.g. a local inference server). None is bundled in this phase.</summary>
public interface ILocalModelRuntime
{
    Task<string?> GenerateAsync(AiRequest request, CancellationToken ct);
}

/// <summary>Contract-ready slot for on-device/on-prem models. Without a runtime it reports itself unavailable instead of pretending.</summary>
public sealed class LocalAIProvider(ILocalModelRuntime? runtime = null) : IAIProvider
{
    public string Name => "local";
    public AiProviderKind Kind => AiProviderKind.Local;

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        if (runtime is null)
        {
            return AiResult.Fail(AiErrorCode.NotConfigured, "No local AI runtime is installed.");
        }

        var text = await runtime.GenerateAsync(request, ct);
        return string.IsNullOrWhiteSpace(text)
            ? AiResult.Fail(AiErrorCode.InvalidResponse, "The local AI runtime returned nothing.")
            : AiResult.Ok(new AiCompletion(text, Name, "local", false));
    }
}

/// <summary>Disabled: the controlled state when no provider is selected.</summary>
public sealed class DisabledAIProvider : IAIProvider
{
    public string Name => "disabled";
    public AiProviderKind Kind => AiProviderKind.Disabled;
    public Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct = default) => Task.FromResult(AiResult.Fail(AiErrorCode.Disabled, "No AI provider is enabled."));
}

/// <summary>Chooses the provider from configuration at call time. Changing providers means changing settings, not code.</summary>
public sealed class ConfiguredAIProvider(IOptions<AiOptions> options, MockAIProvider mock, ExternalAIProvider external, LocalAIProvider local, DisabledAIProvider disabled) : IAIProvider
{
    public IAIProvider Current => options.Value.Kind switch
    {
        AiProviderKind.Mock => mock,
        AiProviderKind.External => external,
        AiProviderKind.Local => local,
        _ => disabled,
    };

    public string Name => Current.Name;
    public AiProviderKind Kind => Current.Kind;
    public Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct = default) => Current.CompleteAsync(request, ct);
}
