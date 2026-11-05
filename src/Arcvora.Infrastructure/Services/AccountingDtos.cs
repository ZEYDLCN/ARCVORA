using System.Text.Json;

namespace Arcvora.Infrastructure.Services;

public sealed record AccountingSettingsDto(string? WebhookUrl, bool HasSecret, Guid? AccountingGroupId);

/// <summary>Alan null ise değişmez; kaldırmak için ilgili Clear bayrağı kullanılır.</summary>
public sealed record UpdateAccountingSettingsRequest(string? WebhookUrl, bool? ClearWebhook, string? WebhookSecret, bool? ClearSecret,
    Guid? AccountingGroupId, bool? ClearGroup);

public sealed record AccountingEntryDto(Guid Id, Guid RequestId, string Number, Guid AppId, string AppName, string Category, string Status,
    int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, Guid? ProcessedBy, string? ProcessedByName,
    DateTimeOffset? ProcessedAt, string? Note, JsonElement Payload);

public sealed record ProcessEntryRequest(string? Note);
