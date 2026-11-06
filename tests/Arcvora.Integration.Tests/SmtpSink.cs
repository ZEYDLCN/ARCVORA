using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Arcvora.Integration.Tests;

/// <summary>Sürece gömülü minimal SMTP alıcısı: gerçek SMTP yolunu (MailKit → soket) uçtan uca sınamak için. Kimlik doğrulama/TLS yok.</summary>
public sealed class SmtpSink : IDisposable
{
    public sealed record Received(string From, IReadOnlyList<string> To, byte[] Raw)
    {
        public MimeKit.MimeMessage Parse() => MimeKit.MimeMessage.Load(new MemoryStream(Raw));
    }

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Received> _mails = [];
    private readonly Task _loop;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public IReadOnlyList<Received> Mails { get { lock (_mails) return [.. _mails]; } }
    /// <summary>true dönerse alıcı 550 ile reddedilir.</summary>
    public Func<string, bool> RejectRecipient { get; set; } = _ => false;
    /// <summary>Sonraki bu kadar bağlantı selamlamadan hemen sonra koparılır (geçici hata).</summary>
    public int DropNextConnections { get; set; }
    public int Connections;

    public SmtpSink()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Handle(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task Handle(TcpClient client)
    {
        Interlocked.Increment(ref Connections);
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.Latin1, false, 4096, leaveOpen: true);
        async Task Say(string line) { var b = Encoding.ASCII.GetBytes(line + "\r\n"); await stream.WriteAsync(b); }
        try
        {
            if (DropNextConnections > 0) { DropNextConnections--; return; }
            await Say("220 sink ESMTP ready");
            string from = "";
            var to = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
            {
                var cmd = line.ToUpperInvariant();
                if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) await Say("250-sink\r\n250-8BITMIME\r\n250-SMTPUTF8\r\n250 SIZE 10485760");
                else if (cmd.StartsWith("MAIL FROM")) { from = Between(line); to.Clear(); await Say("250 2.1.0 ok"); }
                else if (cmd.StartsWith("RCPT TO"))
                {
                    var rcpt = Between(line);
                    if (RejectRecipient(rcpt)) await Say("550 5.1.1 no such user");
                    else { to.Add(rcpt); await Say("250 2.1.5 ok"); }
                }
                else if (cmd.StartsWith("DATA"))
                {
                    await Say("354 end with <CRLF>.<CRLF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } l)
                    {
                        if (l == ".") break;
                        data.Append(l.StartsWith("..") ? l[1..] : l).Append("\r\n");           // nokta kaçışını aç
                    }
                    lock (_mails) _mails.Add(new Received(from, [.. to], Encoding.Latin1.GetBytes(data.ToString())));
                    await Say("250 2.0.0 queued");
                }
                else if (cmd.StartsWith("RSET") || cmd.StartsWith("NOOP")) await Say("250 ok");
                else if (cmd.StartsWith("QUIT")) { await Say("221 bye"); return; }
                else await Say("502 5.5.2 command not recognized");
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static string Between(string line)
    {
        var a = line.IndexOf('<');
        var b = line.IndexOf('>');
        return a >= 0 && b > a ? line[(a + 1)..b] : line;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try { _loop.Wait(1000); } catch { /* yoksay */ }
    }
}
