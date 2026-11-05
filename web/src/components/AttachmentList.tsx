import { useEffect, useState } from 'react';
import { api } from '../api';
import { formatSize } from '../lib/files';
import type { AttachmentInfo } from '../types';

const isImage = (a: AttachmentInfo) => a.contentType.startsWith('image/');

function save(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

function Thumb({ a }: { a: AttachmentInfo }) {
  const [src, setSrc] = useState<string | null>(null);
  useEffect(() => {
    let url: string | null = null;
    let dead = false;
    if (typeof URL.createObjectURL !== 'function') return;
    api.attachmentBlob(a.id).then((b) => { if (!dead) { url = URL.createObjectURL(b); setSrc(url); } }).catch(() => undefined);
    return () => { dead = true; if (url) URL.revokeObjectURL(url); };
  }, [a.id]);
  // Alt metin dosya adıdır; içerik kullanıcıya aittir ve yalnızca <img> ile gösterilir (HTML olarak çalıştırılmaz).
  return src ? <img src={src} alt={a.fileName} className="thumb" /> : <span className="thumb placeholder" aria-hidden="true">{isImage(a) ? '🖼' : '📄'}</span>;
}

/** Talebin ekleri: görsellerde küçük önizleme, hepsinde kaydetme düğmesi. Dosyalar her zaman "indir" olarak sunulur. */
export function AttachmentList({ attachments, labels = {} }: { attachments: AttachmentInfo[]; labels?: Record<string, string> }) {
  const [error, setError] = useState<string | null>(null);
  if (attachments.length === 0) return <p className="muted">Ek yok.</p>;

  const open = async (a: AttachmentInfo) => {
    setError(null);
    try { save(await api.attachmentBlob(a.id), a.fileName); } catch { setError(`“${a.fileName}” indirilemedi.`); }
  };

  return (
    <>
      {error && <div className="notice error" role="alert">{error}</div>}
      <ul className="attachments">
        {attachments.map((a) => (
          <li key={a.id} data-testid={`attachment-${a.id}`}>
            {isImage(a) ? <Thumb a={a} /> : <span className="thumb placeholder" aria-hidden="true">📄</span>}
            <span className="attachment-info">
              <b>{a.fileName}</b>
              <span className="muted">{a.fieldKey ? `${labels[a.fieldKey] ?? a.fieldKey} · ` : ''}{formatSize(a.size)}</span>
            </span>
            <button type="button" className="btn small" onClick={() => void open(a)} aria-label={`${a.fileName} dosyasını indir`}>İndir</button>
          </li>
        ))}
      </ul>
    </>
  );
}
