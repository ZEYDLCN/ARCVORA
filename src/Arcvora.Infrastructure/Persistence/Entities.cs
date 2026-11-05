namespace Arcvora.Infrastructure.Persistence;

/// <summary>
/// Çalışma alanı rolü. <see cref="AppUser"/> yalnızca kendisine yetki verilen akış uygulamalarını kullanır; mimari projeleri
/// göremez. Sıralama karşılaştırmaları bellekte yapılır (sütun metin olarak saklanır): AppUser &lt; Reader &lt; Editor &lt; Admin.
/// </summary>
public enum WorkspaceRole { AppUser = -1, Reader = 0, Editor = 1, Admin = 2 }

public enum ProposalStatus { Pending, Accepted, Rejected, Stale }

public enum JobStatus { Queued, Running, Succeeded, Failed, Cancelled }

public static class JobKinds
{
    public const string Analysis = "analysis";
    public const string Architecture = "architecture";
    public const string Change = "change";
}

public static class ProposalKinds
{
    public const string Analysis = "analysis";
    public const string Architecture = "architecture";
    public const string Change = "change";
    public const string Sample = "sample";
}

public static class VersionKinds
{
    public const string Initial = "initial";
    public const string Edit = "edit";
    public const string Proposal = "proposal";
    public const string Revert = "revert";
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>Yönetici tarafından verilen geçici parolayla açılan hesaplar ilk girişte değiştirmeye çağrılır.</summary>
    public bool MustChangePassword { get; set; }
    /// <summary>
    /// Hesabı bir master admin açtıysa o çalışma alanı. Yalnızca o çalışma alanının master admin'i parolayı sıfırlayabilir;
    /// kendi kaydıyla açılmış hesaplar (null) başka bir şirketin çalışma alanına da üye olabilir ve asla başkası tarafından sıfırlanamaz.
    /// </summary>
    public Guid? ManagedByWorkspaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Membership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    /// <summary>Kabul yetkisi ayrıca sınırlandırılabilir (§19). Yönetici her zaman kabul edebilir.</summary>
    public bool CanAccept { get; set; } = true;
    /// <summary>Doğrudan yönetici (aynı çalışma alanının üyesi). "Yönetici onayı" adımını çözer.</summary>
    public Guid? ManagerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public User? User { get; set; }
}

/// <summary>Çalışma alanı içindeki kişi grubu: onaylayıcı veya muhasebe grubu olarak kullanılır.</summary>
public sealed class Group
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
}

public sealed class Project
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TechProfileId { get; set; } = "aspnetcore";
    /// <summary>En son sürümün numarası (doğrusal geçmiş).</summary>
    public int HeadNumber { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>Değişmez model anlık görüntüsü. Yalnızca eklenir; hiçbir zaman güncellenmez.</summary>
public sealed class ModelVersion
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public int Number { get; set; }
    public Guid? ParentVersionId { get; set; }
    public string ModelJson { get; set; } = "{}";
    public string ContentHash { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Kind { get; set; } = VersionKinds.Edit;
    public Guid? ProposalId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Proposal
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = ProposalKinds.Change;
    public string Title { get; set; } = "";
    public string? RequestText { get; set; }
    public Guid BaseVersionId { get; set; }
    public int BaseNumber { get; set; }
    public string PatchJson { get; set; } = "{}";
    public string ImpactJson { get; set; } = "{}";
    public string FindingsJson { get; set; } = "[]";
    public string Provider { get; set; } = "";
    public bool IsAi { get; set; }
    public string? Note { get; set; }
    public int TokensUsed { get; set; }
    public int RepairsUsed { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid? AcceptedVersionId { get; set; }
    public string? AcceptedGroupsJson { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public Guid? JobId { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class Job
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string InputJson { get; set; } = "{}";
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string? ProblemsJson { get; set; }
    /// <summary>Başarısız veya çalışan aşama: generate | validate | repair | analyze | persist.</summary>
    public string? Stage { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTimeOffset? AvailableAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? LockedBy { get; set; }
    public bool CancelRequested { get; set; }
    public string? IdempotencyKey { get; set; }
    public int TokenBudget { get; set; }
    public int TokensUsed { get; set; }
    public Guid? ProposalId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Artifact
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid VersionId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>markdown | package</summary>
    public string Format { get; set; } = "markdown";
    public string ProfileId { get; set; } = "";
    public string ProfileVersion { get; set; } = "";
    public string GeneratorVersion { get; set; } = "";
    public string ModelHash { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string? ObjectType { get; set; }
    public string? ObjectId { get; set; }
    public string? DetailJson { get; set; }
    public DateTimeOffset At { get; set; }
}
