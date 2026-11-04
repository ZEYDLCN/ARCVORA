import { useCallback, useEffect, useState } from 'react';
import { api, getToken, setSessionToken, setToken } from './api';
import { Intro } from './components/Intro';
import { safeSessionStorage, shouldShowIntro } from './lib/intro';
import { Auth } from './pages/Auth';
import { Projects } from './pages/Projects';
import { Workspace } from './pages/Workspace';
import { ThemeContext } from './theme';
import type { AuthResult } from './types';

type Theme = 'dark' | 'light';

function readTheme(): Theme {
  try {
    const saved = localStorage.getItem('arcvora.theme');
    if (saved === 'dark' || saved === 'light') return saved;
  } catch { /* yoksay */ }
  return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

function route(): { projectId: string | null } {
  const m = /^#\/p\/([0-9a-f-]{36})/i.exec(window.location.hash);
  return { projectId: m ? m[1] : null };
}

export function App() {
  const [session, setSession] = useState<{ user: AuthResult['user']; workspaces: AuthResult['workspaces'] } | null>(null);
  const [booting, setBooting] = useState(Boolean(getToken()));
  const [theme, setTheme] = useState<Theme>(readTheme);
  const [current, setCurrent] = useState(route);
  // Açılış animasyonu: oturum başına bir kez (SPA içi gezinmede veya yenilemede tekrarlanmaz); ?intro ile zorlanır
  const [intro, setIntro] = useState(() => shouldShowIntro(window.location.search, safeSessionStorage()));
  // Animasyon gösterilmeyecekse index.html'in koyu önyükleme zemini kalmasın
  useEffect(() => { if (!intro) document.documentElement.classList.remove('intro-boot'); }, [intro]);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try { localStorage.setItem('arcvora.theme', theme); } catch { /* yoksay */ }
  }, [theme]);

  useEffect(() => {
    const onHash = () => setCurrent(route());
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  const logout = useCallback(() => {
    setToken(null); setSessionToken(null); setSession(null); window.location.hash = '#/';
  }, []);

  useEffect(() => {
    window.addEventListener('arcvora:unauthorized', logout);
    return () => window.removeEventListener('arcvora:unauthorized', logout);
  }, [logout]);

  useEffect(() => {
    const t = getToken();
    if (!t) return;
    setSessionToken(t);
    api.me().then((r) => setSession(r)).catch(() => logout()).finally(() => setBooting(false));
  }, [logout]);

  const onAuth = (r: AuthResult) => {
    setToken(r.token); setSessionToken(r.token);
    setSession({ user: r.user, workspaces: r.workspaces });
  };
  const toggleTheme = () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'));

  const dark = theme === 'dark';
  const wrap = (node: React.ReactNode) => (
    <ThemeContext.Provider value={{ dark }}>
      {node}
      {intro && <Intro onDone={() => setIntro(false)} />}
    </ThemeContext.Provider>
  );

  if (booting) return wrap(<><div className="bg" aria-hidden="true" /><div className="auth"><div className="auth-form"><p className="muted">Yükleniyor…</p></div></div></>);
  if (!session) return wrap(<Auth onAuth={onAuth} />);
  if (current.projectId) {
    return wrap(<Workspace key={current.projectId} projectId={current.projectId} user={session.user} dark={dark} onTheme={toggleTheme}
      onLogout={logout} onBack={() => { window.location.hash = '#/'; }} />);
  }
  return wrap(<Projects user={session.user} workspaces={session.workspaces} dark={dark} onTheme={toggleTheme}
    onOpen={(id) => { window.location.hash = `#/p/${id}`; }} onLogout={logout} />);
}
