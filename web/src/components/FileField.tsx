import { useId, useRef, useState } from 'react';
import { checkPick, formatSize, type PickedFile } from '../lib/files';

export interface UploadApi {
  upload: (file: File) => Promise<PickedFile>;
  remove: (id: string) => Promise<void>;
}

interface Props {
  label: string;
  value: PickedFile[];
  maxFiles: number;
  uploads?: UploadApi;
  disabled?: boolean;
  invalid?: boolean;
  describedBy?: string;
  onChange: (files: PickedFile[]) => void;
}

/**
 * Fiş/belge/tarama seçici. Dosya seçilir seçilmez yüklenir ("hazırlanır"); form gönderilince talebe bağlanır.
 * Telefonda "Kamera ile çek" doğrudan kamerayı açar (fiş fotoğrafı/tarama).
 */
export function FileField({ label, value, maxFiles, uploads, disabled, invalid, describedBy, onChange }: Props) {
  const id = useId();
  const pick = useRef<HTMLInputElement>(null);
  const cam = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(0);
  const [problems, setProblems] = useState<string[]>([]);
  const room = maxFiles - value.length;
  const current = useRef(value);
  current.current = value;

  const handle = async (files: FileList | null) => {
    if (!files || files.length === 0 || !uploads) return;
    const list = [...files];
    const errs: string[] = [];
    const accepted: File[] = [];
    for (const f of list) {
      const why = checkPick(f);
      if (why) errs.push(why);
      else if (accepted.length >= room) errs.push(`En fazla ${maxFiles} dosya eklenebilir; “${f.name}” eklenmedi.`);
      else accepted.push(f);
    }
    setProblems(errs);
    // Art arda yüklemelerde her sonuç öncekilerin üstüne eklenir (liste yeniden çizilmeden bir sonraki bitebilir).
    let acc = current.current;
    for (const f of accepted) {
      setBusy((n) => n + 1);
      try {
        const up = await uploads.upload(f);
        acc = [...acc, up];
        onChange(acc);
      } catch (e) {
        setProblems((p) => [...p, `“${f.name}”: ${e instanceof Error ? e.message : 'yüklenemedi.'}`]);
      } finally { setBusy((n) => n - 1); }
    }
    if (pick.current) pick.current.value = '';
    if (cam.current) cam.current.value = '';
  };

  const remove = async (f: PickedFile) => {
    onChange(value.filter((x) => x.id !== f.id));
    try { await uploads?.remove(f.id); } catch { /* süresi dolunca zaten temizlenir */ }
  };

  const full = room <= 0;
  return (
    <div className="file-field" role="group" aria-label={label} aria-invalid={invalid || undefined} aria-describedby={describedBy}>
      {value.length > 0 && (
        <ul className="file-list">
          {value.map((f) => (
            <li key={f.id}>
              <span className="file-name">{f.fileName}</span>
              <span className="muted">{formatSize(f.size)}</span>
              <button type="button" className="btn small" disabled={disabled} onClick={() => void remove(f)} aria-label={`${f.fileName} dosyasını kaldır`}>Kaldır</button>
            </li>
          ))}
        </ul>
      )}
      <div className="row" style={{ gap: 8 }}>
        <input ref={pick} id={`${id}-pick`} type="file" hidden multiple={maxFiles > 1} accept=".pdf,.png,.jpg,.jpeg,.webp,application/pdf,image/png,image/jpeg,image/webp"
          onChange={(e) => void handle(e.target.files)} />
        <input ref={cam} id={`${id}-cam`} type="file" hidden accept="image/*" capture="environment" onChange={(e) => void handle(e.target.files)} />
        <button type="button" className="btn" disabled={disabled || full || busy > 0 || !uploads} onClick={() => pick.current?.click()}>Dosya seç</button>
        <button type="button" className="btn" disabled={disabled || full || busy > 0 || !uploads} onClick={() => cam.current?.click()}>Kamera ile çek</button>
        {busy > 0 && <span className="muted" role="status">Yükleniyor…</span>}
        {full && <span className="muted">En fazla {maxFiles} dosya.</span>}
      </div>
      <div className="help">PDF, PNG, JPEG veya WEBP · en fazla 10 MB · en fazla {maxFiles} dosya</div>
      {problems.length > 0 && <ul className="issues" role="alert" style={{ color: 'var(--red)' }}>{problems.map((m) => <li key={m}>{m}</li>)}</ul>}
    </div>
  );
}
