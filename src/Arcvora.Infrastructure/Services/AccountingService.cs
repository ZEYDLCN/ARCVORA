using System.Globalization;
using System.Text;
using System.Text.Json;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>Muhasebe kuyruğu: ayarlar (master admin), kayıtları görme/işleme/yeniden deneme/CSV (muhasebe grubu + master admin).</summary>
public sealed class AccountingService(ArcvoraDbContext db, AppAccessService access, AuditLog audit, TimeProvider clock)
{
    private const int ListLimit = 500;
    private const int MinSecretLength = 16;

    // ================================================================== ayarlar

    public async Task<AccountingSettingsDto> GetSettingsAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var s = await db.AccountingSettings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        return ToDto(s);
    }

    public async Task<AccountingSettingsDto> UpdateSettingsAsync(Guid actorId, Guid workspaceId, UpdateAccountingSettingsRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var s = await db.AccountingSettings.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        if (s is null)
        {
            s = new AccountingSettings { WorkspaceId = workspaceId };
            db.AccountingSettings.Add(s);
        }

        if (req.ClearWebhook == true) s.WebhookUrl = null;
        else if (req.WebhookUrl is not null) s.WebhookUrl = ValidUrl(req.WebhookUrl);

        if (req.ClearSecret == true) s.WebhookSecret = null;
        else if (req.WebhookSecret is not null)
        {
            if (req.WebhookSecret.Length is < MinSecretLength or > 200)
                throw new RequestValidationException("invalid_secret", $"Gizli anahtar {MinSecretLength}–200 karakter olmalı.");
            s.WebhookSecret = req.WebhookSecret;
        }

        if (req.ClearGroup == true) s.AccountingGroupId = null;
        else if (req.AccountingGroupId is { } g)
        {
            if (!await db.Groups.AnyAsync(x => x.Id == g && x.WorkspaceId == workspaceId, ct))
                throw new RequestValidationException("invalid_group", "Seçilen grup bu çalışma alanında yok.");
            s.AccountingGroupId = g;
        }
        s.UpdatedAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "accounting.settings_updated", "workspace", workspaceId.ToString(),
            new { webhook = s.WebhookUrl is not null, secret = s.WebhookSecret is not null, group = s.AccountingGroupId });
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    // ================================================================== kuyruk

    public async Task<IReadOnlyList<AccountingEntryDto>> ListAsync(Guid actorId, Guid workspaceId, string? status, string? from, string? to, CancellationToken ct)
    {
        await RequireAccountingAsync(actorId, workspaceId, ct);
        var rows = await QueryAsync(workspaceId, status, from, to, ct);
        var userIds = rows.Where(r => r.Entry.ProcessedBy != null).Select(r => r.Entry.ProcessedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(r => new AccountingEntryDto(r.Entry.Id, r.Entry.RequestId, r.Entry.Number, r.Entry.AppId, r.AppName, r.Entry.Category,
            Status(r.Entry.Status), r.Entry.Attempts, r.Entry.LastError, r.Entry.CreatedAt, r.Entry.SentAt, r.Entry.ProcessedBy,
            r.Entry.ProcessedBy is { } p ? names.GetValueOrDefault(p) : null, r.Entry.ProcessedAt, r.Entry.Note,
            JsonDocument.Parse(r.Entry.PayloadJson).RootElement.Clone())).ToList();
    }

    public async Task<AccountingEntryDto> ProcessAsync(Guid actorId, Guid workspaceId, Guid entryId, string? note, CancellationToken ct)
    {
        await RequireAccountingAsync(actorId, workspaceId, ct);
        var n = note?.Trim();
        if (n is { Length: > 500 }) throw new RequestValidationException("note_too_long", "Not en fazla 500 karakter olabilir.");

        var e = await FindAsync(workspaceId, entryId, ct);
        if (e.Status == AccountingStatus.Processed) throw new ConflictException("already_processed", "Bu kayıt zaten işlendi olarak işaretlenmiş.");
        var now = clock.GetUtcNow();
        e.Status = AccountingStatus.Processed;
        e.ProcessedBy = actorId;
        e.ProcessedAt = now;
        e.Note = string.IsNullOrEmpty(n) ? null : n;
        e.NextAttemptAt = null;
        db.RequestEvents.Add(Event(e, actorId, RequestEventKinds.AccountingProcessed, new { entryId = e.Id, note = e.Note }, now));
        audit.Add(workspaceId, null, actorId, "accounting.processed", "request", e.RequestId.ToString(), new { e.Number });
        await SaveAsync(ct);
        return (await ListAsync(actorId, workspaceId, null, null, null, ct)).First(x => x.Id == entryId);
    }

    /// <summary>Başarısız (veya hâlâ bekleyen) kaydı yeniden denemeye alır; deneme sayacı sıfırlanır.</summary>
    public async Task<AccountingEntryDto> RetryAsync(Guid actorId, Guid workspaceId, Guid entryId, CancellationToken ct)
    {
        await RequireAccountingAsync(actorId, workspaceId, ct);
        var settings = await db.AccountingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);
        if (string.IsNullOrEmpty(settings?.WebhookUrl)) throw new ConflictException("webhook_not_configured", "Webhook adresi tanımlı değil; kayıt elle işlenebilir.");

        var e = await FindAsync(workspaceId, entryId, ct);
        if (e.Status is not (AccountingStatus.Failed or AccountingStatus.Pending))
            throw new ConflictException("not_retryable", "Yalnızca bekleyen veya başarısız kayıtlar yeniden denenebilir.");
        e.Status = AccountingStatus.Pending;
        e.Attempts = 0;
        e.LastError = null;
        e.NextAttemptAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "accounting.retry", "request", e.RequestId.ToString(), new { e.Number });
        await SaveAsync(ct);
        return (await ListAsync(actorId, workspaceId, null, null, null, ct)).First(x => x.Id == entryId);
    }

    // ===================================================================== CSV

    /// <summary>Excel (Türkçe yerel ayar) ile açılabilmesi için varsayılan ayırıcı ';' ve UTF-8 BOM; hücreler formül enjeksiyonuna karşı korunur.</summary>
    public async Task<(byte[] Data, string FileName)> ExportCsvAsync(Guid actorId, Guid workspaceId, string? status, string? from, string? to, string? delimiter, CancellationToken ct)
    {
        await RequireAccountingAsync(actorId, workspaceId, ct);
        var sep = delimiter switch { null or "" or ";" or "semicolon" => ';', "," or "comma" => ',', _ => throw new RequestValidationException("invalid_delimiter", "Ayırıcı ';' veya ',' olmalı.") };
        var rows = await QueryAsync(workspaceId, status, from, to, ct);

        var sb = new StringBuilder("﻿");
        sb.Append(Line(sep, "Numara", "Uygulama", "Kategori", "Aktarım durumu", "Talep eden", "E-posta", "Talep tarihi (UTC)", "Onay tarihi (UTC)", "İşlendi (UTC)", "İşleyen not", "Özet", "Alanlar"));
        foreach (var r in rows)
        {
            using var doc = JsonDocument.Parse(r.Entry.PayloadJson);
            var root = doc.RootElement;
            var requester = root.GetProperty("requester");
            var fileNames = root.TryGetProperty("attachments", out var at) && at.ValueKind == JsonValueKind.Array
                ? at.EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!, a => a.GetProperty("fileName").GetString()!) : [];
            var fields = root.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
                ? string.Join(" | ", f.EnumerateArray().Select(x => $"{x.GetProperty("label").GetString()}={FieldText(x, fileNames)}")) : "";
            sb.Append(Line(sep, r.Entry.Number, r.AppName, r.Entry.Category, Turkish(r.Entry.Status), requester.GetProperty("name").GetString(),
                requester.GetProperty("email").GetString(), Utc(root, "submittedAt"), Utc(root, "approvedAt"),
                r.Entry.ProcessedAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.Entry.Note,
                root.TryGetProperty("summary", out var sm) ? sm.GetString() : "", fields));
        }
        return (Encoding.UTF8.GetBytes(sb.ToString()), $"muhasebe-{clock.GetUtcNow():yyyyMMdd-HHmm}.csv");
    }

    private static string FieldText(JsonElement f, IReadOnlyDictionary<string, string> fileNames)
    {
        if (f.TryGetProperty("valueLabel", out var vl) && vl.ValueKind == JsonValueKind.String) return vl.GetString()!;
        var v = f.GetProperty("value");
        if (f.TryGetProperty("type", out var type) && type.GetString() == "file" && v.ValueKind == JsonValueKind.Array)
            return string.Join(", ", v.EnumerateArray().Select(x => fileNames.GetValueOrDefault(x.GetString() ?? "", "(dosya)")));
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()!,
            JsonValueKind.True => "Evet",
            JsonValueKind.False => "Hayır",
            JsonValueKind.Array => string.Join(", ", v.EnumerateArray().Select(x => x.ToString())),
            _ => v.GetRawText() + (f.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String ? " " + c.GetString() : ""),
        };
    }

    private static string? Utc(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null;

    private static string Line(char sep, params string?[] cells) => string.Join(sep, cells.Select(c => Cell(c, sep))) + "\r\n";

    /// <summary>
    /// CSV hücresi: ayırıcı/tırnak/satır sonu içerenler tırnaklanır. = + - @ ile (veya sekme/CR ile) başlayan metinlerin önüne ' konur;
    /// aksi hâlde Excel bunları formül olarak çalıştırabilir (CSV enjeksiyonu).
    /// </summary>
    public static string Cell(string? value, char sep)
    {
        var v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([sep, '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    // =============================================================== yardımcılar

    /// <summary>Master admin veya ayarlarda tanımlı muhasebe grubunun üyesi.</summary>
    public async Task RequireAccountingAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var m = await access.MemberAsync(userId, workspaceId, ct);
        if (m.Role == WorkspaceRole.Admin) return;
        var groupId = await db.AccountingSettings.AsNoTracking().Where(s => s.WorkspaceId == workspaceId).Select(s => s.AccountingGroupId).FirstOrDefaultAsync(ct);
        if (groupId is { } g && await db.GroupMembers.AsNoTracking().AnyAsync(gm => gm.GroupId == g && gm.UserId == userId, ct)) return;
        throw new ForbiddenException("Bu işlem için muhasebe yetkisi gerekir.");
    }

    private sealed record Row(AccountingEntry Entry, string AppName, string RequesterId);

    private async Task<List<Row>> QueryAsync(Guid workspaceId, string? status, string? from, string? to, CancellationToken ct)
    {
        var q = from e in db.AccountingEntries.AsNoTracking()
                join a in db.Apps.AsNoTracking() on e.AppId equals a.Id
                join r in db.FlowRequests.AsNoTracking() on e.RequestId equals r.Id
                where e.WorkspaceId == workspaceId
                select new { e, a.Name, r.RequesterId };
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AccountingStatus>(status.Replace("_", ""), true, out var st)) throw new RequestValidationException("invalid_status", "Durum pending, sent, processed veya failed olmalı.");
            q = q.Where(x => x.e.Status == st);
        }
        if (Day(from, nameof(from)) is { } f) q = q.Where(x => x.e.CreatedAt >= f);
        if (Day(to, nameof(to)) is { } t) q = q.Where(x => x.e.CreatedAt < t.AddDays(1));
        var rows = await q.OrderByDescending(x => x.e.CreatedAt).ThenByDescending(x => x.e.Id).Take(ListLimit).ToListAsync(ct);
        return rows.Select(x => new Row(x.e, x.Name, x.RequesterId.ToString())).ToList();
    }

    private static DateTimeOffset? Day(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? null
        : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : throw new RequestValidationException("invalid_date", $"'{name}' yyyy-AA-gg biçiminde olmalı.");

    private async Task<AccountingEntry> FindAsync(Guid workspaceId, Guid entryId, CancellationToken ct) =>
        await db.AccountingEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Muhasebe kaydı bulunamadı.");

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("concurrent_update", "Kayıt aynı anda değiştirildi. Yenileyip tekrar deneyin."); }
    }

    private static RequestEvent Event(AccountingEntry e, Guid? actor, string kind, object detail, DateTimeOffset at) => new()
    {
        RequestId = e.RequestId, WorkspaceId = e.WorkspaceId, At = at, ActorId = actor, Kind = kind, DetailJson = Arcvora.Core.Model.ArcvoraJson.Serialize(detail),
    };

    private static string ValidUrl(string url)
    {
        var u = url.Trim();
        if (u.Length > 500 || !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new RequestValidationException("invalid_url", "Webhook adresi http:// veya https:// ile başlayan geçerli bir adres olmalı (kullanıcı adı/parola içermemeli).");
        return uri.ToString();
    }

    private static AccountingSettingsDto ToDto(AccountingSettings? s) => new(s?.WebhookUrl, !string.IsNullOrEmpty(s?.WebhookSecret), s?.AccountingGroupId);

    public static string Status(AccountingStatus s) => s.ToString().ToLowerInvariant();

    private static string Turkish(AccountingStatus s) => s switch
    {
        AccountingStatus.Pending => "Beklemede",
        AccountingStatus.Sent => "Gönderildi",
        AccountingStatus.Processed => "İşlendi",
        _ => "Hata",
    };
}
