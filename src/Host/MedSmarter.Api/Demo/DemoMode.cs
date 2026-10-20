using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.Identity;
using Microsoft.AspNetCore.Authorization;

namespace MedSmarter.Api.Demo;

/// <summary>
/// Settings of the dedicated public-demo environment (<c>ASPNETCORE_ENVIRONMENT=Demo</c>). The access code comes from the
/// environment/secret store only (<c>Demo__AccessCode</c>); it is never committed, never sent to a client and never logged.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";
    public const int MinAccessCodeLength = 12;

    public string? AccessCode { get; set; }

    /// <summary>Wrong codes from one client address before that client is locked out.</summary>
    public int MaxFailuresPerClient { get; set; } = 5;

    /// <summary>Wrong codes from all clients together before everyone is locked out (spoofed or rotating addresses).</summary>
    public int MaxFailuresTotal { get; set; } = 100;

    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>
/// Startup checks for the Demo environment. Anything that could make the public demo unsafe stops the host from starting.
/// Does nothing in any other environment: Production behaviour is unchanged.
/// </summary>
public static class DemoGuard
{
    public static void EnsureSafe(string environmentName, IConfiguration configuration)
    {
        if (EnvironmentRules.IsMisspelledDemo(environmentName))
        {
            throw new InvalidOperationException($"The environment name must be exactly 'Demo' (got '{environmentName}'); a different spelling would skip appsettings.Demo.json. Refusing to start.");
        }

        if (!EnvironmentRules.IsDemo(environmentName))
        {
            return;
        }

        var options = configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
        if (options.AccessCode is null || options.AccessCode.Length < DemoOptions.MinAccessCodeLength)
        {
            throw new InvalidOperationException($"Demo:AccessCode (Demo__AccessCode, at least {DemoOptions.MinAccessCodeLength} characters) must be supplied in the Demo environment. Refusing to start.");
        }

        if (options.MaxFailuresPerClient < 1 || options.MaxFailuresTotal < options.MaxFailuresPerClient || options.LockoutMinutes < 1)
        {
            throw new InvalidOperationException("Demo lockout settings are invalid (per-client >= 1, total >= per-client, lockout minutes >= 1). Refusing to start.");
        }

        if (!string.Equals(configuration[$"{PersistenceSettings.Section}:Provider"], nameof(PersistenceProvider.InMemory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Demo environment only runs on in-memory storage (Persistence:Provider=InMemory): it must never hold durable or real data. Refusing to start.");
        }

        var allowed = configuration.GetSection($"{AuthOptions.Section}:{nameof(AuthOptions.DemoAllowedAccounts)}").Get<string[]>() ?? [];
        if (allowed.Length == 0)
        {
            throw new InvalidOperationException("Auth:DemoAllowedAccounts must list the demo accounts that may sign in (it comes from appsettings.Demo.json) in the Demo environment. Refusing to start.");
        }

        DemoAccountPolicy.Validate(allowed, AccessCatalog.LoadEmbedded()); // unknown, non-login and privileged accounts stop the host

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(o => !IsExactHttpsOrigin(o)))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must list the exact https origin(s) of the demo web app (no wildcard, path or http) in the Demo environment. Refusing to start.");
        }

        var ai = configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();
        var provider = ai.Provider ?? string.Empty;
        if (!string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Demo environment allows only Ai:Provider=Mock or Disabled. No external or local AI provider can run in the demo. Refusing to start.");
        }

        if (ai.AllowExternalDataTransfer || !string.IsNullOrWhiteSpace(ai.BaseUrl) || !string.IsNullOrWhiteSpace(ai.ApiKey))
        {
            throw new InvalidOperationException("The Demo environment forbids external data transfer: Ai:AllowExternalDataTransfer must be off and Ai:BaseUrl / Ai:ApiKey must be empty. Refusing to start.");
        }
    }

    private static bool IsExactHttpsOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && !origin.Contains('*', StringComparison.Ordinal)
        && string.IsNullOrEmpty(u.UserInfo)
        && u.AbsolutePath == "/"
        && !origin.EndsWith('/')
        && string.IsNullOrEmpty(u.Query)
        && string.IsNullOrEmpty(u.Fragment);
}

/// <summary>Counts wrong access codes per client and in total, and locks out when a limit is reached. Memory-bounded.</summary>
public sealed class DemoAccessThrottle(Microsoft.Extensions.Options.IOptions<DemoOptions> options, TimeProvider time)
{
    private const int MaxTrackedClients = 10_000;

