import { useEffect, useState } from 'react';
import { IconKey, IconMail, IconShield, IconUser } from '../components/icons';
import { api, ApiError } from '../api';
import { Hero } from '../components/Hero';
import { Logo } from '../components/Logo';
import type { AuthResult } from '../types';

export function Auth({ onAuth, initialError }: { onAuth: (r: AuthResult) => void; initialError?: string }) {
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(initialError ?? null);
  const [busy, setBusy] = useState(false);
  const [sso, setSso] = useState<{ label: string } | null>(null);
  useEffect(() => { if (initialError) setError(initialError); }, [initialError]);
  useEffect(() => { api.providers().then((p) => setSso(p.oidc ? { label: p.oidcLabel ?? 'Şirket hesabıyla giriş' } : null)).catch(() => setSso(null)); }, []);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onAuth(mode === 'login' ? await api.login(email, password) : await api.register(email, password, name));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Bağlantı kurulamadı.');
    } finally { setBusy(false); }
  };

  return (
    <>
      <div className="bg" aria-hidden="true" />
      <main className="auth">
        <div className="auth-grid">
          <section className="auth-hero">
            <Logo size={40} />
            <h1>Fikrini, ekibinin geliştirebileceği bir mimariye dönüştür.</h1>
            <p className="tagline">Gereksinimlerini anlat. Eksik kararları keşfet. Mimari seçeneklerini değerlendir. Yeni bir özellik istediğinde, hangi parçaların değişeceğini aynı çalışma alanında gör.</p>
            <Hero />
          </section>

          <form className="auth-form" onSubmit={submit}>
            <h2 style={{ fontSize: 24 }}>{mode === 'login' ? 'Tekrar hoş geldin' : 'Hesap oluştur'}</h2>
            <p className="muted">Turn intent into architecture.</p>
            <div className="seg" role="tablist" style={{ marginTop: 8 }}>
              <button type="button" role="tab" aria-selected={mode === 'login'} onClick={() => setMode('login')}>Giriş</button>
              <button type="button" role="tab" aria-selected={mode === 'register'} onClick={() => setMode('register')}>Kayıt ol</button>
            </div>
            {mode === 'register' && (<><label htmlFor="name">Ad</label><div className="with-ico"><span className="wi" aria-hidden="true"><IconUser size={18} /></span><input id="name" type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="name" /></div></>)}
            <label htmlFor="email">E-posta</label>
            <div className="with-ico"><span className="wi" aria-hidden="true"><IconMail size={18} /></span><input id="email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" /></div>
            <label htmlFor="password">Parola {mode === 'register' && <span className="muted">(en az 10 karakter)</span>}</label>
            <div className="with-ico"><span className="wi" aria-hidden="true"><IconKey size={18} /></span>
              <input id="password" type="password" required minLength={mode === 'register' ? 10 : undefined} value={password}
                onChange={(e) => setPassword(e.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} /></div>
            {error && <div className="notice error" role="alert" style={{ marginTop: 12 }}>{error}</div>}
            <p style={{ marginTop: 18 }}><button className="btn primary" disabled={busy} style={{ width: '100%', padding: '12px 16px' }}>{mode === 'login' ? 'Giriş yap' : 'Hesap oluştur'}</button></p>
            {sso && mode === 'login' && (
              <p style={{ marginTop: 10 }}><a className="btn" href="/api/auth/oidc/login" style={{ justifyContent: 'center', padding: '12px 16px', width: '100%' }}><IconShield size={17} />{sso.label}</a></p>
            )}
          </form>
        </div>
      </main>
    </>
  );
}
