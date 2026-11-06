import { useCallback, useEffect, useRef, useState } from 'react';
import { api, getToken, setSessionToken, setToken } from './api';
import { AppsShell, go } from './components/AppsShell';
import { Intro } from './components/Intro';
import { safeSessionStorage, shouldShowIntro } from './lib/intro';
import { canAccess, defaultRoute, parseRoute } from './lib/session';
import { clearSsoHash, parseSsoHash, ssoErrorText } from './lib/sso';
import { Accounting } from './pages/apps/Accounting';
import { Admin } from './pages/apps/Admin';
import { AppForm } from './pages/apps/AppForm';
import { DesignList } from './pages/apps/DesignList';
import { Designer } from './pages/apps/Designer';
import { Inbox } from './pages/apps/Inbox';
import { MyApps } from './pages/apps/MyApps';
import { MyRequests } from './pages/apps/MyRequests';
import { RequestDetail } from './pages/apps/RequestDetail';
import { Delegations } from './pages/apps/Delegations';
import { Verify } from './pages/apps/Verify';
import { Auth } from './pages/Auth';
import { ChangePassword } from './pages/ChangePassword';
import { Projects } from './pages/Projects';
import { Workspace } from './pages/Workspace';
import { ThemeContext } from './theme';
import type { AuthResult } from './types';

type Theme = 'dark' | 'light';
type Session = { user: AuthResult['user']; workspaces: AuthResult['workspaces']; mustChangePassword: boolean };

function readTheme(): Theme {
  try {
    const saved = localStorage.getItem('arcvora.theme');
    if (saved === 'dark' || saved === 'light') return saved;
  } catch { /* yoksay */ }
  return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [booting, setBooting] = useState(Boolean(getToken()));
  const [theme, setTheme] = useState<Theme>(readTheme);
  const [hash, setHash] = useState(window.location.hash);
  const [ssoError, setSsoError] = useState<string | null>(null);
  const ssoHandled = useRef<string | null>(null);
  // Açılış animasyonu: oturum başına bir kez (SPA içi gezinmede veya yenilemede tekrarlanmaz); ?intro ile zorlanır
  const [intro, setIntro] = useState(() => shouldShowIntro(window.location.search, safeSessionStorage()));
  // Animasyon gösterilmeyecekse index.html'in koyu önyükleme zemini kalmasın
  useEffect(() => { if (!intro) document.documentElement.classList.remove('intro-boot'); }, [intro]);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try { localStorage.setItem('arcvora.theme', theme); } catch { /* yoksay */ }
  }, [theme]);

  useEffect(() => {
    const onHash = () => setHash(window.location.hash);
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

  // SSO dönüşü (#/sso?ticket=… veya ?error=…): bilet POST ile oturuma çevrilir; adres çubuğundan hemen temizlenir.
  useEffect(() => {
    const sso = parseSsoHash(hash);
    if (!sso) return;
    // Bilet tek kullanımlıktır: StrictMode'un çift çalıştırması veya yeniden çizim ikinci kez değiştirmemeli.
    if (ssoHandled.current === hash) return;
    ssoHandled.current = hash;
    clearSsoHash();
    setHash('#/');
    if (sso.ticket) {
      setBooting(true);
      api.ssoTicket(sso.ticket).then((r) => { setSsoError(null); setToken(r.token); setSessionToken(r.token); setSession({ user: r.user, workspaces: r.workspaces, mustChangePassword: false }); })
        .catch((e) => setSsoError(e instanceof Error ? e.message : ssoErrorText(undefined))).finally(() => setBooting(false));
    } else setSsoError(ssoErrorText(sso.error));
  }, [hash]);

  useEffect(() => {
    const t = getToken();
    if (!t) return;
    setSessionToken(t);
    api.me().then((r) => setSession({ user: r.user, workspaces: r.workspaces, mustChangePassword: r.mustChangePassword === true })).catch(() => logout()).finally(() => setBooting(false));
  }, [logout]);

  const onAuth = (r: AuthResult) => {
    setToken(r.token); setSessionToken(r.token);
    setSession({ user: r.user, workspaces: r.workspaces, mustChangePassword: r.mustChangePassword === true });
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
  if (!session) return wrap(<Auth onAuth={onAuth} initialError={ssoError ?? undefined} />);
  if (session.mustChangePassword)
    return wrap(<ChangePassword forced onLogout={logout} onDone={() => setSession({ ...session, mustChangePassword: false })} />);

  const ws = session.workspaces[0];
  if (!ws) return wrap(<div className="auth"><div className="auth-form"><h2>Çalışma alanı yok</h2><p className="muted">Hesabınız henüz bir çalışma alanına eklenmemiş. Master admin ile görüşün.</p><button className="btn" onClick={logout}>Çıkış yap</button></div></div>);

  // Ana sayfa role göre: app kullanıcısı uygulamalarına, mimari ekibi projelerine gider.
  const parsed = parseRoute(hash);
  const route = parsed.name === 'home' ? defaultRoute(ws) : parsed;
  const common = { user: session.user, workspace: ws, dark, onTheme: toggleTheme, onLogout: logout };

  if (route.name === 'project' && route.id && canAccess(route, ws)) {
    return wrap(<Workspace key={route.id} projectId={route.id} user={session.user} dark={dark} onTheme={toggleTheme}
      onLogout={logout} onBack={() => { go({ name: 'projects' }); }} />);
  }
  if (route.name === 'projects' && canAccess(route, ws)) {
    return wrap(<Projects {...common} workspaces={session.workspaces} onOpen={(id) => { go({ name: 'project', id }); }} />);
  }

  const page = (() => {
    switch (route.name) {
      case 'apps': return <MyApps />;
      case 'app': return <AppForm key={route.id} id={route.id!} {...common} />;
      case 'requests': return <MyRequests workspace={ws} />;
      case 'request': return <RequestDetail key={route.id} id={route.id!} isAdmin={ws.role === 'admin'} />;
      case 'inbox': return <Inbox />;
      case 'accounting': return <Accounting {...common} />;
      case 'verify': return <Verify {...common} />;
      case 'delegations': return <Delegations {...common} />;
      case 'design': return <DesignList {...common} />;
      case 'designer': return <Designer key={route.id} id={route.id!} {...common} />;
      case 'admin': return <Admin sub={route.sub ?? 'users'} {...common} />;
      case 'account': return <ChangePassword onDone={() => { /* parola değişti; oturum açık kalır */ }} />;
      default: return null;
    }
  })();

  return wrap(<AppsShell route={route} {...common}>{page}</AppsShell>);
}
