using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Arcvora.Infrastructure.Common;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Arcvora.Infrastructure.Mail;

public sealed record OutgoingMail(string ToAddress, string ToName, string Subject, string Text, string Html);

/// <summary>Gönderim hatası. <see cref="Permanent"/>: yeniden denemenin anlamı yok (ör. alıcı adresi reddedildi).</summary>
public sealed class EmailSendException(string message, bool permanent, Exception? inner = null) : Exception(message, inner)
{
    public bool Permanent { get; } = permanent;
}

public interface IEmailSender
{
    Task SendAsync(OutgoingMail mail, CancellationToken ct);
}

/// <summary>SMTP (MailKit). Hata iletilerine kimlik bilgisi sızdırılmaz.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        try { message.To.Add(new MailboxAddress(mail.ToName, mail.ToAddress)); }
        catch (ParseException ex) { throw new EmailSendException("Alıcı adresi geçersiz.", permanent: true, ex); }
        message.Subject = mail.Subject;
        var alt = new MultipartAlternative { new TextPart(TextFormat.Plain) { Text = mail.Text }, new TextPart(TextFormat.Html) { Text = mail.Html } };
        message.Body = alt;
        message.Date = DateTimeOffset.UtcNow;

        using var client = new SmtpClient { Timeout = Math.Max(1, o.TimeoutSeconds) * 1000 };
        if (o.AllowInvalidCertificate) client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        var secure = o.Security.ToLowerInvariant() switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        try
        {
            await client.ConnectAsync(o.Host, o.Port, secure, ct);
            if (!string.IsNullOrEmpty(o.User)) await client.AuthenticateAsync(o.User, o.Password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.MessageNotAccepted && (int)ex.StatusCode >= 500)
        {
            throw new EmailSendException($"Sunucu iletiyi reddetti ({(int)ex.StatusCode}).", permanent: true, ex);
        }
        catch (AuthenticationException ex) { throw new EmailSendException("SMTP kimlik doğrulaması başarısız (kullanıcı adı/parola).", permanent: false, ex); }
        catch (Exception ex) when (ex is ServiceNotConnectedException or ServiceNotAuthenticatedException or SmtpProtocolException or SmtpCommandException
                                    or System.Net.Sockets.SocketException or IOException or TimeoutException or System.Security.Authentication.AuthenticationException)
        {
            throw new EmailSendException($"SMTP gönderimi başarısız: {ex.GetType().Name}", permanent: false, ex);
        }
    }
}
