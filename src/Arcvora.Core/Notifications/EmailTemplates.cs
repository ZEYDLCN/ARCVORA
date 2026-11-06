namespace Arcvora.Core.Notifications;

public sealed record MailContent(string Subject, string Text, string Html);

public sealed record MailContext(string RecipientName, string Number, string AppName, string RequesterName, string Summary, string? Link);

/// <summary>İskelet (henüz uygulanmadı).</summary>
public static class EmailTemplates
{
    public static MailContent TaskAssigned(MailContext c, string step) => new("", "", "");
    public static MailContent TaskReminder(MailContext c, string step, int daysWaiting) => new("", "", "");
    public static MailContent RequestApproved(MailContext c) => new("", "", "");
    public static MailContent RequestRejected(MailContext c, string step, string? comment) => new("", "", "");
    public static MailContent RequestCancelled(MailContext c, string byName) => new("", "", "");
    public static MailContent DelegationGranted(string recipient, string fromName, DateOnly start, DateOnly end, string? reason, string? link) => new("", "", "");
    public static MailContent DelegationRevoked(string recipient, string otherName, string byName) => new("", "", "");
    public static MailContent UserInvited(string recipient, string email, string? link) => new("", "", "");
    public static MailContent Test(string recipient) => new("", "", "");
    public static string? RequestLink(string? baseUrl, Guid id) => null;
    public static string? PageLink(string? baseUrl, string page) => null;
}
