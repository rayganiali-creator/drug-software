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

    /// <summary>The links that make the actor a care provider of the patient (directly or through one of the actor's organizations).</summary>
    private static List<(Guid? User, Guid? Org, CareRelationshipKind Kind)> ActorRelationships(CurrentUser actor, IEnumerable<(Guid? User, Guid? Org, CareRelationshipKind Kind)> links) =>
        [.. links.Where(r => r.User == actor.UserId || (r.Org is { } o && actor.OrganizationIds.Contains(o)))];

    /// <summary>Which consent purposes can open the data for these relationships: a treating link and a dispensing link open different purposes.</summary>
    private static string[] PurposesFor(IEnumerable<(Guid? User, Guid? Org, CareRelationshipKind Kind)> relationships) =>
        [.. relationships.SelectMany(r => r.Kind == CareRelationshipKind.Treating
            ? new[] { ConsentPurposes.Treatment, ConsentPurposes.MedicationReview }
            : [ConsentPurposes.Dispensing, ConsentPurposes.MedicationReview]).Distinct()];

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

        var relationships = ActorRelationships(actor, await ActiveLinksAsync(subject, ct));
        if (relationships.Count == 0)
        {
            return AccessDecision.Deny(AccessLayer.Relationship, "no_care_relationship");
        }

        var scope = catalog.DataScopeOf(permission) ?? string.Empty;
        var verdict = await consent.EvaluateAsync(new ConsentCheck(subject, actor.UserId, actor.OrganizationIds.ToArray(), scope, PurposesFor(relationships)), ct);
        return verdict.Allowed
            ? AccessDecision.Allow(AccessLayer.Consent)
            : AccessDecision.Deny(AccessLayer.Consent, verdict.Reason);
    }

    /// <summary>
    /// Many resources of one subject-kind permission. The decision for each resource is the one DecideSubjectAsync gives (same checks, same order, same shared helpers); only the reads are
    /// shared: the dynamic relationships of all subjects come from one query per relationship source and the consents of all subjects from one consent read.
    /// A denial is audited exactly as in the single call (one ACCESS_DENIED entry per denied resource, in order).
    /// </summary>
    public async Task<IReadOnlyList<AccessDecision>> AuthorizeManyAsync(CurrentUser actor, string permission, IReadOnlyList<AccessResource> resources, RequestContext context, CancellationToken ct = default)
    {
        if (catalog.KindOf(permission) != "subject" || !actor.Has(permission))
        {
            // Other kinds and role-level denials involve no per-subject reads: use the single call for each.
            var each = new List<AccessDecision>(resources.Count);
            foreach (var resource in resources)
            {
                each.Add(await AuthorizeAsync(actor, permission, resource, context, ct));
            }

            return each;
        }

        var decisions = new AccessDecision?[resources.Count];
        var pending = new List<(int Index, Guid Subject)>();
        for (var i = 0; i < resources.Count; i++)
        {
            if (resources[i].SubjectUserId is not { } subject)
            {
                decisions[i] = AccessDecision.Deny(AccessLayer.Ownership, "no_subject");
            }
            else if (subject == actor.UserId)
            {
                decisions[i] = AccessDecision.Allow(AccessLayer.Ownership);
            }
            else
            {
                pending.Add((i, subject));
            }
        }

        if (pending.Count > 0)
        {
            var subjects = pending.Select(p => p.Subject).Distinct().ToArray();
            var links = new Dictionary<Guid, List<(Guid? User, Guid? Org, CareRelationshipKind Kind)>>();
            foreach (var subject in subjects)
            {
                links[subject] = [.. (await identity.RelationshipsForPatientAsync(subject, ct)).Where(r => r.IsActive).Select(r => (r.ProviderUserId, r.ProviderOrganizationId, r.Kind))];
            }

            foreach (var source in _careSources)
            {
                foreach (var l in await source.ActiveForPatientsAsync(subjects, ct))
                {
                    if (links.TryGetValue(l.PatientUserId, out var list))
                    {
                        list.Add((l.ProviderUserId, l.ProviderOrganizationId, Enum.Parse<CareRelationshipKind>(l.Kind)));
                    }
                }
            }

            var scope = catalog.DataScopeOf(permission) ?? string.Empty;
            var orgs = actor.OrganizationIds.ToArray();
            var checks = new List<ConsentCheck>();
            var checkAt = new List<int>();
            foreach (var (index, subject) in pending)
            {
                var relationships = ActorRelationships(actor, links[subject]);
                if (relationships.Count == 0)
                {
                    decisions[index] = AccessDecision.Deny(AccessLayer.Relationship, "no_care_relationship");
                    continue;
                }

                checks.Add(new ConsentCheck(subject, actor.UserId, orgs, scope, PurposesFor(relationships)));
                checkAt.Add(index);
            }

            var verdicts = await consent.EvaluateManyAsync(checks, ct);
            if (verdicts.Count != checks.Count)
            {
                // A consent evaluator that answers for a different number of checks cannot be mapped back to the resources: no decision is made from it.
                throw new InvalidOperationException($"The consent evaluator returned {verdicts.Count} decisions for {checks.Count} checks.");
            }

            for (var k = 0; k < checks.Count; k++)
            {
                decisions[checkAt[k]] = verdicts[k].Allowed ? AccessDecision.Allow(AccessLayer.Consent) : AccessDecision.Deny(AccessLayer.Consent, verdicts[k].Reason);
            }
        }

        // Every resource must have its own decision before anything is audited or returned; an unset slot is a defect, never an implicit allow or deny.
        if (decisions.Any(d => d is null))
        {
            throw new InvalidOperationException("A resource was left without an authorization decision.");
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var decision = decisions[i]!;
            if (!decision.Allowed)
            {
                var resource = resources[i];
                await audit.WriteAsync(new AuditEvent(AuditActions.AccessDenied, AuditResult.Denied, actor.UserId, resource.Type, resource.Id, resource.SubjectUserId, context.Source, context.CorrelationId,
                    $"{decision.Layer}:{decision.ReasonCode}", new Dictionary<string, string> { ["permission"] = permission }), ct);
            }
        }

        return [.. decisions.Select(d => d!)];
    }
}
