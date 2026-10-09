using System.Text.Json;
using MedSmarter.Api.Security;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Api.Patients;

public sealed record MedicationUpdateBody(PatientMedicationInput Input, string? Reason);
public sealed record ProductUpdateBody(ProductRecordInput Input, string? Reason);
public sealed record StopBody(string? Reason, DateOnly? EndDate, int ExpectedVersion);
public sealed record VersionBody(int ExpectedVersion);
public sealed record ReviewBody(ReviewDecision Decision, string? Note, int ExpectedVersion);
public sealed record RelationshipRequestBody(Guid CounterpartUserId, string? Kind);
public sealed record RelationshipEndBody(string? Reason);
public sealed record GuidanceStatusBody(GuidanceStatus Status);
public sealed record ScanBody(string? Code);
public sealed record ProcessQueueBody(int? Max);

/// <summary>
/// Patient profile, medications taken, schedule/intake, symptoms, batch/lot records, manufacturer reports, care relationships and patient messages.
/// Every route: authenticated (fallback policy), a catalog permission (RBAC), then the resource check (ownership or an active care relationship) and consent
/// for other people's data. A refusal is always the same uniform 403; the reason goes to the audit log only.
/// </summary>
public static class PatientEndpoints
{
    private static string Perm(string p) => PermissionPolicyProvider.Prefix + p;
    private static CurrentUser Actor(HttpContext ctx) => (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;
    private static string SourceOf(HttpContext ctx) => ctx.Request.Headers["X-Client"].FirstOrDefault() is { } c && c is "web" or "android" or "ios" or "windows" or "macos" or "linux" ? c : "api";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Everything a handler needs about the authorized request.</summary>
    private sealed class Call(HttpContext ctx, Guid subject)
    {
        public HttpContext Ctx { get; } = ctx;
        public CurrentUser Actor { get; } = PatientEndpoints.Actor(ctx);
        public Guid Subject { get; } = subject;
        public string Source { get; } = SourceOf(ctx);
        public string? Corr => Ctx.TraceIdentifier;
        public CancellationToken Ct => Ctx.RequestAborted;
        public bool IsSelf => Actor.UserId == Subject;
        public T Svc<T>() where T : notnull => Ctx.RequestServices.GetRequiredService<T>();
        public Guid Id(string name) => Guid.Parse((string)Ctx.GetRouteValue(name)!);

        public async Task<T?> Body<T>() where T : class
        {
            try
            {
                return await Ctx.Request.ReadFromJsonAsync<T>(Web, Ct);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static readonly IResult BadBody = Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes("body.invalid"));

    private static Dictionary<string, object?> Codes(string? detail) =>
        new() { ["code"] = detail?.Split(';')[0] ?? "invalid", ["errors"] = detail?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [] };

    private static IResult Map<T>(PatientOutcome<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Result(ok, r.Value) : r.Error switch
    {
        PatientError.NotFound => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound, extensions: Codes(r.Detail)),
        PatientError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        PatientError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(),
    };

    private static IResult Map<T>(TraceOutcome<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Result(ok, r.Value) : r.Error switch
    {
        TraceError.NotFound => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound, extensions: Codes(r.Detail)),
        TraceError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        TraceError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(),
    };

    private static IResult Map<T>(GuidanceOutcome<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Result(ok, r.Value) : r.Error switch
    {
        GuidanceError.NotFound => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound, extensions: Codes(r.Detail)),
        GuidanceError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        GuidanceError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(),
    };

    private static IResult Result<T>(int status, T? value) => status == StatusCodes.Status204NoContent ? Results.NoContent() : Results.Json(value, Web, statusCode: status);

    /// <summary>Registers a patient-scoped route: permission policy, then ownership/relationship/consent, then (for other people's data) an audit line.</summary>
    private static void Route(IEndpointRouteBuilder app, string method, string pattern, string permission, string resource, bool read, Func<Call, Task<IResult>> handler)
    {
        var builder = app.MapMethods(pattern, [method], async (Guid id, HttpContext ctx) =>
        {
            var actor = Actor(ctx);
            var authz = ctx.RequestServices.GetRequiredService<IAccessAuthorizer>();
            var decision = await authz.AuthorizeAsync(actor, permission, AccessResource.Patient(id, resource), new RequestContext(SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted);
            if (!decision.Allowed)
            {
                return Problem.ForbiddenResult(); // the same answer for "no such patient", "not your patient" and "no consent"
            }

            if (actor.UserId != id)
            {
                // Professionals only ever reach an ACTIVE patient record; a deactivated record grants nothing.
                var patient = await ctx.RequestServices.GetRequiredService<IPatientService>().GetAsync(id, ctx.RequestAborted);
                if (!patient.Succeeded || patient.Value!.Status != PatientStatus.Active)
                {
                    return Problem.ForbiddenResult();
                }

                if (read)
                {
                    await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.PatientDataAccessed, AuditResult.Success, actor.UserId, resource, id.ToString(), id, SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted);
                }
            }

            return await handler(new Call(ctx, id));
        }).RequireAuthorization(Perm(permission));
        if (!read)
        {
            builder.RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        }
    }

    public static IEndpointRouteBuilder MapPatientRecords(this IEndpointRouteBuilder app)
    {
        MapProfileAndLists(app);
        MapMedications(app);
        MapProducts(app);
        MapReports(app);
        MapCare(app);
        MapGuidance(app);
        return app;
    }

    // ---------- profile, conditions, allergies, symptoms ----------

    private static void MapProfileAndLists(IEndpointRouteBuilder app)
    {
        // A patient creates their own record (nobody else can create it for them).
        app.MapPost("/patients/me", async (HttpContext ctx, IPatientService svc) =>
            Map(await svc.EnsureOwnAsync(Actor(ctx).UserId, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.PatientProfileUpdate));

        Route(app, "GET", "/patients/{id:guid}/profile", Permissions.PatientProfileRead, "profile", true, async c => Map(await c.Svc<IPatientService>().GetProfileAsync(c.Subject, c.Ct)));
        Route(app, "PUT", "/patients/{id:guid}/profile", Permissions.PatientProfileUpdate, "profile", false, async c =>
            await c.Body<UpdateProfileCommand>() is { } body ? Map(await c.Svc<IPatientService>().UpdateProfileAsync(c.Actor.UserId, c.Subject, body, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "GET", "/patients/{id:guid}/freshness", Permissions.PatientProfileRead, "freshness", true, async c => Map(await c.Svc<IPatientService>().GetFreshnessAsync(c.Subject, c.Ct)));

        Route(app, "GET", "/patients/{id:guid}/conditions", Permissions.PatientConditionsRead, "conditions", true, async c => Map(await c.Svc<IPatientService>().ListConditionsAsync(c.Subject, c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/conditions", Permissions.PatientConditionsUpdate, "conditions", false, async c =>
            await c.Body<ConditionInput>() is { } b ? Map(await c.Svc<IPatientService>().AddConditionAsync(c.Actor.UserId, c.Subject, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "PUT", "/patients/{id:guid}/conditions/{cid:guid}", Permissions.PatientConditionsUpdate, "conditions", false, async c =>
            await c.Body<ConditionInput>() is { } b ? Map(await c.Svc<IPatientService>().UpdateConditionAsync(c.Actor.UserId, c.Subject, c.Id("cid"), b, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "DELETE", "/patients/{id:guid}/conditions/{cid:guid}", Permissions.PatientConditionsUpdate, "conditions", false, async c =>
            Map(await c.Svc<IPatientService>().RemoveConditionAsync(c.Actor.UserId, c.Subject, c.Id("cid"), c.Source, c.Corr, c.Ct), StatusCodes.Status204NoContent));

        Route(app, "GET", "/patients/{id:guid}/allergies", Permissions.PatientAllergiesRead, "allergies", true, async c => Map(await c.Svc<IPatientService>().ListAllergiesAsync(c.Subject, c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/allergies", Permissions.PatientAllergiesUpdate, "allergies", false, async c =>
            await c.Body<AllergyInput>() is { } b ? Map(await c.Svc<IPatientService>().AddAllergyAsync(c.Actor.UserId, c.Subject, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "PUT", "/patients/{id:guid}/allergies/{aid:guid}", Permissions.PatientAllergiesUpdate, "allergies", false, async c =>
            await c.Body<AllergyInput>() is { } b ? Map(await c.Svc<IPatientService>().UpdateAllergyAsync(c.Actor.UserId, c.Subject, c.Id("aid"), b, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "DELETE", "/patients/{id:guid}/allergies/{aid:guid}", Permissions.PatientAllergiesUpdate, "allergies", false, async c =>
            Map(await c.Svc<IPatientService>().RemoveAllergyAsync(c.Actor.UserId, c.Subject, c.Id("aid"), c.Source, c.Corr, c.Ct), StatusCodes.Status204NoContent));

        Route(app, "GET", "/patients/{id:guid}/symptoms", Permissions.PatientSymptomsRead, "symptoms", true, async c =>
            Map(await c.Svc<IPatientService>().ListSymptomsAsync(c.Subject, int.TryParse(c.Ctx.Request.Query["take"], out var take) ? take : 50, c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/symptoms", Permissions.PatientSymptomsCreate, "symptoms", false, async c =>
            await c.Body<SymptomInput>() is { } b ? Map(await c.Svc<IPatientService>().AddSymptomAsync(c.Actor.UserId, c.Subject, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "DELETE", "/patients/{id:guid}/symptoms/{sid:guid}", Permissions.PatientSymptomsCreate, "symptoms", false, async c =>
            Map(await c.Svc<IPatientService>().RemoveSymptomAsync(c.Actor.UserId, c.Subject, c.Id("sid"), c.Source, c.Corr, c.Ct), StatusCodes.Status204NoContent));

        // What an AI feature would receive about the caller: shown to the patient for transparency. Own data only.
        Route(app, "GET", "/patients/{id:guid}/ai-context", Permissions.PatientAiSummaryRead, "ai-context", true, async c =>
            !c.IsSelf ? Problem.ForbiddenResult() : Map(await c.Svc<IPatientContextService>().BuildAsync(c.Subject, PatientContextPurpose.AiAssistant, c.Ct)));
    }

    // ---------- medications taken, schedule, intake ----------

    private static void MapMedications(IEndpointRouteBuilder app)
    {
        Route(app, "GET", "/patients/{id:guid}/medications", Permissions.PatientMedicationsRead, "medications", true, async c =>
            Map(await c.Svc<IPatientMedicationService>().ListAsync(c.Subject, c.Ctx.Request.Query["includeStopped"] == "true", c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/medications", Permissions.PatientMedicationsUpdate, "medications", false, async c =>
            await c.Body<PatientMedicationInput>() is { } b ? Map(await c.Svc<IPatientMedicationService>().AddAsync(c.Actor.UserId, c.Subject, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "GET", "/patients/{id:guid}/medications/{mid:guid}", Permissions.PatientMedicationsRead, "medications", true, async c => Map(await c.Svc<IPatientMedicationService>().GetAsync(c.Subject, c.Id("mid"), c.Ct)));
        Route(app, "PUT", "/patients/{id:guid}/medications/{mid:guid}", Permissions.PatientMedicationsUpdate, "medications", false, async c =>
            await c.Body<MedicationUpdateBody>() is { Input: not null } b ? Map(await c.Svc<IPatientMedicationService>().UpdateAsync(c.Actor.UserId, c.Subject, c.Id("mid"), b.Input, b.Reason ?? string.Empty, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "POST", "/patients/{id:guid}/medications/{mid:guid}/stop", Permissions.PatientMedicationsUpdate, "medications", false, async c =>
            await c.Body<StopBody>() is { } b ? Map(await c.Svc<IPatientMedicationService>().StopAsync(c.Actor.UserId, c.Subject, c.Id("mid"), new StopMedicationCommand(b.Reason, b.EndDate, b.ExpectedVersion), c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "POST", "/patients/{id:guid}/medications/{mid:guid}/resume", Permissions.PatientMedicationsUpdate, "medications", false, async c =>
            await c.Body<VersionBody>() is { } b ? Map(await c.Svc<IPatientMedicationService>().ResumeAsync(c.Actor.UserId, c.Subject, c.Id("mid"), b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "DELETE", "/patients/{id:guid}/medications/{mid:guid}", Permissions.PatientMedicationsUpdate, "medications", false, async c =>
            Map(await c.Svc<IPatientMedicationService>().RemoveAsync(c.Actor.UserId, c.Subject, c.Id("mid"), c.Source, c.Corr, c.Ct), StatusCodes.Status204NoContent));
        Route(app, "GET", "/patients/{id:guid}/medications/{mid:guid}/versions", Permissions.PatientMedicationsRead, "medications", true, async c => Map(await c.Svc<IPatientMedicationService>().VersionsAsync(c.Subject, c.Id("mid"), c.Ct)));

        Route(app, "GET", "/patients/{id:guid}/medications/{mid:guid}/schedule", Permissions.PatientMedicationsRead, "schedule", true, async c => Map(await c.Svc<IPatientMedicationService>().ListScheduleAsync(c.Subject, c.Id("mid"), c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/medications/{mid:guid}/schedule", Permissions.PatientMedicationsUpdate, "schedule", false, async c =>
            await c.Body<ScheduleEntryInput>() is { } b ? Map(await c.Svc<IPatientMedicationService>().AddScheduleEntryAsync(c.Actor.UserId, c.Subject, c.Id("mid"), b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "DELETE", "/patients/{id:guid}/schedule/{eid:guid}", Permissions.PatientMedicationsUpdate, "schedule", false, async c =>
            Map(await c.Svc<IPatientMedicationService>().RemoveScheduleEntryAsync(c.Actor.UserId, c.Subject, c.Id("eid"), c.Source, c.Corr, c.Ct), StatusCodes.Status204NoContent));

        // The day's planned doses with what was logged; and the logging itself.
        Route(app, "GET", "/patients/{id:guid}/doses", Permissions.PatientAdherenceRead, "adherence", true, async c =>
            DateOnly.TryParse(c.Ctx.Request.Query["date"], out var date) ? Map(await c.Svc<IPatientMedicationService>().GetDayAsync(c.Subject, date, c.Ct))
            : Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes("date.invalid")));
        Route(app, "POST", "/patients/{id:guid}/intake", Permissions.PatientAdherenceLog, "adherence", false, async c =>
            await c.Body<LogIntakeCommand>() is { } b ? Map(await c.Svc<IPatientMedicationService>().LogIntakeAsync(c.Actor.UserId, c.Subject, b, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "GET", "/patients/{id:guid}/adherence", Permissions.PatientAdherenceRead, "adherence", true, async c =>
        {
            var until = DateTimeOffset.TryParse(c.Ctx.Request.Query["to"], out var u) ? u : DateTimeOffset.UtcNow;
            var from = DateTimeOffset.TryParse(c.Ctx.Request.Query["from"], out var f) ? f : until.AddDays(-30);
            return Map(await c.Svc<IPatientMedicationService>().ListIntakeAsync(c.Subject, from, until, c.Ct));
        });
    }

    // ---------- batch / lot product records ----------

    private static ProductRecorder RecorderOf(Call c) => c.IsSelf ? ProductRecorder.Patient : c.Actor.Roles.Contains(RoleNames.Pharmacist) ? ProductRecorder.Pharmacist : ProductRecorder.Physician;

    private static void MapProducts(IEndpointRouteBuilder app)
    {
        Route(app, "GET", "/patients/{id:guid}/products", Permissions.PatientProductsRead, "products", true, async c => Map(await c.Svc<IProductTraceService>().ListAsync(c.Subject, c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/products", Permissions.PatientProductsRecord, "products", false, async c =>
            await c.Body<ProductRecordInput>() is { } b ? Map(await c.Svc<IProductTraceService>().RecordAsync(c.Actor.UserId, RecorderOf(c), c.Subject, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "GET", "/patients/{id:guid}/products/{pid:guid}", Permissions.PatientProductsRead, "products", true, async c => Map(await c.Svc<IProductTraceService>().GetAsync(c.Subject, c.Id("pid"), c.Ct)));
        Route(app, "PUT", "/patients/{id:guid}/products/{pid:guid}", Permissions.PatientProductsRecord, "products", false, async c =>
            await c.Body<ProductUpdateBody>() is { Input: not null } b ? Map(await c.Svc<IProductTraceService>().UpdateAsync(c.Actor.UserId, RecorderOf(c), c.Subject, c.Id("pid"), b.Input, b.Reason ?? string.Empty, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "POST", "/patients/{id:guid}/products/{pid:guid}/confirm", Permissions.PatientProductsRecord, "products", false, async c =>
            await c.Body<VersionBody>() is { } b ? Map(await c.Svc<IProductTraceService>().ConfirmAsync(c.Actor.UserId, RecorderOf(c), c.Subject, c.Id("pid"), b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "DELETE", "/patients/{id:guid}/products/{pid:guid}", Permissions.PatientProductsRecord, "products", false, async c =>
            Map(await c.Svc<IProductTraceService>().RemoveAsync(c.Actor.UserId, c.Subject, c.Id("pid"), c.Source, c.Corr, c.Ct), StatusCodes.Status204NoContent));
        Route(app, "GET", "/patients/{id:guid}/products/{pid:guid}/versions", Permissions.PatientProductsRead, "products", true, async c => Map(await c.Svc<IProductTraceService>().VersionsAsync(c.Subject, c.Id("pid"), c.Ct)));

        // Barcode / QR: a connection point only. There is no scanner and no external service in this phase.
        Route(app, "POST", "/patients/{id:guid}/products/scan", Permissions.PatientProductsRecord, "products", false, async c =>
            await Task.FromResult(Results.Problem(title: "Not implemented", statusCode: StatusCodes.Status501NotImplemented, extensions: Codes("scan.not_available"))));
    }

    // ---------- manufacturer reports ----------

    private static void MapReports(IEndpointRouteBuilder app)
    {
        Route(app, "GET", "/patients/{id:guid}/manufacturer-reports", Permissions.ManufacturerReportRead, "manufacturer-reports", true, async c => Map(await c.Svc<IManufacturerReportService>().ListAsync(c.Subject, c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/manufacturer-reports", Permissions.ManufacturerReportCreate, "manufacturer-reports", false, async c =>
            await c.Body<ReportDraftInput>() is { } b ? Map(await c.Svc<IManufacturerReportService>().CreateDraftAsync(c.Actor.UserId, c.Subject, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : BadBody);
        Route(app, "GET", "/patients/{id:guid}/manufacturer-reports/{rid:guid}", Permissions.ManufacturerReportRead, "manufacturer-reports", true, async c => Map(await c.Svc<IManufacturerReportService>().GetAsync(c.Subject, c.Id("rid"), c.Ct)));
        Route(app, "PUT", "/patients/{id:guid}/manufacturer-reports/{rid:guid}", Permissions.ManufacturerReportCreate, "manufacturer-reports", false, async c =>
            await c.Body<ReportDraftInput>() is { } b ? Map(await c.Svc<IManufacturerReportService>().UpdateDraftAsync(c.Actor.UserId, c.Subject, c.Id("rid"), b, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "POST", "/patients/{id:guid}/manufacturer-reports/{rid:guid}/submit", Permissions.ManufacturerReportCreate, "manufacturer-reports", false, async c =>
            await c.Body<VersionBody>() is { } b ? Map(await c.Svc<IManufacturerReportService>().SubmitAsync(c.Actor.UserId, c.Subject, c.Id("rid"), b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : BadBody);
        Route(app, "POST", "/patients/{id:guid}/manufacturer-reports/{rid:guid}/cancel", Permissions.ManufacturerReportCreate, "manufacturer-reports", false, async c =>
            Map(await c.Svc<IManufacturerReportService>().CancelAsync(c.Actor.UserId, c.Subject, c.Id("rid"), c.Source, c.Corr, c.Ct)));
        Route(app, "POST", "/patients/{id:guid}/manufacturer-reports/{rid:guid}/review", Permissions.ManufacturerReportReview, "manufacturer-reports", false, async c =>
            await c.Body<ReviewBody>() is { } b ? Map(await c.Svc<IManufacturerReportService>().ReviewAsync(c.Actor.UserId, c.Subject, c.Id("rid"), new ReviewReportCommand(b.Decision, b.Note, b.ExpectedVersion), c.Source, c.Corr, c.Ct)) : BadBody);

        // The reviewer's inbox: reports of the patients the caller is related to, each checked against consent like any other access to that patient's data.
        app.MapGet("/manufacturer-reports/pending-review", async (HttpContext ctx, IUserIdentityService users, IAccessAuthorizer authz, IManufacturerReportService reports, IPatientService patients) =>
        {
            var actor = Actor(ctx);
            var allowed = new List<Guid>();
            foreach (var p in await users.ListRelatedPatientsAsync(actor, ctx.RequestAborted))
            {
                if ((await authz.AuthorizeAsync(actor, Permissions.ManufacturerReportReview, AccessResource.Patient(p.UserId, "manufacturer-reports"), new RequestContext(SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted)).Allowed
                    && (await patients.GetAsync(p.UserId, ctx.RequestAborted)) is { Succeeded: true, Value.Status: PatientStatus.Active })
                {
                    allowed.Add(p.UserId);
                }
            }

            return Results.Json(await reports.PendingReviewAsync(allowed, ctx.RequestAborted), Web);
        }).RequireAuthorization(Perm(Permissions.ManufacturerReportReview));

        // Operators: queue status (no clinical content) and a manual pass over the outbox.
        app.MapGet("/manufacturer-reports/queue", async (IManufacturerReportOutbox outbox, CancellationToken ct) => Results.Json(await outbox.GetQueueAsync(ct), Web))
            .RequireAuthorization(Perm(Permissions.ManufacturerReportQueueRead));
        app.MapPost("/manufacturer-reports/queue/process", async (HttpContext ctx, IManufacturerReportOutbox outbox) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<ProcessQueueBody>(Web, ctx.RequestAborted).ConfigureAwait(false);
            return Results.Json(await outbox.ProcessDueAsync(Actor(ctx).UserId, body?.Max ?? 20, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted), Web);
        }).RequireAuthorization(Perm(Permissions.ManufacturerReportQueueManage)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        app.MapPost("/manufacturer-reports/{rid:guid}/retry", async (Guid rid, HttpContext ctx, IManufacturerReportOutbox outbox) =>
            Map(await outbox.RetryFailedAsync(Actor(ctx).UserId, rid, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.ManufacturerReportQueueManage)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
    }

    // ---------- care relationships + consent history ----------

    private static void MapCare(IEndpointRouteBuilder app)
    {
        app.MapGet("/care-relationships", async (HttpContext ctx, ICareRelationshipService svc, IUserIdentityService users) =>
        {
            var list = await svc.ListMineAsync(Actor(ctx).UserId, ctx.RequestAborted);
            var names = new Dictionary<Guid, string>();
            foreach (var id in list.SelectMany(r => new[] { (Guid?)r.PatientSubjectId, r.ProviderUserId }).Where(x => x is not null).Select(x => x!.Value).Distinct())
            {
                names[id] = (await users.GetSummaryAsync(id, ctx.RequestAborted))?.DisplayName ?? "Unknown";
            }

            return Results.Json(list.Select(r => new { relationship = r, patientName = names[r.PatientSubjectId], providerName = r.ProviderUserId is { } p ? names[p] : null }), Web);
        }).RequireAuthorization(Perm(Permissions.CareRelationshipRead));

        app.MapPost("/care-relationships", async (RelationshipRequestBody body, HttpContext ctx, ICareRelationshipService svc) =>
            Map(await svc.RequestAsync(Actor(ctx).UserId, new RequestCareRelationshipCommand(body.CounterpartUserId, body.Kind ?? "Treating"), SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted), StatusCodes.Status201Created))
            .RequireAuthorization(Perm(Permissions.CareRelationshipManage)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        app.MapPost("/care-relationships/{id:guid}/accept", async (Guid id, HttpContext ctx, ICareRelationshipService svc) =>
            Map(await svc.AcceptAsync(Actor(ctx).UserId, id, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.CareRelationshipManage)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        app.MapPost("/care-relationships/{id:guid}/decline", async (Guid id, HttpContext ctx, ICareRelationshipService svc) =>
            Map(await svc.DeclineAsync(Actor(ctx).UserId, id, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.CareRelationshipManage)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        app.MapPost("/care-relationships/{id:guid}/end", async (Guid id, HttpContext ctx, ICareRelationshipService svc) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<RelationshipEndBody>(Web, ctx.RequestAborted).ConfigureAwait(false);
            return Map(await svc.EndAsync(Actor(ctx).UserId, id, body?.Reason, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted));
        }).RequireAuthorization(Perm(Permissions.CareRelationshipManage)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);

        app.MapGet("/consents/history", async (HttpContext ctx, IConsentService consents) => Results.Json(await consents.HistoryAsync(Actor(ctx).UserId, ctx.RequestAborted), Web))
            .RequireAuthorization(Perm(Permissions.ConsentRead));
    }

    // ---------- patient messages ----------

    private static void MapGuidance(IEndpointRouteBuilder app)
    {
        app.MapGet("/guidance/messages", async (HttpContext ctx, IGuidanceService svc) => Map(await svc.ListForPatientAsync(Actor(ctx).UserId, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.GuidanceRead));
        app.MapPost("/guidance/messages/{id:guid}/status", async (Guid id, GuidanceStatusBody body, HttpContext ctx, IGuidanceService svc) =>
            Map(await svc.SetStatusAsync(Actor(ctx).UserId, Actor(ctx).UserId, id, body.Status, false, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.GuidanceUpdate)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);

        // Fictional examples of the message contract. Patients get the patient view; professionals also see the technical view.
        app.MapGet("/guidance/samples", (HttpContext ctx, IGuidanceService svc, string? locale) =>
        {
            var actor = Actor(ctx);
            if (!actor.Has(Permissions.GuidanceRead) && !actor.Has(Permissions.GuidanceProfessionalRead))
            {
                return Problem.ForbiddenResult();
            }

            var pro = actor.Has(Permissions.GuidanceProfessionalRead);
            var lang = locale is "fa" ? "fa" : "en";
            return Results.Json(new
            {
                demo = true,
                notice = "DEMO DATA - NOT FOR CLINICAL USE. Examples of the message format; they are not generated from any real data.",
                samples = svc.Samples(lang).Select(s => new { level = s.Level, patient = s.Patient, professional = pro ? s.Professional : null }),
            }, Web);
        });
    }
}
