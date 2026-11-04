import { useState } from 'react';
import { api, ApiError } from '../api';
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
    <main className="center-page">
      <div className="brand"><span className="mark">▲</span> ARCVORA</div>
      <h1>Fikrini, ekibinin geliştirebileceği bir mimariye dönüştür.</h1>
      <p className="tagline">Turn intent into architecture.</p>
      <form className="card" onSubmit={submit}>
        <div role="tablist" className="tabs" style={{ marginBottom: 8 }}>
          <button type="button" role="tab" className="tab" aria-selected={mode === 'login'} onClick={() => setMode('login')}>Giriş</button>
          <button type="button" role="tab" className="tab" aria-selected={mode === 'register'} onClick={() => setMode('register')}>Kayıt ol</button>
        </div>
        {mode === 'register' && (<><label htmlFor="name">Ad</label><input id="name" type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="name" /></>)}
        <label htmlFor="email">E-posta</label>
        <input id="email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
        <label htmlFor="password">Parola {mode === 'register' && <span className="muted">(en az 10 karakter)</span>}</label>
        <input id="password" type="password" required minLength={mode === 'register' ? 10 : undefined} value={password}
          onChange={(e) => setPassword(e.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} />
        {error && <div className="notice error" role="alert" style={{ marginTop: 10 }}>{error}</div>}
        <p><button className="btn primary" disabled={busy}>{mode === 'login' ? 'Giriş yap' : 'Hesap oluştur'}</button></p>
      </form>
    </main>
  );
}
