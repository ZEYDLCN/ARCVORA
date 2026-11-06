using System.Text.Json;
using Arcvora.Core.Flows;

namespace Arcvora.Infrastructure.Services;

public sealed record AppSummaryDto(Guid Id, Guid WorkspaceId, string Name, string Description, string Icon, string Status,
    int VersionNumber, bool HasDraftChanges, int Revision, DateTimeOffset UpdatedAt);

public sealed record AppDetailDto(AppSummaryDto Summary, FlowDefinition Draft, FlowDefinition? Published, IReadOnlyList<FlowIssue> Issues);

public sealed record CreateAppRequest(string? Name, string? Description, string? Icon, FlowDefinition? Definition);

public sealed record UpdateAppRequest(int? BaseRevision, string? Name, string? Description, string? Icon, FlowDefinition? Definition);

public sealed record AppGrantsDto(IReadOnlyList<Guid> UserIds, IReadOnlyList<Guid> GroupIds);

public sealed record SetGrantsRequest(IReadOnlyList<Guid>? UserIds, IReadOnlyList<Guid>? GroupIds);

public sealed record MyAppDto(Guid Id, Guid WorkspaceId, string Name, string Description, string Icon, int VersionNumber);

/// <param name="Lists">Listeye bağlı seçim alanlarının aktif öğeleri (liste kimliği → değerler).</param>
public sealed record FormHolidayDto(DateOnly Date, bool HalfDay);

/// <param name="Holidays">İş günü hesabı (work_days) kullanan formlar için tatil takvimi: istemci önizlemesi sunucuyla aynı sayıyı göstersin.</param>
public sealed record AppFormDto(MyAppDto App, FlowDefinition Definition, IReadOnlyDictionary<Guid, IReadOnlyList<string>> Lists, IReadOnlyList<FormHolidayDto> Holidays);

public sealed record SubmitRequest(Dictionary<string, JsonElement>? Data);

public sealed record RequestSummaryDto(Guid Id, Guid WorkspaceId, string Number, Guid AppId, string AppName, string Icon, string Status,
    string Summary, Guid RequesterId, string RequesterName, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? CurrentStepName,
    IReadOnlyList<SummaryPart>? SummaryParts = null, bool IsDemo = false);

public sealed record TaskDto(Guid Id, Guid AssigneeId, int StepIndex, string StepId, string StepName, string Status, string? Comment,
    string? Fallback, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, Guid? DecidedBy);

public sealed record EventDto(long Id, DateTimeOffset At, Guid? ActorId, string Kind, JsonElement? Detail);

/// <param name="People">Kayıtta geçen kişilerin kimlik → ad eşlemesi (talep eden, atananlar, işlem yapanlar).</param>
public sealed record RequestDetailDto(RequestSummaryDto Summary, FlowDefinition Definition, int VersionNumber, JsonElement Data,
    IReadOnlyList<TaskDto> Tasks, IReadOnlyList<EventDto> Events, IReadOnlyDictionary<Guid, string> People, bool CanCancel, Guid? MyPendingTaskId,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record MyTaskDto(Guid Id, Guid RequestId, string RequestNumber, string AppName, string Icon, string Summary, string RequesterName,
    string StepName, string Status, string? Fallback, string? Comment, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt,
    Guid? OnBehalfOfId = null, string? OnBehalfOfName = null, IReadOnlyList<SummaryPart>? SummaryParts = null, bool IsDemo = false);

public sealed record DecideRequest(string? Comment);

public sealed record DirectoryEntry(Guid Id, string DisplayName);
