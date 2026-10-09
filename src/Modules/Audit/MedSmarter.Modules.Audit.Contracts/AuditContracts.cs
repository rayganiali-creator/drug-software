namespace MedSmarter.Modules.Audit.Contracts;

public enum AuditResult
{
    Success,
    Denied,
    Failure,
}

/// <summary>Well-known audit actions. Free-form strings are not accepted by callers, only these constants.</summary>
public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string Logout = "LOGOUT";
    public const string LogoutAll = "LOGOUT_ALL";
    public const string TokenRefreshed = "TOKEN_REFRESHED";
    public const string RefreshTokenReuse = "REFRESH_TOKEN_REUSE_DETECTED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string RoleAssigned = "ROLE_ASSIGNED";
    public const string RoleRevoked = "ROLE_REVOKED";
    public const string PermissionGranted = "PERMISSION_GRANTED";
    public const string PermissionRevoked = "PERMISSION_REVOKED";
    public const string ConsentGranted = "CONSENT_GRANTED";
    public const string ConsentRevoked = "CONSENT_REVOKED";
    public const string PatientDataAccessed = "PATIENT_DATA_ACCESSED";
    public const string PatientDataUpdated = "PATIENT_DATA_UPDATED";
    public const string PrescriptionAccessed = "PRESCRIPTION_ACCESSED";
    public const string AdrAccessed = "ADR_ACCESSED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string AdminAction = "ADMIN_ACTION";
    public const string AuditRead = "AUDIT_READ";
    public const string MedicationCreated = "MEDICATION_CREATED";
    public const string MedicationUpdated = "MEDICATION_UPDATED";
    public const string MedicationLifecycleChanged = "MEDICATION_LIFECYCLE_CHANGED";
    public const string MedicationValidationChanged = "MEDICATION_VALIDATION_CHANGED";
    public const string KnowledgeSourceRegistered = "KNOWLEDGE_SOURCE_REGISTERED";
    public const string KnowledgeRevisionAdded = "KNOWLEDGE_REVISION_ADDED";
    public const string AiProviderCalled = "AI_PROVIDER_CALLED";
    public const string InsuranceImport = "INSURANCE_IMPORT";
    public const string CareRelationshipRequested = "CARE_RELATIONSHIP_REQUESTED";
    public const string CareRelationshipAccepted = "CARE_RELATIONSHIP_ACCEPTED";
    public const string CareRelationshipDeclined = "CARE_RELATIONSHIP_DECLINED";
    public const string CareRelationshipEnded = "CARE_RELATIONSHIP_ENDED";
    public const string ProductRecorded = "PRODUCT_RECORDED";
    public const string ProductUpdated = "PRODUCT_UPDATED";
    public const string ProductRemoved = "PRODUCT_REMOVED";
    public const string ManufacturerReportCreated = "MANUFACTURER_REPORT_CREATED";
    public const string ManufacturerReportSubmitted = "MANUFACTURER_REPORT_SUBMITTED";
    public const string ManufacturerReportReviewed = "MANUFACTURER_REPORT_REVIEWED";
    public const string ManufacturerReportSent = "MANUFACTURER_REPORT_SENT";
    public const string ManufacturerReportAcknowledged = "MANUFACTURER_REPORT_ACKNOWLEDGED";
    public const string ManufacturerReportFailed = "MANUFACTURER_REPORT_FAILED";
    public const string ManufacturerReportCancelled = "MANUFACTURER_REPORT_CANCELLED";
    public const string ManufacturerReportBlocked = "MANUFACTURER_REPORT_BLOCKED";
    public const string GuidanceStatusChanged = "GUIDANCE_STATUS_CHANGED";
    public const string AiPatientContextUsed = "AI_PATIENT_CONTEXT_USED";
}

/// <summary>
/// What callers hand to the audit writer. Never put passwords, tokens or clinical payloads in
/// <see cref="Metadata"/>: the writer redacts credential-like keys/values, but callers must not rely on that.
/// </summary>
public sealed record AuditEvent(
    string Action,
    AuditResult Result,
    Guid? ActorUserId,
    string? ResourceType = null,
    string? ResourceId = null,
    Guid? SubjectUserId = null,
    string Source = "api",
    string? CorrelationId = null,
    string? ReasonCode = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AuditEntry(
    Guid Id,
    long Sequence,
    DateTimeOffset Timestamp,
    Guid? ActorUserId,
    string Action,
    string? ResourceType,
    string? ResourceId,
    Guid? SubjectUserId,
    AuditResult Result,
    string Source,
    string? CorrelationId,
    string? ReasonCode,
    IReadOnlyDictionary<string, string> Metadata,
    string Hash);

public sealed record AuditQuery(Guid? ActorUserId = null, Guid? SubjectUserId = null, string? Action = null, int Take = 100);

public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public interface IAuditReader
{
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>Recomputes the hash chain; false means an entry was altered, removed or reordered.</summary>
    Task<bool> VerifyChainAsync(CancellationToken cancellationToken = default);
}
