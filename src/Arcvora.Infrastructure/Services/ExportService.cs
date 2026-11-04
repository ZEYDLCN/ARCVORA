using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcvora.Core.Model;
using Arcvora.Core.Profiles;
using Arcvora.Core.Rendering;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Teknik devir çıktısı (§18). Her dışa aktarma; model sürümü, profil sürümü, üretici sürümü ve içerik hash'iyle
/// kaydedilir; hangi çıktının hangi tasarıma ait olduğu anlaşılır (§17.2).
/// </summary>
public sealed class ExportService(ArcvoraDbContext db, ProjectAccessService access, VersionStore versions, AuditLog audit, TimeProvider clock)
{
    public const string FormatMarkdown = "markdown";
    public const string FormatPackage = "package";

    public async Task<ArtifactDto> CreateAsync(Guid userId, Guid projectId, ExportRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var format = (req.Format ?? FormatMarkdown).ToLowerInvariant();
        if (format is not (FormatMarkdown or FormatPackage))
            throw new RequestValidationException("unknown_format", "Biçim 'markdown' veya 'package' olmalı.");

        var version = await versions.GetAsync(projectId, req.VersionNumber ?? ctx.Project.HeadNumber, ct);
        var model = VersionStore.Load(version);
        var profile = ProfileCatalog.Find(model.Project.TechProfileId) ?? ProfileCatalog.AspNetCore;

        var history = await (from v in db.ModelVersions.AsNoTracking()
                             join u in db.Users.AsNoTracking() on v.CreatedBy equals u.Id into users
                             from u in users.DefaultIfEmpty()
                             where v.ProjectId == projectId && v.Number <= version.Number
                             orderby v.Number
                             select new ChangeLogEntry(v.Number, v.Summary, v.CreatedAt, u == null ? "—" : u.DisplayName)).ToListAsync(ct);

        var handoff = new HandoffContext { VersionNumber = version.Number, ModelHash = version.ContentHash, History = history };
        var slug = Slug(model.Project.Name);

        byte[] data;
        string fileName, contentType;
        if (format == FormatMarkdown)
        {
            data = Encoding.UTF8.GetBytes(HandoffRenderer.RenderSingle(model, handoff));
            fileName = $"{slug}-v{version.Number}.md";
            contentType = "text/markdown; charset=utf-8";
        }
        else
        {
            data = BuildPackage(model, handoff, version, profile);
            fileName = $"{slug}-v{version.Number}.zip";
            contentType = "application/zip";
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var existing = await db.Artifacts.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectId == projectId && a.VersionId == version.Id
            && a.Format == format && a.GeneratorVersion == HandoffRenderer.GeneratorVersion && a.ContentHash == contentHash, ct);
        if (existing is not null) return ToDto(existing);

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(), ProjectId = projectId, VersionId = version.Id, VersionNumber = version.Number, Format = format,
            ProfileId = profile.Id, ProfileVersion = profile.ProfileVersion, GeneratorVersion = HandoffRenderer.GeneratorVersion,
            ModelHash = version.ContentHash, ContentHash = contentHash, FileName = fileName, ContentType = contentType,
            Data = data, CreatedBy = userId, CreatedAt = clock.GetUtcNow(),
        };
        db.Artifacts.Add(artifact);
        audit.Add(ctx.Project.WorkspaceId, projectId, userId, "export.created", "artifact", artifact.Id.ToString(),
            new { version = version.Number, format, contentHash });
        await db.SaveChangesAsync(ct);
        return ToDto(artifact);
    }

    public async Task<IReadOnlyList<ArtifactDto>> ListAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        return await db.Artifacts.AsNoTracking().Where(a => a.ProjectId == projectId).OrderByDescending(a => a.CreatedAt).Take(50)
            .Select(a => new ArtifactDto(a.Id, a.ProjectId, a.VersionNumber, a.Format, a.ProfileId, a.ProfileVersion, a.GeneratorVersion,
                a.ModelHash, a.ContentHash, a.FileName, a.ContentType, a.Data.Length, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<Artifact> DownloadAsync(Guid userId, Guid projectId, Guid artifactId, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        return await db.Artifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == artifactId && a.ProjectId == projectId, ct)
               ?? throw new NotFoundException("Çıktı bulunamadı.");
    }

    private static byte[] BuildPackage(ArchitectureModel model, HandoffContext handoff, ModelVersion version, TechProfile profile)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);   // deterministik içerik hash'i
                using var s = entry.Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }

            foreach (var f in HandoffRenderer.RenderPackage(model, handoff)) Add(f.Path, f.Content);
            Add("MANIFEST.json", JsonSerializer.Serialize(new
            {
                project = model.Project.Name,
                modelVersion = version.Number,
                modelHash = version.ContentHash,
                profile = new { profile.Id, profile.Name, profile.ProfileVersion },
                generator = HandoffRenderer.GeneratorVersion,
                notice = HandoffRenderer.Disclaimer,
            }, ArcvoraJson.Indented));
        }
        return ms.ToArray();
    }

    private static ArtifactDto ToDto(Artifact a, long? size = null) => new(a.Id, a.ProjectId, a.VersionNumber, a.Format, a.ProfileId,
        a.ProfileVersion, a.GeneratorVersion, a.ModelHash, a.ContentHash, a.FileName, a.ContentType, size ?? a.Data.Length, a.CreatedAt);

    private static string Slug(string name)
    {
        var s = MermaidText.Identifier(name, "arcvora").Replace('_', '-').Trim('-').ToLowerInvariant();
        return s.Length == 0 ? "arcvora" : s[..Math.Min(s.Length, 60)];
    }
}
