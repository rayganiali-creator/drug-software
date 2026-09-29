using Serilog.Context;
using System.Text.RegularExpressions;

namespace MedSmarter.Api.Http;

/// <summary>
/// Assigns/propagates a correlation id (header <c>X-Correlation-Id</c>) and pushes it into the log context.
/// Untrusted inbound values are accepted only if they look like a safe token, preventing log injection.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    [GeneratedRegex("^[A-Za-z0-9\\-_.]{8,64}$")]
    private static partial Regex Safe();

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var id = incoming is not null && Safe().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }
}
