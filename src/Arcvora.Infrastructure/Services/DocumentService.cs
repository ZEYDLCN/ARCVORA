using System.Security.Cryptography;
using System.Text;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Documents;
using Arcvora.Infrastructure.Files;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

public sealed record VerifyResult(bool Valid, string? Number, string? Status, string? StatusLabel, DateTimeOffset? CompletedAt, string? AppName);

/// <summary>
/// Talebin yazdırılabilir PDF'i ve çıktının doğrulama kodu. Kodu görme yetkisi talebi görme yetkisiyle aynıdır (<see cref="RequestVisibility"/>).
/// Kod, talebin kimliği + numarası + durumu + sonuç zamanı + veri özetinden sunucu anahtarıyla (HMAC) üretilir: talep değişince kod değişir,
/// böylece eski bir çıktı "güncel değil" olarak anlaşılır. Anahtar <c>Jwt:Key</c>'den türetilir (değiştirirseniz eski kodlar geçersiz olur).
/// </summary>
public sealed class DocumentService(ArcvoraDbContext db, RequestVisibility visibility, AppAccessService access, IFileStore store,
    IOptions<JwtOptions> jwt, IOptions<DocumentOptions> options, TimeProvider clock)
{
    private static readonly string[] ImageTypes = ["image/png", "image/jpeg"];

    public async Task<(byte[] Pdf, string FileName)> RequestPdfAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var (r, _, tasks) = await visibility.LoadAsync(userId, requestId, ct);
        var app = await db.Apps.AsNoTracking().FirstAsync(a => a.Id == r.AppId, ct);
        var version = await db.AppVersions.AsNoTracking().FirstAsync(v => v.Id == r.AppVersionId, ct);
        var workspace = await db.Workspaces.AsNoTracking().FirstAsync(w => w.Id == r.WorkspaceId, ct);
        var def = AppDesignService.Deserialize(version.DefinitionJson);
        var values = FlowValues.FromJson(r.DataJson);
        var events = await db.RequestEvents.AsNoTracking().Where(e => e.RequestId == r.Id).OrderBy(e => e.Id).ToListAsync(ct);
        var files = await db.Attachments.AsNoTracking().Where(a => a.RequestId == r.Id).ToListAsync(ct);

        var ids = new HashSet<Guid> { r.RequesterId };
        foreach (var t in tasks) { ids.Add(t.AssigneeId); if (t.DecidedBy is { } d) ids.Add(d); }
        foreach (var e in events) if (e.ActorId is { } a) ids.Add(a);
        foreach (var (_, uid) in FormValidator.UserFieldIds(def, values)) ids.Add(uid);
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        string Name(Guid id) => people.TryGetValue(id, out var n) ? n : "(eski kullanıcı)";
        foreach (var id in ids.Where(i => !people.ContainsKey(i))) people[id] = "(eski kullanıcı)";

        var status = RequestService.Status(r.Status);
        var fields = def.Fields.Where(f => f.Type != FieldType.File)
            .Select(f => new PdfField(f.Label, FlowDisplay.Format(f, values.GetValueOrDefault(f.Key), people))).ToList();

        var approvals = tasks.Select(t => new PdfApprovalRow(t.StepName, Name(t.AssigneeId),
            t.Status switch { ApprovalStatus.Approved => "Onayladı", ApprovalStatus.Rejected => "Reddetti", ApprovalStatus.Skipped => "Gerekmedi", _ => "Bekliyor" },
            t.DecidedAt, t.Comment)).ToList();

        var timeline = events.Select(e => new PdfTimelineRow(e.At, RequestText.Event(e.Kind, e.ActorId,
            e.DetailJson is null ? null : System.Text.Json.JsonDocument.Parse(e.DetailJson).RootElement.Clone(), people))).ToList();

        var labels = def.Fields.ToDictionary(f => f.Key, f => f.Label);
        var attachments = new List<PdfAttachment>();
        foreach (var a in AttachmentList.Ordered(def, values, files))
        {
            byte[]? image = null;
            if (ImageTypes.Contains(a.ContentType) && a.Size <= options.Value.MaxImageBytes && attachments.Count(x => x.Image is not null) < 8)
                image = await store.ReadAsync(a.Id, ct);
            attachments.Add(new PdfAttachment(a.FileName, a.FieldKey is { } k ? labels.GetValueOrDefault(k, k) : "", a.Size, a.Sha256, image));
        }

        var model = new RequestPdfModel(r.Number, app.Name, workspace.Name, status, RequestText.StatusLabel(status), Name(r.RequesterId), r.CreatedAt, r.CompletedAt,
            version.Number, fields, approvals, timeline, attachments, CodeFor(r), clock.GetUtcNow(), options.Value.UtcOffsetMinutes);
        return (RequestPdf.Build(model), $"{r.Number}.pdf");
    }

    /// <summary>Basılı kodun talebin güncel durumuyla eşleşip eşleşmediği. Eşleşmezse (veya talep yoksa) hiçbir ayrıntı verilmez.</summary>
    public async Task<VerifyResult> VerifyAsync(Guid userId, Guid workspaceId, string? number, string? code, CancellationToken ct)
    {
        await access.MemberAsync(userId, workspaceId, ct);
        var invalid = new VerifyResult(false, null, null, null, null, null);
        var normalized = Normalize(code);
        if (string.IsNullOrWhiteSpace(number) || normalized is null || number.Length > 40) return invalid;

        var r = await db.FlowRequests.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Number == number.Trim().ToUpperInvariant(), ct);
        if (r is null) return invalid;
        var expected = Normalize(CodeFor(r))!;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(normalized))) return invalid;

        var appName = await db.Apps.AsNoTracking().Where(a => a.Id == r.AppId).Select(a => a.Name).FirstAsync(ct);
        var status = RequestService.Status(r.Status);
        return new VerifyResult(true, r.Number, status, RequestText.StatusLabel(status), r.CompletedAt, appName);
    }

    /// <summary>"abcd-1234 ef56" → "ABCD1234EF56"; 12 onaltılık hane değilse null.</summary>
    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var s = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return s.Length == 12 && s.All(Uri.IsHexDigit) ? s : null;
    }

    public string CodeFor(FlowRequest r)
    {
        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), Encoding.UTF8.GetBytes("arcvora-document-verification-v1"));
        var dataHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.DataJson)));
        var payload = $"{r.Id:N}|{r.Number}|{r.Status}|{r.CompletedAt?.UtcTicks}|{dataHash}";
        var mac = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))[..12];
        return $"{mac[..4]}-{mac[4..8]}-{mac[8..12]}";
    }
}
