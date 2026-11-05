import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import type { DemoSeed, DemoStatus } from '../../types';
import { errorText } from './common';

/**
 * Deneme/tanıtım verisi: gerçek veriyi yüklemeden önce sistemi denemek için. Tek tuşla yüklenir ve tek tuşla silinir; gerçek veriye dokunmaz.
 * Geçici parolalar yalnızca yükleme anında bir kez gösterilir.
 */
export function DemoData({ ws }: { ws: string }) {
  const [status, setStatus] = useState<DemoStatus | null>(null);
  const [seed, setSeed] = useState<DemoSeed | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => { api.demoStatus(ws).then(setStatus).catch((e) => setError(errorText(e, 'Durum okunamadı.'))); }, [ws]);
  useEffect(load, [load]);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await fn(); } catch (e) { setError(errorText(e)); } finally { setBusy(false); load(); }
  };
  const add = () => run(async () => { setSeed(await api.seedDemo(ws)); });
  const remove = () => {
    if (!window.confirm('Tüm demo verisi (demo kullanıcılar, uygulamalar, talepler ve ekler) silinecek. Gerçek verilerinize dokunulmaz. Devam edilsin mi?')) return Promise.resolve();
    return run(async () => { await api.removeDemo(ws); setSeed(null); });
  };

  return (
    <div className="card" style={{ maxWidth: 860 }}>
      <h3>Demo veri</h3>
      <p className="muted">
        Sistemi denemek ve ekibe göstermek için örnek kullanıcılar, hazır şablonlardan uygulamalar, her durumda talepler ve fiş/fatura örnekleri yükler.
        Her şey uydurmadır ve “demo” olarak işaretlidir. Gerçek verilerinizi girmeye başlamadan önce silin; silmek gerçek verilere dokunmaz.
      </p>
      {error && <div className="notice error" role="alert">{error}</div>}
      {!status && !error && <p className="muted">Yükleniyor…</p>}
      {status && (
        <>
          <p>
            {status.present
              ? <>Yüklü: <b>{status.users}</b> kullanıcı · <b>{status.apps}</b> uygulama · <b>{status.requests}</b> talep · <b>{status.attachments}</b> ek dosya</>
              : <>Çalışma alanında demo verisi yok.</>}
          </p>
          <div className="row" style={{ gap: 8 }}>
            <button className="btn primary" disabled={busy || status.present} onClick={() => void add()}>Demo verisini yükle</button>
            <button className="btn danger" disabled={busy || !status.present} onClick={() => void remove()}>Demo verisini sil</button>
          </div>
        </>
      )}

      {seed && (
        <div style={{ marginTop: 16 }}>
          <div className="notice warn" role="status">
            Aşağıdaki geçici parolalar <b>yalnızca şimdi</b> gösterilir. Demo hesaplarla giriş yapmak için not edin; kullanıcılar ilk girişte parolasını değiştirmek zorundadır.
          </div>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Ad</th><th>Görev</th><th>E-posta</th><th>Geçici parola</th></tr></thead>
              <tbody>
                {seed.users.map((u) => (
                  <tr key={u.id}><td>{u.displayName}</td><td>{u.title}</td><td className="mono-small">{u.email}</td><td className="mono-small">{u.temporaryPassword}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
