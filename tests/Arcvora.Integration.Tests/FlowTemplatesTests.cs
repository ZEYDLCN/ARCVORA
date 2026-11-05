using System.Text.Json;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Apps;

namespace Arcvora.Integration.Tests;

/// <summary>Sunucuya gömülü şablon kopyası, arayüzün tek kaynak dosyasıyla aynı ve geçerli olmalı.</summary>
public class FlowTemplatesTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arcvora.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir!.FullName, .. parts]);
    }

    [Fact]
    public void The_embedded_copy_is_identical_to_the_web_templates_file()
    {
        using var web = JsonDocument.Parse(File.ReadAllText(RepoFile("web", "src", "lib", "templates.json")));
        using var server = JsonDocument.Parse(File.ReadAllText(RepoFile("src", "Arcvora.Infrastructure", "Apps", "templates.json")));
        Assert.Equal(web.RootElement.GetRawText().Replace(" ", "").Replace("\n", "").Replace("\r", ""),
                     server.RootElement.GetRawText().Replace(" ", "").Replace("\n", "").Replace("\r", ""));
    }

    [Fact]
    public void Templates_load_and_every_definition_passes_the_validator()
    {
        Assert.Equal(["advance", "expense", "leave"], FlowTemplates.All.Select(t => t.Id).Order().ToArray());
        foreach (var t in FlowTemplates.All)
        {
            var problems = FlowValidator.Validate(t.Definition).Select(i => $"{i.Path}: {i.Message}").ToList();
            Assert.True(problems.Count == 0, $"{t.Id}: {string.Join("; ", problems)}");
        }
        Assert.Throws<KeyNotFoundException>(() => FlowTemplates.Get("yok"));
    }
}