    private readonly DemoOptions _o = options.Value;
    private readonly ConcurrentDictionary<string, Entry> _clients = new();
    private readonly Lock _gate = new();
    private Entry _total = new(0, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    private sealed record Entry(int Failures, DateTimeOffset WindowStart, DateTimeOffset LockedUntil);

    /// <summary>Seconds until a lockout ends, or 0 when the client may try.</summary>
    public int LockedSeconds(string client)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            var until = _total.LockedUntil;
            if (_clients.TryGetValue(client, out var e) && e.LockedUntil > until)
            {
                until = e.LockedUntil;
            }

            if (until <= now && _clients.Count >= MaxTrackedClients && !_clients.ContainsKey(client))
            {
                Prune(now);
                if (_clients.Count >= MaxTrackedClients)
                {
                    return Math.Max(1, _o.LockoutMinutes * 60); // table full of live entries: refuse unknown clients rather than grow
                }
            }

            return until > now ? (int)Math.Ceiling((until - now).TotalSeconds) : 0;
        }
    }

    public void RecordFailure(string client)
    {
        var now = time.GetUtcNow();
        var window = TimeSpan.FromMinutes(_o.LockoutMinutes);
        lock (_gate)
        {
            _clients[client] = Bump(_clients.GetValueOrDefault(client), now, window, _o.MaxFailuresPerClient);
            _total = Bump(_total, now, window, _o.MaxFailuresTotal);
        }
    }

    public void RecordSuccess(string client)
    {
        lock (_gate)
        {
            _clients.TryRemove(client, out _);
        }
    }

    private static Entry Bump(Entry? e, DateTimeOffset now, TimeSpan window, int limit)
    {
        if (e is null || now - e.WindowStart > window)
        {
            e = new Entry(0, now, DateTimeOffset.MinValue);
        }

        var failures = e.Failures + 1;
        return failures >= limit ? e with { Failures = failures, LockedUntil = now + window } : e with { Failures = failures };
    }

    private void Prune(DateTimeOffset now)
    {
        var window = TimeSpan.FromMinutes(_o.LockoutMinutes);
        foreach (var (key, e) in _clients)
        {
            if (e.LockedUntil <= now && now - e.WindowStart > window)
            {
                _clients.TryRemove(key, out _);
            }
        }
    }
}

/// <summary>
/// Demo access gate. Every ANONYMOUS endpoint (sign-in, refresh, demo-account discovery, and any anonymous route added later) needs the
/// shared demo code in the <c>X-Demo-Code</c> header, except the liveness/readiness probes and /version. Authenticated endpoints are
/// already protected by their bearer token, which can only be obtained through the gate. The code is compared in constant time, wrong
/// guesses are throttled, and the code is never logged. It is a gate against casual visitors, NOT real user authentication.
/// </summary>
public sealed partial class DemoAccessGate(RequestDelegate next, Microsoft.Extensions.Options.IOptions<DemoOptions> options, DemoAccessThrottle throttle, ILogger<DemoAccessGate> logger)
{
    public const string HeaderName = "X-Demo-Code";
    private const int MaxCodeLength = 256;

    private readonly byte[] _expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.AccessCode ?? string.Empty));

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!NeedsCode(ctx))
        {
            await next(ctx);
            return;
        }

        // The connection address only: X-Forwarded-For is deliberately NOT trusted (its behaviour on the host is unverified). Behind a proxy all visitors may share one address.
        var client = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (throttle.LockedSeconds(client) is var locked and > 0)
        {
            LogLockedOut();
            ctx.Response.Headers.RetryAfter = locked.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Deny(ctx, StatusCodes.Status429TooManyRequests, "Too many attempts", "demo_code_throttled");
            return;
        }

        var supplied = ctx.Request.Headers[HeaderName].ToString();
        if (supplied.Length == 0)
        {
            await Deny(ctx, StatusCodes.Status401Unauthorized, "Demo access code required", "demo_code_required"); // no guess was made: not counted
            return;
        }

        if (supplied.Length > MaxCodeLength || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), _expected))
        {
            throttle.RecordFailure(client);
            LogRejected();
            await Deny(ctx, StatusCodes.Status401Unauthorized, "Demo access code invalid", "demo_code_invalid");
            return;
        }

        throttle.RecordSuccess(client);
        await next(ctx);
    }

    private static bool NeedsCode(HttpContext ctx)
    {
        if (HttpMethods.IsOptions(ctx.Request.Method))
        {
            return false; // CORS preflight carries no credentials and is answered by the CORS middleware
        }

        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/version"))
        {
            return false;
        }

        // Anonymous endpoints need the code. Unknown paths have no endpoint (plain 404) and nothing to protect; authenticated ones need a bearer token.
        return ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }

    private static Task Deny(HttpContext ctx, int status, string title, string code)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(ctx);
    }

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Demo access code rejected")]
    private partial void LogRejected();

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "Demo access attempt refused: client is locked out")]
    private partial void LogLockedOut();
}
