using System.Text;
using Arcvora.Core.Model;
using Arcvora.Core.Profiles;
using Arcvora.Core.Validation;

namespace Arcvora.Core.Rendering;

public sealed record ChangeLogEntry(int Number, string Summary, DateTimeOffset CreatedAt, string Actor);

public sealed record HandoffContext
{
    public required int VersionNumber { get; init; }
    public required string ModelHash { get; init; }
    public IReadOnlyList<ChangeLogEntry> History { get; init; } = [];
}

public sealed record HandoffFile(string Path, string Content);

/// <summary>
/// Seçilen model sürümünden teknik devir Markdown'ı (§8.6, §18). Aynı model sürümü + aynı üretici sürümü
/// her zaman aynı çıktıyı üretir; zaman damgası içermez (hash karşılaştırılabilir kalır).
/// </summary>
public static class HandoffRenderer
{
    public const string GeneratorVersion = "arcvora-handoff/1";

    public const string Disclaimer =
        "Bu belge, ekip tarafından üzerinde anlaşılmış bir tasarım başlangıcını temsil eder; " +
        "\"üretime hazır olduğu doğrulandı\" anlamına gelmez. Sistem önerileri ve doğrulanmamış varsayımlar işaretlidir.";

    /// <summary>Tek dosyalık devir belgesi.</summary>
    public static string RenderSingle(ArchitectureModel m, HandoffContext ctx)
    {
        var files = RenderPackage(m, ctx);
        var sb = new StringBuilder();
        sb.Append(Header(m, ctx));
        foreach (var f in files.Where(f => f.Path != "CHANGELOG.md"))
        {
            sb.Append('\n').Append(StripTitle(f.Content));
        }
        var change = files.First(f => f.Path == "CHANGELOG.md");
        sb.Append('\n').Append(StripTitle(change.Content));
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Paket: PROJECT_OVERVIEW, REQUIREMENTS, ARCHITECTURE, DECISIONS, OPEN_QUESTIONS, CHANGELOG.</summary>
    public static IReadOnlyList<HandoffFile> RenderPackage(ArchitectureModel m, HandoffContext ctx) =>
    [
        new("PROJECT_OVERVIEW.md", Overview(m, ctx)),
        new("REQUIREMENTS.md", Requirements(m)),
        new("ARCHITECTURE.md", Architecture(m)),
        new("DECISIONS.md", Decisions(m)),
        new("OPEN_QUESTIONS.md", OpenQuestions(m)),
        new("CHANGELOG.md", Changelog(ctx)),
    ];

    // ------------------------------------------------------------- bölümler

    private static string Header(ArchitectureModel m, HandoffContext ctx)
    {
        var profile = ProfileCatalog.Find(m.Project.TechProfileId);
        var sb = new StringBuilder();
        sb.Append("# ").Append(Text(m.Project.Name)).Append(" — Teknik Devir\n\n");
        sb.Append("> Model sürümü: v").Append(ctx.VersionNumber)
          .Append(" · Model özeti: `").Append(ctx.ModelHash.Length >= 12 ? ctx.ModelHash[..12] : ctx.ModelHash).Append('`')
          .Append(" · Profil: ").Append(profile is null ? m.Project.TechProfileId : $"{profile.Name} (profil {profile.ProfileVersion})")
          .Append(" · Üretici: ").Append(GeneratorVersion).Append("\n>\n> ").Append(Disclaimer).Append('\n');
        return sb.ToString();
    }

    private static string Overview(ArchitectureModel m, HandoffContext ctx)
    {
        var sb = new StringBuilder(Header(m, ctx));
        sb.Append("\n## Proje kapsamı\n\n");
        sb.Append(string.IsNullOrWhiteSpace(m.Project.Description) ? "_Açıklama girilmemiş._" : Text(m.Project.Description)).Append("\n\n");

        if (m.Actors.Count > 0)
        {
            sb.Append("### Aktörler\n\n");
            foreach (var a in m.Actors.OrderBy(a => a.Id, StringComparer.Ordinal))
                sb.Append("- **").Append(Text(a.Name)).Append("** (`").Append(a.Id).Append("`)")
                  .Append(string.IsNullOrWhiteSpace(a.Description) ? "" : " — " + Text(a.Description)).Append('\n');
            sb.Append('\n');
        }

        if (m.Sources.Count > 0)
        {
            sb.Append("### Kaynaklar\n\n");
            foreach (var s in m.Sources.OrderBy(s => s.Id, StringComparer.Ordinal))
                sb.Append("- `").Append(s.Id).Append("` ").Append(string.IsNullOrWhiteSpace(s.Title) ? "Kaynak metin" : Text(s.Title)).Append('\n');
            sb.Append('\n');
        }

        var findings = ModelValidator.Validate(m).Findings;
        sb.Append("### Doğrulama özeti\n\n");
        sb.Append("| Tür | Hata | Uyarı | Bilgi |\n|---|--:|--:|--:|\n");
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var k = findings.Where(f => f.Kind == kind).ToList();
            sb.Append("| ").Append(KindLabel(kind)).Append(" | ").Append(k.Count(f => f.Severity == Severity.Error))
              .Append(" | ").Append(k.Count(f => f.Severity == Severity.Warning))
              .Append(" | ").Append(k.Count(f => f.Severity == Severity.Info)).Append(" |\n");
        }
        sb.Append("\n_İnceleme önerileri kural tabanlıdır ve deterministik test sonucu değildir._\n");

        var shown = findings.Where(f => f.Severity != Severity.Info).ToList();
        if (shown.Count > 0)
        {
            sb.Append("\n<details><summary>Bulgular</summary>\n\n");
            foreach (var f in shown)
                sb.Append("- **").Append(f.RuleId).Append("** [").Append(KindLabel(f.Kind)).Append(" · ").Append(SeverityLabel(f.Severity)).Append("] ")
                  .Append(f.ObjectId is null ? "" : $"`{f.ObjectId}` ").Append(Text(f.Message)).Append('\n');
            sb.Append("\n</details>\n");
        }
        return sb.ToString();
    }

