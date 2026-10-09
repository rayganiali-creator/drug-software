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
public sealed class AccessAuthorizer(AccessCatalog catalog, IIdentityStore identity, IConsentEvaluator consent, IAuditWriter audit, IEnumerable<ICareRelationshipSource> careSources) : IAccessAuthorizer
{
    private readonly ICareRelationshipSource[] _careSources = [.. careSources];

    /// <summary>Static catalog relationships plus every dynamic source (a relationship ended in a source stops granting access immediately).</summary>
    private async Task<List<(Guid? User, Guid? Org, CareRelationshipKind Kind)>> ActiveLinksAsync(Guid patient, CancellationToken ct)
    {
        var links = (await identity.RelationshipsForPatientAsync(patient, ct)).Where(r => r.IsActive).Select(r => (r.ProviderUserId, r.ProviderOrganizationId, r.Kind)).ToList();
        foreach (var source in _careSources)
        {
            links.AddRange((await source.ActiveForPatientAsync(patient, ct)).Select(l => (l.ProviderUserId, l.ProviderOrganizationId, Enum.Parse<CareRelationshipKind>(l.Kind))));
        }

        return links;
    }

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
                if (resource.OrganizationId is not { } org || !actor.OrganizationIds.Contains(org))
                {
                    return AccessDecision.Deny(AccessLayer.Organization, "not_member");
                }

                // Organization permissions that reach an individual patient's data (pharmacy prescriptions) also need the
                // organization's care relationship with that patient AND the patient's consent to the organization.
                return resource.SubjectUserId is { } patient
                    ? await DecideThroughOrganizationAsync(actor, permission, org, patient, ct)
                    : AccessDecision.Allow(AccessLayer.Organization);

            case "subject":
                return await DecideSubjectAsync(actor, permission, resource, ct);

            default:
                return AccessDecision.Deny(AccessLayer.Rbac, "unhandled_kind");
        }
    }

    private async Task<AccessDecision> DecideThroughOrganizationAsync(CurrentUser actor, string permission, Guid organizationId, Guid patient, CancellationToken ct)
    {
        var related = (await ActiveLinksAsync(patient, ct)).Any(r => r.Org == organizationId);
        if (!related)
        {
            return AccessDecision.Deny(AccessLayer.Relationship, "no_care_relationship");
        }

        var scope = catalog.DataScopeOf(permission) ?? string.Empty;
        var verdict = await consent.EvaluateAsync(new ConsentCheck(patient, actor.UserId, [organizationId], scope, [ConsentPurposes.Dispensing, ConsentPurposes.MedicationReview]), ct);
        return verdict.Allowed ? AccessDecision.Allow(AccessLayer.Consent) : AccessDecision.Deny(AccessLayer.Consent, verdict.Reason);
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

        var relationships = (await ActiveLinksAsync(subject, ct))
            .Where(r => r.User == actor.UserId || (r.Org is { } o && actor.OrganizationIds.Contains(o)))
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
