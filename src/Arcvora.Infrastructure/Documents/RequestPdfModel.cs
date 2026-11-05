namespace Arcvora.Infrastructure.Documents;

/// <summary>Yazdırılabilir talep formunun içeriği. Tüm metinler hazır (okunur) biçimdedir; çizici yalnızca yerleşim yapar.</summary>
public sealed record RequestPdfModel(
    string Number,
    string AppName,
    string WorkspaceName,
    string Status,            // in_review | approved | rejected | cancelled
    string StatusLabel,
    string RequesterName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int VersionNumber,
    IReadOnlyList<PdfField> Fields,
    IReadOnlyList<PdfApprovalRow> Approvals,
    IReadOnlyList<PdfTimelineRow> Timeline,
    IReadOnlyList<PdfAttachment> Attachments,
    string VerificationCode,
    DateTimeOffset GeneratedAt,
    int UtcOffsetMinutes = 180);

public sealed record PdfField(string Label, string Value);

/// <param name="Decision">Bekliyor | Onayladı | Reddetti | Gerekmedi</param>
public sealed record PdfApprovalRow(string Step, string Who, string Decision, DateTimeOffset? At, string? Comment);

public sealed record PdfTimelineRow(DateTimeOffset At, string Text);

/// <param name="Image">Görsel ekse (PNG/JPEG/WEBP dışı olanlar yalnızca listelenir) baytları; sayfaya gömülür.</param>
public sealed record PdfAttachment(string FileName, string Label, long Size, string Sha256, byte[]? Image);