    private static string Requirements(ArchitectureModel m)
    {
        var sb = new StringBuilder("## Gereksinimler\n\n");
        if (m.Requirements.Count == 0) return sb.Append("_Henüz gereksinim yok._\n").ToString();

        sb.Append("| ID | Gereksinim | Tür | Öncelik | Kaynak | Durum |\n|---|---|---|---|---|---|\n");
        foreach (var r in m.Requirements.OrderBy(r => r.Id, StringComparer.Ordinal))
            sb.Append("| `").Append(r.Id).Append("` | ").Append(Cell(r.Text)).Append(" | ").Append(KindLabel(r.Kind))
              .Append(" | ").Append(PriorityLabel(r.Priority)).Append(" | ").Append(OriginLabel(r.Origin))
              .Append(" | ").Append(ReviewLabel(r.ReviewStatus)).Append(" |\n");

        sb.Append("\n### Kabul ölçütleri\n\n");
        var any = false;
        foreach (var r in m.Requirements.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (r.AcceptanceCriteria.Count == 0) continue;
            any = true;
            sb.Append("**").Append(r.Id).Append("**\n\n");
            foreach (var ac in r.AcceptanceCriteria.OrderBy(a => a.Id, StringComparer.Ordinal))
                sb.Append("- `").Append(ac.Id).Append("` ").Append(Text(ac.Text)).Append('\n');
            sb.Append('\n');
        }
        if (!any) sb.Append("_Kabul ölçütü tanımlı değil._\n\n");

        sb.Append("### İzlenebilirlik\n\n");
        sb.Append("Bağlantı bulunması yeterlilik kanıtı değildir; boş hücre açık bir boşluktur.\n\n");
        sb.Append("| Gereksinim | Karar(lar) | Uygulayan nesneler | Kabul ölçütü |\n|---|---|---|--:|\n");
        foreach (var r in m.Requirements.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var decisions = m.TraceLinks.Where(t => t.Kind == TraceKind.Addresses && t.ToId == r.Id).Select(t => t.FromId)
                .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var impl = m.TraceLinks.Where(t => t.Kind == TraceKind.Implements && decisions.Contains(t.ToId)).Select(t => t.FromId)
                .Distinct().OrderBy(x => x, StringComparer.Ordinal);
            sb.Append("| `").Append(r.Id).Append("` | ").Append(Ids(decisions)).Append(" | ").Append(Ids(impl))
              .Append(" | ").Append(r.AcceptanceCriteria.Count).Append(" |\n");
        }
        return sb.ToString();
    }

