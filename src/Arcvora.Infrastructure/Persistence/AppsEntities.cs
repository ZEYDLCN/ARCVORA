using Arcvora.Core.Flows;

namespace Arcvora.Infrastructure.Persistence;

public enum AppStatus { Draft, Published, Archived }

public enum RequestStatus { InReview, Approved, Rejected, Cancelled }

public static class GrantKinds
{
    public const string User = "user";
    public const string Group = "group";
}

public static class RequestEventKinds
{
    public const string Submitted = "submitted";
    public const string StepStarted = "step_started";
    public const string StepSkipped = "step_skipped";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
    public const string AccountingQueued = "accounting_queued";
    public const string AccountingSent = "accounting_sent";
    public const string AccountingFailed = "accounting_failed";
    public const string AccountingProcessed = "accounting_processed";
}

/// <summary>Bir akış uygulaması. Taslak düzenlenir; yayınlama değişmez bir <see cref="AppVersion"/> üretir.</summary>
public sealed class AppDefinition
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Arayüzde gösterilen simge anahtarı (sabit bir kümeden).</summary>
    public string Icon { get; set; } = "file";
    public AppStatus Status { get; set; } = AppStatus.Draft;
    /// <summary>Düzenlenen taslak (FlowDefinition JSON).</summary>
    public string DraftJson { get; set; } = "{}";
    /// <summary>Taslak her kaydedildiğinde artar; iki yöneticinin birbirinin değişikliğini ezmesini önler.</summary>
    public int Revision { get; set; } = 1;
    public Guid? PublishedVersionId { get; set; }
    /// <summary>Son yayın numarası.</summary>
    public int VersionCount { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Değişmez yayın. Açık talepler açıldıkları sürümle devam eder.</summary>
public sealed class AppVersion
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public int Number { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public string Hash { get; set; } = "";
    public Guid PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

/// <summary>Bir uygulamayı kullanma yetkisi: kişi veya grup.</summary>
public sealed class AppGrant
{
    public Guid AppId { get; set; }
    public string Kind { get; set; } = GrantKinds.User;
    public Guid SubjectId { get; set; }
}

/// <summary>Talep numarası sayacı (çalışma alanı + önek + yıl); numaralar çalışma alanında benzersizdir.</summary>
public sealed class RequestCounter
{
    public Guid WorkspaceId { get; set; }
    public string Prefix { get; set; } = "";
    public int Year { get; set; }
    public int Value { get; set; }
}

public sealed class FlowRequest
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AppId { get; set; }
    public Guid AppVersionId { get; set; }
    public string Number { get; set; } = "";
    public Guid RequesterId { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.InReview;
    public string DataJson { get; set; } = "{}";
    public string Summary { get; set; } = "";
    /// <summary>Şu an beklenen adımın dizini; tamamlanınca son çalışan adımın dizini kalır.</summary>
    public int CurrentStepIndex { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class ApprovalTask
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid WorkspaceId { get; set; }
    public int StepIndex { get; set; }
    public string StepId { get; set; } = "";
    public string StepName { get; set; } = "";
    public ApprovalMode Mode { get; set; }
    public Guid AssigneeId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? Comment { get; set; }
    /// <summary>Onaylayıcı doğrudan çözülemediyse nedeni (manager_missing, group_empty…).</summary>
    public string? Fallback { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Değişmez zaman çizelgesi: yalnızca eklenir.</summary>
public sealed class RequestEvent
{
    public long Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid WorkspaceId { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? ActorId { get; set; }
    public string Kind { get; set; } = "";
    public string? DetailJson { get; set; }
}

public enum AccountingStatus { Pending, Sent, Processed, Failed }

/// <summary>
/// Onaylanan talebin muhasebeye aktarım kaydı (talep başına tek; transactional outbox). Webhook tanımlıysa işçi gönderir
/// (<c>Pending → Sent</c> veya <c>Failed</c>); muhasebe kullanıcısı <c>Processed</c> olarak işaretler.
/// </summary>
public sealed class AccountingEntry
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public Guid AppId { get; set; }
    public string Number { get; set; } = "";
    public string Category { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public AccountingStatus Status { get; set; } = AccountingStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public Guid? ProcessedBy { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>Çalışma alanı başına muhasebe ayarı: webhook adresi/gizli anahtarı ve kuyruğu işleyen grup.</summary>
public sealed class AccountingSettings
{
    public Guid WorkspaceId { get; set; }
    public string? WebhookUrl { get; set; }
    /// <summary>İstek gövdesini HMAC-SHA256 ile imzalamak için; API yanıtlarında asla döndürülmez.</summary>
    public string? WebhookSecret { get; set; }
    public Guid? AccountingGroupId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
