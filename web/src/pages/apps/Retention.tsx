import { useCallback, useEffect, useState } from 'react';
import { IconCheckCircle, IconClock, IconUser } from '../../components/icons';
import { CardTitle, EmptyState } from '../../components/ui';
import { api } from '../../api';
import type { FormerUser, RetentionInfo, RetentionPreview } from '../../types';
import { errorText } from './common';

const periods = [6, 12, 24, 36, 60, 120];

/** Saklama süresi ve KVKK araçları: eski talepleri önizleyip onayla silme, otomatik silme, ayrılan kullanıcıları anonimleştirme. */
export function Retention({ ws }: { ws: string }) {
  const [info, setInfo] = useState<RetentionInfo | null>(null);
  const [preview, setPreview] = useState<RetentionPreview | null>(null);
  const [former, setFormer] = useState<FormerUser[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    api.retention(ws).then(setInfo).catch((e) => setError(errorText(e, 'Ayarlar yüklenemedi.')));
    api.formerUsers(ws).then(setFormer).catch(() => setFormer([]));
  }, [ws]);
  useEffect(load, [load]);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try { await fn(); } catch (e) { setError(errorText(e)); } finally { setBusy(false); load(); }
  };

  const months = info?.closedRequestMonths;
  return (
    <div className="cards" style={{ maxWidth: 860 }}>
      <div className="card">
        <CardTitle icon={<IconClock size={19} />}>Saklama süresi</CardTitle>
        <p className="muted">Sonuçlanmış (onaylı, reddedilmiş, iptal) talepler bu süreden sonra silinebilir. Açık talepler ve muhasebede işlenmemiş kayıtlar asla silinmez. Talebin görevleri, geçmişi ve ekleri (dosyalar dahil) birlikte silinir.</p>
        {error && <div className="notice error" role="alert">{error}</div>}
        {notice && <div className="notice" role="status">{notice}</div>}
        {!info && !error && <p className="muted">Yükleniyor…</p>}
        {info && (
          <>
            <div className="field"><label htmlFor="rt-m">Süre</label>
              <select id="rt-m" value={months ?? ''} disabled={busy} onChange={(e) => { setPreview(null); void run(async () => { const v = e.target.value; setInfo(await api.updateRetention(ws, v === '' ? { clear: true } : { closedRequestMonths: Number(v) })); }); }}>
                <option value="">Süresiz sakla</option>
                {periods.map((p) => <option key={p} value={p}>{p} ay</option>)}
              </select></div>
            <div className="check-row">
              <input id="rt-auto" type="checkbox" checked={info.autoPurge} disabled={busy || months === undefined}
                onChange={(e) => void run(async () => setInfo(await api.updateRetention(ws, { autoPurge: e.target.checked })))} />
              <label htmlFor="rt-auto" style={{ margin: 0 }}>Süresi dolanları otomatik sil <span className="muted">(kapalıyken yalnızca onayınızla silinir)</span></label>
            </div>
            <p className="row" style={{ marginTop: 12 }}>
              <button className="btn" disabled={busy || months === undefined} onClick={() => void run(async () => setPreview(await api.retentionPreview(ws)))}>Önizle</button>
              <button className="btn danger" disabled={busy || !preview || preview.eligible === 0}
                onClick={() => {
                  if (!window.confirm(`${preview!.eligible} talep ve ekleri KALICI olarak silinecek. Bu işlem geri alınamaz. Devam edilsin mi?`)) return;
                  void run(async () => { const r = await api.retentionPurge(ws); setPreview(null); setNotice(`${r.purged} talep silindi${r.skipped ? `, ${r.skipped} talep muhasebe işlemediği için korundu` : ''}.`); });
                }}>Önizlenenleri sil</button>
            </p>
            {preview && <p>Silinecek: <b>{preview.eligible}</b> talep{preview.skippedAccounting > 0 && <> · muhasebe işlemediği için korunacak: <b>{preview.skippedAccounting}</b></>}</p>}
          </>
        )}
      </div>

      <div className="card">
        <CardTitle icon={<IconUser size={19} />}>Ayrılan kullanıcılar (anonimleştirme)</CardTitle>
        <p className="muted">Çalışma alanından çıkarılmış hesapların ad ve e-postası geri döndürülemez biçimde silinir; geçmiş talepler “Silinmiş kullanıcı” adıyla korunur. Kişisel veri dışa aktarma için Kullanıcılar sekmesine bakın.</p>
        {former.length === 0 && <EmptyState icon={<IconCheckCircle size={30} />} title="Anonimleştirilecek hesap yok." />}
        <ul className="list">
          {former.map((u) => (
            <li key={u.id} className="row" style={{ gap: 10 }}>
              <span style={{ flex: 1 }}><b>{u.displayName}</b> <span className="muted">{u.email}</span></span>
              <button className="btn small danger" disabled={busy} onClick={() => { if (window.confirm(`${u.displayName} anonimleştirilsin mi? Bu işlem geri alınamaz.`)) void run(async () => { await api.anonymizeUser(ws, u.id); setNotice('Hesap anonimleştirildi.'); }); }}>Anonimleştir</button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