    private static string Architecture(ArchitectureModel m)
    {
        var sb = new StringBuilder("## Mimari görünümler\n\n");
        sb.Append("Aşağıdaki diyagramlar aynı model sürümünden üretilmiştir. Bu dosyadaki Mermaid metninde yapılan dış düzenlemeler modele otomatik aktarılmaz.\n\n");

        Fence(sb, ViewRenderer.Component(m));
        sb.Append("### Bileşenler\n\n");
        if (m.Components.Count == 0) sb.Append("_Henüz bileşen yok._\n\n");
        else
        {
            sb.Append("| ID | Bileşen | Tür | Sorumluluk | Teknoloji | Veri sahipliği |\n|---|---|---|---|---|---|\n");
            foreach (var c in m.Components.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var owns = m.Entities.Where(e => e.OwnerComponentId == c.Id).Select(e => e.Name);
                sb.Append("| `").Append(c.Id).Append("` | ").Append(Cell(c.Name)).Append(" | ").Append(c.Kind).Append(" | ")
                  .Append(Cell(c.Responsibility)).Append(" | ").Append(Cell(c.Technology)).Append(" | ")
                  .Append(Cell(string.Join(", ", owns))).Append(" |\n");
            }
            sb.Append('\n');
        }

        if (m.Relationships.Count > 0)
        {
            sb.Append("### Bağlantılar\n\n| ID | Kaynak → Hedef | İletişim | Açıklama | Hata davranışı |\n|---|---|---|---|---|\n");
            var index = new ModelIndex(m);
            foreach (var r in m.Relationships.OrderBy(r => r.Id, StringComparer.Ordinal))
                sb.Append("| `").Append(r.Id).Append("` | ").Append(Cell(index.TitleOf(r.SourceId))).Append(" → ")
                  .Append(Cell(index.TitleOf(r.TargetId))).Append(" | ").Append(r.Communication).Append(" | ")
                  .Append(Cell(r.Description)).Append(" | ").Append(string.IsNullOrWhiteSpace(r.FailureBehavior) ? "_tanımsız_" : Cell(r.FailureBehavior)).Append(" |\n");
            sb.Append('\n');
        }

        sb.Append("### Veri modeli\n\n");
        Fence(sb, ViewRenderer.Er(m));

        sb.Append("### İş akışları\n\n");
        if (m.Flows.Count == 0) sb.Append("_Henüz akış yok._\n\n");
        foreach (var f in m.Flows.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            sb.Append("#### `").Append(f.Id).Append("` ").Append(Text(f.Name)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(f.Description)) sb.Append(Text(f.Description)).Append("\n\n");
            Fence(sb, ViewRenderer.Sequence(m, f));
        }

        sb.Append("### API taslağı\n\n");
        sb.Append("İncelenebilir taslaktır; doğrulanmış bir OpenAPI sözleşmesi değildir.\n\n");
        if (m.Endpoints.Count == 0) sb.Append("_Henüz endpoint yok._\n");
        else
        {
            sb.Append("| ID | Metot | Yol | Özet | Yetkilendirme | Durum |\n|---|---|---|---|---|---|\n");
            foreach (var e in m.Endpoints.OrderBy(e => e.Id, StringComparer.Ordinal))
                sb.Append("| `").Append(e.Id).Append("` | ").Append(Cell(e.Method.ToUpperInvariant())).Append(" | `").Append(Cell(e.Path)).Append("` | ")
                  .Append(Cell(e.Summary)).Append(" | ").Append(Cell(e.Authorization)).Append(" | ").Append(ReviewLabel(e.ReviewStatus)).Append(" |\n");
        }
        return sb.ToString();
    }

