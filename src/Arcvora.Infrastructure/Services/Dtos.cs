using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Rendering;
using Arcvora.Core.Validation;

namespace Arcvora.Infrastructure.Services;

public sealed record CreateProjectRequest(Guid WorkspaceId, string? Name, string? Description, string? TechProfileId, string? IdeaText);

public sealed record ProjectSummary(Guid Id, Guid WorkspaceId, string Name, string Description, string TechProfileId,
    int HeadNumber, DateTimeOffset UpdatedAt, int PendingProposals, string Role);

public sealed record ProjectDetail(ProjectSummary Summary, VersionInfo Head, ArchitectureModel Model,
    IReadOnlyList<ValidationFinding> Findings, bool CanEdit, bool CanAccept, bool CanAdmin);

public sealed record VersionDetail(VersionInfo Info, ArchitectureModel Model);

public sealed record EditResult(VersionInfo Version, IReadOnlyList<ValidationFinding> NewFindings);

public sealed record AddSourceRequest(string? Title, string? Text, string? Format, int? BaseVersion);

public sealed record CriterionInput(string? Id, string Text);

public sealed record UpdateRequirementRequest(int? BaseVersion, string? Text, RequirementKind? Kind, Priority? Priority,
    ReviewStatus? ReviewStatus, IReadOnlyList<CriterionInput>? AcceptanceCriteria);

public sealed record AnswerQuestionRequest(int? BaseVersion, QuestionStatus? Status, string? Answer);

public sealed record ApplyPatchRequest(int? BaseVersion, string? Summary, ModelPatch? Patch);

public sealed record ViewsResult(int VersionNumber, string ModelHash, IReadOnlyList<RenderedView> Views);

public sealed record FindingsResult(int VersionNumber, IReadOnlyList<ValidationFinding> Findings);

public sealed record ProposalSummary(Guid Id, string Kind, string Title, string? RequestText, string Status, int BaseNumber,
    string Provider, bool IsAi, string? Note, int TokensUsed, int RepairsUsed, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt,
    Guid? AcceptedVersionId, int OperationCount, bool IsStale);

public sealed record GroupInfo(string Id, string Title, string Description, int OperationCount, IReadOnlyList<string> DependsOn);

public sealed record ProposalDetail(ProposalSummary Summary, ModelPatch Patch, ImpactReport Impact,
    IReadOnlyList<ValidationFinding> NewFindings, IReadOnlyList<GroupInfo> Groups);

public sealed record AcceptRequest(IReadOnlyList<string>? GroupIds, int? BaseVersion);

public sealed record AcceptResult(VersionInfo Version, ProposalSummary Proposal, bool AlreadyAccepted);

public sealed record RejectRequest(string? Note);

public sealed record JobDto(Guid Id, Guid ProjectId, string Kind, string Status, string? Stage, int Attempts, int MaxAttempts,
    string? ErrorCode, string? Error, IReadOnlyList<string> Problems, Guid? ProposalId, int TokensUsed, int TokenBudget,
    bool CancelRequested, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record EnqueueResult(JobDto Job, bool Created);

public sealed record ExportRequest(int? VersionNumber, string? Format);

public sealed record ArtifactDto(Guid Id, Guid ProjectId, int VersionNumber, string Format, string ProfileId, string ProfileVersion,
    string GeneratorVersion, string ModelHash, string ContentHash, string FileName, string ContentType, long Size,
    DateTimeOffset CreatedAt);

public sealed record MemberDto(Guid UserId, string Email, string DisplayName, string Role, bool CanAccept,
    Guid? ManagerId = null, bool MustChangePassword = false, bool PasswordResettable = false);

public sealed record ResetPasswordRequest(string? Password);

public sealed record AddMemberRequest(string? Email, string? Role, bool? CanAccept);

/// <summary>Yönetici tarafından yeni hesap açma. Parola geçicidir; kullanıcı ilk girişte değiştirmeye çağrılır.</summary>
public sealed record CreateUserRequest(string? Email, string? DisplayName, string? Password, string? Role, Guid? ManagerId);

/// <summary>Üye güncelleme. <c>ClearManager</c> true ise doğrudan yönetici kaldırılır.</summary>
public sealed record UpdateMemberRequest(string? Role, bool? CanAccept, Guid? ManagerId, bool? ClearManager, string? DisplayName);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record GroupDto(Guid Id, string Name, IReadOnlyList<Guid> MemberIds);

public sealed record GroupNameRequest(string? Name);

public sealed record GroupMembersRequest(IReadOnlyList<Guid>? UserIds);

public sealed record AuditItem(long Id, DateTimeOffset At, string Action, string? Actor, string? ObjectType, string? ObjectId, string? DetailJson);
