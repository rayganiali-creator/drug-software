using System.Text.Json;
using MedSmarter.Api.Security;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Integrations.Contracts;

namespace MedSmarter.Api.Integrations;

/// <summary>What is connected, simulated or blocked. Configuration only: no call is made, no secret and no endpoint address is returned.</summary>
public static class IntegrationEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static IEndpointRouteBuilder MapIntegrations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/integrations/registry", (IIntegrationRegistry registry) =>
        {
            var list = registry.Describe();
            return Results.Json(new
            {
                integrations = list,
                anyRealIntegrationActive = list.Any(i => i.State == IntegrationState.RealGatedElsewhere && i.ConfiguredMode == IntegrationMode.Real),
                notice = "Mock results are simulated. No real external system is connected by this application; a request for a real integration is ignored (fail-closed).",
            }, Web);
        }).RequireAuthorization(PermissionPolicyProvider.Prefix + Permissions.IntegrationsRegistryRead);
        return app;
    }
}