    private static string Decisions(ArchitectureModel m)
    {
        var sb = new StringBuilder("## Mimari kararlar\n\n");
        if (m.Decisions.Count == 0) return sb.Append("_Henüz karar yok._\n").ToString();

        var index = new ModelIndex(m);
        foreach (var d in m.Decisions.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            sb.Append("### `").Append(d.Id).Append("` ").Append(Text(d.Title)).Append("\n\n");
            sb.Append("**Kaynak:** ").Append(OriginLabel(d.Origin)).Append(" · **Durum:** ").Append(ReviewLabel(d.ReviewStatus)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(d.Choice)) sb.Append("**Seçim:** ").Append(Text(d.Choice)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(d.Rationale)) sb.Append("**Gerekçe:** ").Append(Text(d.Rationale)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(d.Consequences)) sb.Append("**Sonuçlar:** ").Append(Text(d.Consequences)).Append("\n\n");
            if (d.Alternatives.Count > 0)
            {
                sb.Append("**Alternatifler:**\n\n");
                foreach (var a in d.Alternatives)
                    sb.Append("- ").Append(Text(a.Name)).Append(string.IsNullOrWhiteSpace(a.Tradeoff) ? "" : " — " + Text(a.Tradeoff)).Append('\n');
                sb.Append('\n');
            }

            Links(sb, "Dayandığı gereksinimler", m.TraceLinks.Where(t => t.Kind == TraceKind.Addresses && t.FromId == d.Id).Select(t => t.ToId), index);
            Links(sb, "Uygulayan nesneler", m.TraceLinks.Where(t => t.Kind == TraceKind.Implements && t.ToId == d.Id).Select(t => t.FromId), index);
            Links(sb, "Dayandığı varsayımlar", m.TraceLinks.Where(t => t.Kind == TraceKind.Assumes && t.FromId == d.Id).Select(t => t.ToId), index);
            Links(sb, "Bağlı kararlar", m.TraceLinks.Where(t => t.Kind == TraceKind.DependsOn && t.FromId == d.Id).Select(t => t.ToId), index);
            Links(sb, "Bağlı açık sorular", m.TraceLinks.Where(t => t.Kind == TraceKind.BlockedBy && t.FromId == d.Id).Select(t => t.ToId), index);
        }
        return sb.ToString();
    }

    private static string OpenQuestions(ArchitectureModel m)
    {
        var sb = new StringBuilder("## Açık konular ve varsayımlar\n\n");
        sb.Append("### Açık sorular\n\n");
        if (m.OpenQuestions.Count == 0) sb.Append("_Açık soru yok._\n\n");
        else
        {
            sb.Append("| ID | Soru | Etki | Durum | Cevap | Bağlı nesneler |\n|---|---|---|---|---|---|\n");
            foreach (var q in m.OpenQuestions.OrderBy(q => q.Id, StringComparer.Ordinal))
            {
                var blocked = m.TraceLinks.Where(t => t.Kind == TraceKind.BlockedBy && t.ToId == q.Id).Select(t => t.FromId)
                    .OrderBy(x => x, StringComparer.Ordinal);
                sb.Append("| `").Append(q.Id).Append("` | ").Append(Cell(q.Text)).Append(" | ").Append(PriorityLabel(q.Impact))
                  .Append(" | ").Append(q.Status switch { QuestionStatus.Open => "Açık", QuestionStatus.Answered => "Cevaplandı", _ => "Ertelendi" })
                  .Append(" | ").Append(Cell(q.Answer)).Append(" | ").Append(Ids(blocked)).Append(" |\n");
            }
            sb.Append('\n');
        }

        sb.Append("### Varsayımlar\n\n");
        if (m.Assumptions.Count == 0) sb.Append("_Varsayım yok._\n");
        else
        {
            sb.Append("| ID | Varsayım | Gerekçe | Doğrulama |\n|---|---|---|---|\n");
            foreach (var a in m.Assumptions.OrderBy(a => a.Id, StringComparer.Ordinal))
                sb.Append("| `").Append(a.Id).Append("` | ").Append(Cell(a.Text)).Append(" | ").Append(Cell(a.Rationale)).Append(" | ")
                  .Append(a.Status switch { AssumptionStatus.Verified => "Doğrulandı", AssumptionStatus.Invalidated => "Geçersiz", _ => "Doğrulanmadı" })
                  .Append(" |\n");
        }
        return sb.ToString();
    }

