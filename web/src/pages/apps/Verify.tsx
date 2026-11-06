import { useState } from 'react';
import { IconCheckCircle, IconShield, IconXCircle } from '../../components/icons';
import { api } from '../../api';
import { requestStatusLabel } from '../../lib/requestText';
import type { VerifyResult } from '../../types';
import { errorText, when, type Ctx } from './common';

/** Basılı bir talep formunun alt bilgisindeki doğrulama kodunu sistemle karşılaştırır (yalnızca çalışma alanı üyeleri). */
export function Verify({ workspace }: Ctx) {
  const [number, setNumber] = useState('');
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<VerifyResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const check = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setResult(null);
    try { setResult(await api.verifyDocument(workspace.id, number.trim(), code.trim())); }
    catch (err) { setError(errorText(err, 'Doğrulama yapılamadı.')); }
    finally { setBusy(false); }
  };

  return (
    <>
      <div className="page-head"><h1>Belge doğrula</h1><p className="tagline">Basılı formun üstündeki talep numarasını ve alt bilgideki doğrulama kodunu girin.</p></div>
      <div className="page-body">
        <form className="card" style={{ maxWidth: 520 }} onSubmit={check}>
          <div className="field"><label htmlFor="vf-number">Talep numarası</label>
            <input id="vf-number" type="text" autoComplete="off" placeholder="IZN-2026-0007" maxLength={40} value={number} onChange={(e) => setNumber(e.target.value)} /></div>
          <div className="field"><label htmlFor="vf-code">Doğrulama kodu</label>
            <input id="vf-code" type="text" autoComplete="off" placeholder="ABCD-1234-EF56" maxLength={20} value={code} onChange={(e) => setCode(e.target.value)} /></div>
          <p><button className="btn primary" disabled={busy || number.trim() === '' || code.trim() === ''}>{!busy && <IconShield size={16} />}{busy ? 'Kontrol ediliyor…' : 'Doğrula'}</button></p>
        </form>
        {error && <div className="notice error" role="alert" style={{ marginTop: 12 }}>{error}</div>}
        {result?.valid && (
          <div className="notice ok" role="status" style={{ marginTop: 12 }}>
            <IconCheckCircle size={17} style={{ verticalAlign: '-3px', marginRight: 6 }} /><b>Belge geçerli.</b> {result.number} · {result.appName} · şu anki durum: <b>{result.statusLabel ?? (result.status ? requestStatusLabel[result.status] : '')}</b>
            {result.completedAt && <> · sonuç: {when(result.completedAt)}</>}
          </div>
        )}
        {result && !result.valid && (
          <div className="notice error" role="alert" style={{ marginTop: 12 }}>
            <IconXCircle size={17} style={{ verticalAlign: '-3px', marginRight: 6 }} /><b>Doğrulanamadı.</b> Kod bu talep numarasıyla eşleşmiyor ya da talep çıktıdan sonra değişmiş (ör. onaylandı veya iptal edildi). Güncel bir çıktı alın.
          </div>
        )}
      </div>
    </>
  );
}
