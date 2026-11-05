using System.Net;
using Arcvora.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

public sealed class DiskApiFactory : ApiFactory
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "arcvora-files-" + Guid.NewGuid().ToString("N"));
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["Attachments:Store"] = "disk", ["Attachments:Path"] = Root,
    };

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

/// <summary>Aynı davranış, bayt deposu diskteyken: dosyalar klasörde, veritabanında yalnızca üst veri.</summary>
public class AttachmentDiskTests(DiskApiFactory factory) : IClassFixture<DiskApiFactory>
{
    private static IEnumerable<string> FilesOnDisk(string root) =>
        Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : [];

    [Fact]
    public async Task Files_live_on_disk_download_back_and_are_cleaned_up()
    {
        var admin = await Api.RegisterAsync(factory);
        var emp = await admin.NewUserAsync(factory);
        var app = await admin.PublishedAppAsync(AppsKit.ReceiptDef(), "Fişli", emp.UserId);

        var pdf = FilesKit.Pdf("diskte");
        var up = await emp.UploadAsync(app, pdf, "belge.pdf");
        Assert.True(up.Is(HttpStatusCode.Created), up.ToString());
        var id = up.Str("id");

        var path = Assert.Single(FilesOnDisk(factory.Root));
        Assert.Equal(pdf, await File.ReadAllBytesAsync(path));
        Assert.DoesNotContain(".tmp", path);
        Assert.Equal(pdf, (await emp.DownloadAsync($"/api/attachments/{id}")).Bytes);

        // Hazırlanmış dosya silinince diskten de gider.
        Assert.True((await emp.DeleteAsync($"/api/attachments/{id}")).Is(HttpStatusCode.NoContent));
        Assert.Empty(FilesOnDisk(factory.Root));

        // Süre dolan hazırlanmış dosya temizlenir; bağlı dosya kalır.
        var staged = (await emp.UploadAsync(app, FilesKit.Png())).Str("id");
        var linked = (await emp.UploadAsync(app, FilesKit.Png(), "bagli.png")).Str("id");
        Assert.True((await emp.SubmitAsync(app, new { amount = 5m, receipt = new[] { linked } })).Is(HttpStatusCode.Created));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeStagedAsync(CancellationToken.None));
        Assert.Single(FilesOnDisk(factory.Root));
        Assert.Equal(HttpStatusCode.NotFound, (await emp.DownloadAsync($"/api/attachments/{staged}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await emp.DownloadAsync($"/api/attachments/{linked}")).Status);
    }

    [Fact]
    public async Task A_missing_file_on_disk_is_a_clean_404_not_a_server_error()
    {
        var admin = await Api.RegisterAsync(factory);
        var emp = await admin.NewUserAsync(factory);
        var app = await admin.PublishedAppAsync(AppsKit.ReceiptDef(), "Fişli", emp.UserId);
        var id = (await emp.UploadAsync(app, FilesKit.Png())).Str("id");
        foreach (var f in FilesOnDisk(factory.Root).Where(f => f.Contains(Guid.Parse(id).ToString("N")))) File.Delete(f);
        Assert.Equal(HttpStatusCode.NotFound, (await emp.DownloadAsync($"/api/attachments/{id}")).Status);
    }

    [Fact]
    public async Task Demo_files_are_written_to_disk_and_removed_with_the_demo_data()
    {
        var admin = await Api.RegisterAsync(factory);
        var before = FilesOnDisk(factory.Root).Count();
        Assert.True((await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/demo-data")).Is(HttpStatusCode.Created));
        Assert.True(FilesOnDisk(factory.Root).Count() - before >= 4);
        Assert.True((await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/demo-data")).Is(HttpStatusCode.NoContent));
        Assert.Equal(before, FilesOnDisk(factory.Root).Count());
    }
}
