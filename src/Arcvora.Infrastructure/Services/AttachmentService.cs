using System.Security.Cryptography;
using Arcvora.Core.Files;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Files;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Fiş/belge/tarama ekleri. Yükleme iki adımlıdır: dosya önce "hazırlanır" (yalnızca yükleyen görür), talep gönderilince talebe bağlanır
/// (<see cref="RequestService"/>). Dosya türü içerikten belirlenir, ad temizlenir, indirme talebi görme kuralına bağlıdır.
/// </summary>
public sealed class AttachmentService(ArcvoraDbContext db, AppAccessService access, RequestVisibility visibility, IFileStore store,
    IOptions<AttachmentOptions> options, TimeProvider clock)
{
    private AttachmentOptions Options => options.Value;
    private long MaxBytes => Options.MaxSizeMb * 1024L * 1024L;

    public async Task<AttachmentDto> UploadAsync(Guid userId, Guid appId, string? fileName, Stream body, long? contentLength, CancellationToken ct)
    {
        var (app, member) = await access.LoadAppAsync(userId, appId, false, ct);
        await access.RequireUsableAsync(userId, member, app, ct);

        if (await db.Attachments.CountAsync(a => a.UploadedBy == userId && a.RequestId == null, ct) >= Options.MaxStagedPerUser)
            throw new ConflictException("too_many_staged_files", $"Talebe eklenmemiş en fazla {Options.MaxStagedPerUser} dosyanız olabilir. Kullanmayacaklarınızı silin.");
        if (contentLength > MaxBytes) throw TooLarge();

        var data = await ReadAsync(body, ct);
        if (data.Length == 0) throw new RequestValidationException("empty_file", "Dosya boş.");
        var kind = FileSniffer.Detect(data) ?? throw new AppException(415, "unsupported_file_type", "Yalnızca PDF, PNG, JPEG veya WEBP dosyaları yüklenebilir.");

        var now = clock.GetUtcNow();
        var att = new Attachment
        {
            Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, AppId = app.Id, UploadedBy = userId,
            FileName = FileSniffer.SafeName(fileName, kind), ContentType = kind.ContentType(), Size = data.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), CreatedAt = now,
        };
        db.Attachments.Add(att);
        await store.StageAsync(att.Id, data, ct);
        try { await db.SaveChangesAsync(ct); }
        catch { await store.DeleteAsync(att.Id, CancellationToken.None); throw; }       // disk deposunda yetim dosya bırakma
        return AttachmentList.ToDto(att);
    }

    /// <summary>Bağlı dosya: talebi görebilen herkes. Hazırlanmış dosya: yalnızca yükleyen (master admin dahil kimse başkasınınkini görmez).</summary>
    public async Task<(AttachmentDto Meta, byte[] Data)> DownloadAsync(Guid userId, Guid attachmentId, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct) ?? throw new NotFoundException("Dosya bulunamadı.");
        if (a.RequestId is { } rid) await visibility.LoadAsync(userId, rid, ct);
        else if (a.UploadedBy != userId || !await db.Memberships.AnyAsync(m => m.WorkspaceId == a.WorkspaceId && m.UserId == userId, ct))
            throw new NotFoundException("Dosya bulunamadı.");

        var data = await store.ReadAsync(a.Id, ct) ?? throw new NotFoundException("Dosya bulunamadı.");
        return (AttachmentList.ToDto(a), data);
    }

    public async Task DeleteStagedAsync(Guid userId, Guid attachmentId, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.UploadedBy == userId, ct) ?? throw new NotFoundException("Dosya bulunamadı.");
        if (a.RequestId is not null) throw new ConflictException("attachment_linked", "Talebe eklenmiş dosya silinemez.");
        await store.DeleteAsync(a.Id, ct);
        db.Attachments.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Süresi dolmuş, talebe bağlanmamış dosyaları siler. Bağlı dosyalara dokunmaz. Silinen sayıyı döndürür.</summary>
    public async Task<int> PurgeStagedAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddHours(-Options.StagedTtlHours);
        var total = 0;
        while (true)
        {
            var batch = await db.Attachments.Where(a => a.RequestId == null && a.CreatedAt <= cutoff).OrderBy(a => a.CreatedAt).Take(200).ToListAsync(ct);
            if (batch.Count == 0) return total;
            foreach (var a in batch) await store.DeleteAsync(a.Id, ct);
            db.Attachments.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            total += batch.Count;
        }
    }

    private AppException TooLarge() => new(413, "file_too_large", $"Dosya en fazla {Options.MaxSizeMb} MB olabilir.");

    /// <summary>Gövdeyi en fazla sınır+1 bayta kadar okur; sınır aşılırsa gerisini okumadan reddeder.</summary>
    private async Task<byte[]> ReadAsync(Stream body, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > MaxBytes) throw TooLarge();
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }
}
