import { useCallback, useEffect, useState } from 'react';
import { api, getToken, setSessionToken, setToken } from './api';
import { Auth } from './pages/Auth';
import { Projects } from './pages/Projects';
import { Workspace } from './pages/Workspace';
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

  if (booting) return <div className="page"><p className="muted">Yükleniyor…</p></div>;
  if (!session) return <Auth onAuth={onAuth} />;
  if (current.projectId) {
    return <Workspace key={current.projectId} projectId={current.projectId} dark={theme === 'dark'} onTheme={toggleTheme}
      onBack={() => { window.location.hash = '#/'; }} />;
  }
  return <Projects user={session.user} workspaces={session.workspaces} theme={theme} onTheme={toggleTheme}
    onOpen={(id) => { window.location.hash = `#/p/${id}`; }} onLogout={logout} />;
}
