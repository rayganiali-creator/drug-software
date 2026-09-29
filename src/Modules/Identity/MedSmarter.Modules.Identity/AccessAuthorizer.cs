using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Identity;

/// <summary>
/// Authorization ONLY. Layers, all must pass, deny by default:
///   1. RBAC          - the actor's roles grant the permission (from the shared catalog).
///   2. Resource scope- depending on the permission kind: ownership / care relationship / organization membership.
///   3. Consent       - accessing ANOTHER person's data additionally needs an active, in-scope, unexpired consent.
/// Every denial is audited with an internal reason; callers must return a uniform 403 without that reason.
/// </summary>
public sealed class AccessAuthorizer(AccessCatalog catalog, IIdentityStore identity, IConsentEvaluator consent, IAuditWriter audit) : IAccessAuthorizer
{
    public async Task<AccessDecision> AuthorizeAsync(CurrentUser actor, string permission, AccessResource resource, RequestContext context, CancellationToken ct = default)
    {
        var decision = await DecideAsync(actor, permission, resource, ct);
        if (!decision.Allowed)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.AccessDenied, AuditResult.Denied, actor.UserId, resource.Type, resource.Id, resource.SubjectUserId, context.Source, context.CorrelationId,
                $"{decision.Layer}:{decision.ReasonCode}", new Dictionary<string, string> { ["permission"] = permission }), ct);
        }

        return decision;
    }

    private async Task<AccessDecision> DecideAsync(CurrentUser actor, string permission, AccessResource resource, CancellationToken ct)
    {
        var kind = catalog.KindOf(permission);
        if (kind is null)
        {
            return AccessDecision.Deny(AccessLayer.Rbac, "unknown_permission");
        }

        if (!actor.Has(permission))
        {
            return AccessDecision.Deny(AccessLayer.Rbac, "role_lacks_permission");
        }

        switch (kind)
        {
            case "global":
            case "aggregate":
                return AccessDecision.Allow(AccessLayer.Rbac);

            case "own":
                return resource.SubjectUserId is { } owner && owner != actor.UserId
                    ? AccessDecision.Deny(AccessLayer.Ownership, "not_owner")
                    : AccessDecision.Allow(AccessLayer.Ownership);

            case "organization":
                return resource.OrganizationId is { } org && actor.OrganizationIds.Contains(org)
                    ? AccessDecision.Allow(AccessLayer.Organization)
                    : AccessDecision.Deny(AccessLayer.Organization, "not_member");

            case "subject":
                return await DecideSubjectAsync(actor, permission, resource, ct);

            default:
                return AccessDecision.Deny(AccessLayer.Rbac, "unhandled_kind");
        }
    }

    private async Task<AccessDecision> DecideSubjectAsync(CurrentUser actor, string permission, AccessResource resource, CancellationToken ct)
    {
        if (resource.SubjectUserId is not { } subject)
        {
            return AccessDecision.Deny(AccessLayer.Ownership, "no_subject");
        }

        if (subject == actor.UserId)
        {
            return AccessDecision.Allow(AccessLayer.Ownership);
        }

        var relationships = (await identity.RelationshipsForPatientAsync(subject, ct))
            .Where(r => r.IsActive && (r.ProviderUserId == actor.UserId || (r.ProviderOrganizationId is { } o && actor.OrganizationIds.Contains(o))))
            .ToList();
        if (relationships.Count == 0)
        {
            return AccessDecision.Deny(AccessLayer.Relationship, "no_care_relationship");
        }

        var purposes = relationships.SelectMany(r => r.Kind == CareRelationshipKind.Treating
            ? new[] { ConsentPurposes.Treatment, ConsentPurposes.MedicationReview }
            : [ConsentPurposes.Dispensing, ConsentPurposes.MedicationReview]).Distinct().ToArray();
        var scope = catalog.DataScopeOf(permission) ?? string.Empty;
        var verdict = await consent.EvaluateAsync(new ConsentCheck(subject, actor.UserId, actor.OrganizationIds.ToArray(), scope, purposes), ct);
        return verdict.Allowed
            ? AccessDecision.Allow(AccessLayer.Consent)
            : AccessDecision.Deny(AccessLayer.Consent, verdict.Reason);
    }
}
