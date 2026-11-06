import { useState } from 'react';
import { api } from '../api';
import { Logo } from '../components/Logo';
import { errorText } from './apps/common';
import { NotificationPrefs } from './apps/NotificationPrefs';

const MIN = 10;

interface Props { forced?: boolean; onDone: () => void; onLogout?: () => void }

/**
 * Parola değiştirme. İlk girişte (yönetici geçici parola verdiyse) zorunlu ekran olarak, aksi hâlde hesap sayfası olarak gösterilir.
 * Zorunlu olması arayüz düzeyindedir; geçici parolayı yönetici bildiği için asıl amaç kullanıcının kendi parolasını belirlemesidir.
 */
export function ChangePassword({ forced, onDone, onLogout }: Props) {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [again, setAgain] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);
  const [busy, setBusy] = useState(false);

  const problem = next.length > 0 && next.length < MIN ? `Yeni parola en az ${MIN} karakter olmalı.` : again.length > 0 && again !== next ? 'Parolalar eşleşmiyor.' : null;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (problem) return;
    setBusy(true);
    setError(null);
    try { await api.changePassword(current, next); setOk(true); setCurrent(''); setNext(''); setAgain(''); onDone(); }
    catch (err) { setError(errorText(err)); } finally { setBusy(false); }
  };

  const form = (
    <form className={forced ? 'auth-form' : 'card'} style={forced ? undefined : { maxWidth: 480 }} onSubmit={submit}>
      <h2 style={{ fontSize: forced ? 24 : 17 }}>{forced ? 'Parolanızı belirleyin' : 'Parolamı değiştir'}</h2>
      {forced && <p className="muted">Hesabınız geçici bir parolayla açıldı. Devam etmeden önce kendi parolanızı belirleyin.</p>}
      {error && <div className="notice error" role="alert">{error}</div>}
      {ok && !forced && <div className="notice" role="status">Parolanız değiştirildi.</div>}
      <label htmlFor="pw-cur">{forced ? 'Geçici parola' : 'Mevcut parola'}</label>
      <input id="pw-cur" type="password" required autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />
      <label htmlFor="pw-new">Yeni parola <span className="muted">(en az {MIN} karakter)</span></label>
      <input id="pw-new" type="password" required minLength={MIN} autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
      <label htmlFor="pw-again">Yeni parola (tekrar)</label>
      <input id="pw-again" type="password" required autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} aria-invalid={again.length > 0 && again !== next || undefined} />
      {problem && <div className="err" style={{ color: 'var(--red)', marginTop: 6 }} role="alert">{problem}</div>}
      <p className="row" style={{ marginTop: 16 }}>
        <button className="btn primary" disabled={busy || !!problem || !current || !next || !again}>Parolayı kaydet</button>
        {forced && onLogout && <button type="button" className="btn" onClick={onLogout}>Çıkış yap</button>}
      </p>
    </form>
  );

  if (!forced) return <><div className="page-head"><h1>Hesap</h1><p className="tagline">Giriş parolanızı ve bildirim tercihlerinizi yönetin.</p></div><div className="page-body">{form}<NotificationPrefs /></div></>;
  return (
    <>
      <div className="bg" aria-hidden="true" />
      <main className="auth"><div style={{ display: 'grid', gap: 18, placeItems: 'center', width: '100%' }}><Logo size={40} />{form}</div></main>
    </>
  );
}