    private static string Changelog(HandoffContext ctx)
    {
        var sb = new StringBuilder("## Sürüm geçmişi\n\n");
        if (ctx.History.Count == 0) return sb.Append("_Geçmiş kaydı yok._\n").ToString();
        sb.Append("| Sürüm | Değişiklik | Yapan | Zaman (UTC) |\n|--:|---|---|---|\n");
        foreach (var h in ctx.History.OrderByDescending(h => h.Number))
            sb.Append("| v").Append(h.Number).Append(" | ").Append(Cell(h.Summary)).Append(" | ").Append(Cell(h.Actor))
              .Append(" | ").Append(h.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm")).Append(" |\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------- yardımcı

    private static void Fence(StringBuilder sb, RenderedView v) =>
        sb.Append("```mermaid\n").Append(v.Mermaid.TrimEnd()).Append("\n```\n\n");

    private static void Links(StringBuilder sb, string title, IEnumerable<string> ids, ModelIndex index)
    {
        var list = ids.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (list.Count == 0) return;
        sb.Append("**").Append(title).Append(":** ")
          .Append(string.Join("; ", list.Select(id => $"`{id}` {Text(index.Find(id)?.Title ?? "?")}"))).Append("\n\n");
    }

    private static string Ids(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        return list.Count == 0 ? "—" : string.Join(", ", list.Select(i => $"`{i}`"));
    }

    /// <summary>Serbest metinde HTML etiketlerini etkisiz kılar.</summary>
    private static string Text(string? s) => (s ?? "").Replace("<", "&lt;").Replace(">", "&gt;").Trim();

    private static string Cell(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "—" : Text(s).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string StripTitle(string content)
    {
        // Birleşik belgede bölümler ## ile başlar; kendi başlıklarını korur.
        var firstSection = content.IndexOf("\n## ", StringComparison.Ordinal);
        if (content.StartsWith("# ", StringComparison.Ordinal) && firstSection >= 0) return content[(firstSection + 1)..];
        return content.StartsWith("# ", StringComparison.Ordinal) ? "" : content;
    }

    private static string OriginLabel(Origin o) => o switch
    {
        Origin.UserStatement => "Kullanıcı",
        Origin.SystemProposal => "Sistem önerisi",
        _ => "Varsayım",
    };

    private static string ReviewLabel(ReviewStatus s) => s switch
    {
        ReviewStatus.Confirmed => "Onaylandı",
        ReviewStatus.Rejected => "Reddedildi",
        ReviewStatus.Deferred => "Ertelendi",
        _ => "Önerildi",
    };

    private static string PriorityLabel(Priority p) => p switch
    {
        Priority.Critical => "Kritik",
        Priority.High => "Yüksek",
        Priority.Medium => "Orta",
        _ => "Düşük",
    };

    private static string KindLabel(RequirementKind k) => k switch
    {
        RequirementKind.Functional => "İşlevsel",
        RequirementKind.Quality => "Kalite",
        RequirementKind.Constraint => "Kısıt",
        _ => "Mevcut karar",
    };

    private static string KindLabel(FindingKind k) => k switch
    {
        FindingKind.Schema => "Şema",
        FindingKind.Reference => "Referans",
        FindingKind.Consistency => "Tutarlılık",
        FindingKind.Traceability => "İzlenebilirlik",
        FindingKind.Profile => "Profil",
        _ => "İnceleme önerisi",
    };

    private static string SeverityLabel(Severity s) => s switch
    {
        Severity.Error => "Hata",
        Severity.Warning => "Uyarı",
        _ => "Bilgi",
    };
}
