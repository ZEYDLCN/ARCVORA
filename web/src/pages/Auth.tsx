import { useState } from 'react';
import { api, ApiError } from '../api';
import { Hero } from '../components/Hero';
import { Logo } from '../components/Logo';
import type { AuthResult } from '../types';

export function Auth({ onAuth }: { onAuth: (r: AuthResult) => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

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
            {mode === 'register' && (<><label htmlFor="name">Ad</label><input id="name" type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="name" /></>)}
            <label htmlFor="email">E-posta</label>
            <input id="email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
            <label htmlFor="password">Parola {mode === 'register' && <span className="muted">(en az 10 karakter)</span>}</label>
            <input id="password" type="password" required minLength={mode === 'register' ? 10 : undefined} value={password}
              onChange={(e) => setPassword(e.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} />
            {error && <div className="notice error" role="alert" style={{ marginTop: 12 }}>{error}</div>}
            <p style={{ marginTop: 18 }}><button className="btn primary" disabled={busy} style={{ width: '100%', padding: '12px 16px' }}>{mode === 'login' ? 'Giriş yap' : 'Hesap oluştur'}</button></p>
          </form>
        </div>
      </main>
    </>
  );
}
