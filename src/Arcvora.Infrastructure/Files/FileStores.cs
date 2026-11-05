using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Files;

/// <summary>
/// Dosya baytlarının deposu. <see cref="StageAsync"/> yazmayı hazırlar: veritabanı deposunda aynı <c>SaveChanges</c> ile
/// atomik olarak yazılır; disk deposunda dosya hemen yazılır (çağıran, kayıt başarısız olursa <see cref="DeleteAsync"/> çağırır).
/// </summary>
public interface IFileStore
{
    Task StageAsync(Guid id, byte[] data, CancellationToken ct);
    Task<byte[]?> ReadAsync(Guid id, CancellationToken ct);
    /// <summary>Veritabanı deposunda kayıt silinirken satır zaten cascade ile gider; yine de güvenle çağrılabilir.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class DbFileStore(ArcvoraDbContext db) : IFileStore
{
    public Task StageAsync(Guid id, byte[] data, CancellationToken ct)
    {
        db.AttachmentBlobs.Add(new AttachmentBlob { AttachmentId = id, Data = data });
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(Guid id, CancellationToken ct) =>
        db.AttachmentBlobs.AsNoTracking().Where(b => b.AttachmentId == id).Select(b => (byte[]?)b.Data).FirstOrDefaultAsync(ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        await db.AttachmentBlobs.Where(b => b.AttachmentId == id).ExecuteDeleteAsync(ct);
}

/// <summary>Kök klasör altında <c>aa/aabbcc…</c> dosyaları. Yol yalnızca sunucunun ürettiği GUID'den oluşur; kullanıcı girdisi yola girmez.</summary>
public sealed class DiskFileStore(string root) : IFileStore
{
    private string PathOf(Guid id)
    {
        var n = id.ToString("N");
        return Path.Combine(root, n[..2], n);
    }

    public async Task StageAsync(Guid id, byte[] data, CancellationToken ct)
    {
        var path = PathOf(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, path, overwrite: true);       // yarım dosya hiç görünmesin
    }

    public async Task<byte[]?> ReadAsync(Guid id, CancellationToken ct)
    {
        var path = PathOf(id);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var path = PathOf(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
