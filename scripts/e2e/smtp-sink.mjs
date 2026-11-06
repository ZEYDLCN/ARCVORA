// Uçtan uca testler için minimal SMTP alıcısı + HTTP denetim ucu (kimlik doğrulama/TLS yok; yalnızca yerel).
//   SMTP_PORT (varsayılan 2525) üzerinde posta alır; SMTP_HTTP_PORT (varsayılan 2526) üzerinde:
//     GET /messages  → [{ from, to[], subject, text, html }]   ·   DELETE /messages → temizle
import http from 'node:http';
import net from 'node:net';
import { pathToFileURL } from 'node:url';

const SMTP_PORT = Number(process.env.SMTP_PORT ?? 2525);
const HTTP_PORT = Number(process.env.SMTP_HTTP_PORT ?? 2526);
const mails = [];

/** RFC 2047 kodlu sözcükleri ("=?utf-8?b?...?=" / "=?utf-8?q?...?=") çözer; bitişik kodlu sözcükler arasındaki boşluk atılır. */
export function decodeWords(value) {
  const joined = value.replace(/\?=\s+=\?/g, '?==?');
  return joined.replace(/=\?([^?]+)\?([bBqQ])\?([^?]*)\?=/g, (_, charset, enc, data) => {
    const bytes = enc.toLowerCase() === 'b'
      ? Buffer.from(data, 'base64')
      : Buffer.from(data.replace(/_/g, ' ').replace(/=([0-9A-Fa-f]{2})/g, (__, h) => String.fromCharCode(parseInt(h, 16))), 'latin1');
    return bytes.toString(/utf-?8/i.test(charset) ? 'utf8' : 'latin1');
  });
}

function qpDecode(s) {
  return Buffer.from(s.replace(/=\r?\n/g, '').replace(/=([0-9A-Fa-f]{2})/g, (_, h) => String.fromCharCode(parseInt(h, 16))), 'latin1').toString('utf8');
}

function parseHeaders(block) {
  const headers = {};
  const lines = block.replace(/\r\n[ \t]+/g, ' ').split('\r\n');          // katlanmış başlıkları aç
  for (const line of lines) {
    const i = line.indexOf(':');
    if (i > 0) headers[line.slice(0, i).trim().toLowerCase()] = line.slice(i + 1).trim();
  }
  return headers;
}

function decodeBody(body, headers) {
  const cte = (headers['content-transfer-encoding'] ?? '').toLowerCase();
  if (cte === 'base64') return Buffer.from(body.replace(/\s+/g, ''), 'base64').toString('utf8');
  if (cte === 'quoted-printable') return qpDecode(body);
  return Buffer.from(body, 'latin1').toString('utf8');
}

/** Ham iletiyi { subject, to, text, html } olarak ayrıştırır (multipart/alternative dahil). */
export function parseMessage(raw) {
  const split = raw.indexOf('\r\n\r\n');
  const headers = parseHeaders(split < 0 ? raw : raw.slice(0, split));
  const body = split < 0 ? '' : raw.slice(split + 4);
  const out = { subject: decodeWords(headers.subject ?? ''), toHeader: decodeWords(headers.to ?? ''), text: '', html: '' };
  const place = (h, b) => {
    const type = (h['content-type'] ?? 'text/plain').toLowerCase();
    const boundary = /boundary="?([^";]+)"?/i.exec(h['content-type'] ?? '')?.[1];
    if (type.startsWith('multipart/') && boundary) {
      for (const part of b.split(`--${boundary}`).slice(1)) {
        if (part.startsWith('--')) break;
        const trimmed = part.replace(/^\r\n/, '');
        const s = trimmed.indexOf('\r\n\r\n');
        place(parseHeaders(s < 0 ? trimmed : trimmed.slice(0, s)), s < 0 ? '' : trimmed.slice(s + 4));
      }
    } else if (type.startsWith('text/html')) out.html = decodeBody(b, h);
    else if (type.startsWith('text/plain')) out.text = decodeBody(b, h);
  };
  place(headers, body);
  return out;
}

function handle(socket) {
  let from = '';
  let to = [];
  let inData = false;
  let data = [];
  let buffer = '';
  const say = (line) => socket.write(line + '\r\n');
  socket.setEncoding('latin1');
  say('220 e2e-sink ESMTP ready');
  socket.on('data', (chunk) => {
    buffer += chunk;
    let i;
    while ((i = buffer.indexOf('\r\n')) >= 0) {
      const line = buffer.slice(0, i);
      buffer = buffer.slice(i + 2);
      if (inData) {
        if (line === '.') {
          inData = false;
          mails.push({ from, to: [...to], ...parseMessage(data.join('\r\n') + '\r\n') });
          data = [];
          say('250 2.0.0 queued');
        } else data.push(line.startsWith('..') ? line.slice(1) : line);
        continue;
      }
      const cmd = line.toUpperCase();
      const addr = /<([^>]*)>/.exec(line)?.[1] ?? line;
      if (cmd.startsWith('EHLO') || cmd.startsWith('HELO')) say('250-e2e-sink\r\n250-8BITMIME\r\n250-SMTPUTF8\r\n250 SIZE 10485760');
      else if (cmd.startsWith('MAIL FROM')) { from = addr; to = []; say('250 2.1.0 ok'); }
      else if (cmd.startsWith('RCPT TO')) { to.push(addr); say('250 2.1.5 ok'); }
      else if (cmd.startsWith('DATA')) { inData = true; say('354 end with <CRLF>.<CRLF>'); }
      else if (cmd.startsWith('RSET') || cmd.startsWith('NOOP')) say('250 ok');
      else if (cmd.startsWith('QUIT')) { say('221 bye'); socket.end(); }
      else say('502 5.5.2 command not recognized');
    }
  });
  socket.on('error', () => {});
}

// Doğrudan çalıştırıldığında sunucuları başlat (modül olarak içe aktarıldığında başlatma: ayrıştırıcı birim testi için).
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  net.createServer(handle).listen(SMTP_PORT, '127.0.0.1', () => console.log(`smtp-sink: SMTP ${SMTP_PORT}, HTTP ${HTTP_PORT}`));
  http.createServer((req, res) => {
    if (req.url?.startsWith('/messages') && req.method === 'DELETE') { mails.length = 0; res.writeHead(204).end(); return; }
    if (req.url?.startsWith('/messages')) { res.writeHead(200, { 'content-type': 'application/json' }).end(JSON.stringify(mails)); return; }
    res.writeHead(200).end('ok');
  }).listen(HTTP_PORT, '127.0.0.1');
}
