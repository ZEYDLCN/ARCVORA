using System.Text.Json;

namespace Arcvora.Core.Flows;

public enum FieldType { Text, LongText, Number, Money, Date, Select, MultiSelect, Checkbox, User, File }

public enum ApproverType { Manager, User, Group, Admin }

/// <summary><c>Any</c>: atananlardan biri onaylasa adım geçer. <c>All</c>: hepsi onaylamalı.</summary>
public enum ApprovalMode { Any, All }

public enum ConditionOp { Eq, Ne, Gt, Gte, Lt, Lte, In }

public enum ComputedOp { CalendarDays, Weekdays }

/// <summary>Başka iki tarih alanından hesaplanan sayı (ör. izin gün sayısı). Kullanıcı girmez; sunucu hesaplar.</summary>
public sealed record FlowComputed
{
    public required ComputedOp Op { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
}

public sealed record FlowField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required FieldType Type { get; init; }
    public bool Required { get; init; }
    public string? HelpText { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public int? MaxLength { get; init; }
    /// <summary>Bu tarih, başka bir tarih alanından önce olamaz (ör. bitiş &gt;= başlangıç).</summary>
    public string? NotBefore { get; init; }
    public FlowComputed? Computed { get; init; }
    /// <summary>Yalnızca <c>money</c> için para birimi etiketi (varsayılan TRY).</summary>
    public string? Currency { get; init; }
    /// <summary>Yalnızca <c>file</c> için: bir talebe eklenebilecek en fazla dosya (1–10, varsayılan 3).</summary>
    public int? MaxFiles { get; init; }
    /// <summary>Yalnızca <c>select</c>/<c>multi_select</c> için: sabit <see cref="Options"/> yerine yönetilen bir listeye (ana veri) bağlanır.</summary>
    public Guid? ListId { get; init; }
}

public sealed record ApproverSpec
{
    public required ApproverType Type { get; init; }
    /// <summary><c>user</c> için kullanıcı, <c>group</c> için grup kimliği.</summary>
    public Guid? Id { get; init; }
}

public sealed record FlowCondition
{
    public required string Field { get; init; }
    public required ConditionOp Op { get; init; }
    public JsonElement? Value { get; init; }
}

public sealed record FlowStep
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ApproverSpec Approvers { get; init; }
    public ApprovalMode Mode { get; init; } = ApprovalMode.Any;
    /// <summary>Hepsi sağlanırsa adım çalışır; aksi hâlde atlanır. Boşsa her zaman çalışır.</summary>
    public IReadOnlyList<FlowCondition> Conditions { get; init; } = [];
}

public sealed record AccountingSpec
{
    public bool Enabled { get; init; }
    public string? Category { get; init; }
}

public sealed record FlowCompletion
{
    public AccountingSpec? Accounting { get; init; }
}

/// <summary>Bir akış uygulamasının tanımı (schema 1). Yayınlanınca değişmez sürüm olur.</summary>
public sealed record FlowDefinition
{
    public int Schema { get; init; } = 1;
    /// <summary>Talep numarası öneki: <c>IZN</c> → IZN-2026-0007.</summary>
    public string Prefix { get; init; } = "";
    public IReadOnlyList<string> SummaryFields { get; init; } = [];
    public IReadOnlyList<FlowField> Fields { get; init; } = [];
    public IReadOnlyList<FlowStep> Steps { get; init; } = [];
    public FlowCompletion? Completion { get; init; }
}

/// <summary>Bir doğrulama bulgusu. <see cref="Path"/> örn. <c>fields[2].key</c> veya <c>data.start</c>.</summary>
public sealed record FlowIssue(string Code, string Path, string Message);
