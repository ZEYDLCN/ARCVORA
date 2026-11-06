import { useEffect, useState } from 'react';
import { api } from '../../api';
import { errorText } from './common';

/** Kullanıcının e-posta bildirimlerini açıp kapattığı küçük tercih kartı (Hesap sayfasında). */
export function NotificationPrefs() {
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { api.myNotifications().then((r) => setEnabled(r.emailEnabled)).catch((e) => setError(errorText(e, 'Tercih okunamadı.'))); }, []);

  const toggle = async (value: boolean) => {
    const previous = enabled;
    setEnabled(value);
    setError(null);
    try { setEnabled((await api.setMyNotifications(value)).emailEnabled); } catch (e) { setEnabled(previous); setError(errorText(e)); }
  };

  return (
    <div className="card" style={{ maxWidth: 480, marginTop: 14 }}>
      <h3>E-posta bildirimleri</h3>
      <p className="muted">Size atanan onaylar, taleplerinizin sonucu ve vekâlet bildirimleri e-postayla gelir.</p>
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="check-row">
        <input id="np-email" type="checkbox" disabled={enabled === null} checked={enabled === true} onChange={(e) => void toggle(e.target.checked)} />
        <label htmlFor="np-email" style={{ margin: 0 }}>E-posta bildirimleri alayım</label>
      </div>
    </div>
  );
}
