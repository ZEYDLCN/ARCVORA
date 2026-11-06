// @vitest-environment jsdom
import { StrictMode } from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
  providers: vi.fn(), ssoTicket: vi.fn(), me: vi.fn(),
}));
vi.mock('./api', async (orig) => {
  const real = await orig<typeof import('./api')>();
  // Giriş sonrası sayfalar bu testin konusu değil: tanımsız her çağrı sonsuza dek bekleyen bir söz döndürür.
  const loose = new Proxy(api as Record<string, unknown>, { get: (t, k: string) => (k in t ? t[k] : () => new Promise(() => {})) });
  return { ...real, api: loose };
});
const { App } = await import('./App');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const TICKET = 'a'.repeat(32);
const result = { token: 'jwt-1', user: { id: 'u1', email: 'a@b.c', name: 'Ali' }, workspaces: [] };
let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 8; i++) await act(async () => { await Promise.resolve(); }); };

beforeEach(() => {
  vi.clearAllMocks();
  try { localStorage.clear(); sessionStorage.clear(); } catch { /* yoksay */ }
  history.replaceState(null, '', '/?intro=0#/');
  api.providers.mockResolvedValue({ oidc: true });
  host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('Uygulama: SSO dönüşü', () => {
  it('bileti oturuma çevirir ve adres çubuğundan temizler', async () => {
    api.ssoTicket.mockResolvedValue(result);
    window.location.hash = `#/sso?ticket=${TICKET}`;
    await act(async () => { root.render(<App />); }); await flush();
    expect(api.ssoTicket).toHaveBeenCalledWith(TICKET);
    expect(window.location.hash).toBe('#/');
    expect(window.location.href).not.toContain(TICKET);
    expect(host.textContent).toContain('Çalışma alanı yok'); // oturum açıldı (çalışma alanı olmayan hesap)
  });

  it('StrictMode çift çalıştırmasında bileti yalnızca bir kez kullanır', async () => {
    api.ssoTicket.mockResolvedValue(result);
    window.location.hash = `#/sso?ticket=${TICKET}`;
    await act(async () => { root.render(<StrictMode><App /></StrictMode>); }); await flush();
    expect(api.ssoTicket).toHaveBeenCalledTimes(1);
    expect(host.querySelector('[role=alert]')).toBeNull();
  });

  it('bilet reddedilirse giriş sayfasında hata gösterir', async () => {
    api.ssoTicket.mockRejectedValue(new Error('Bilet geçersiz veya süresi doldu.'));
    window.location.hash = `#/sso?ticket=${TICKET}`;
    await act(async () => { root.render(<App />); }); await flush();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('Bilet geçersiz');
    expect(host.querySelector('input[type=password]')).not.toBeNull();
  });

  it('sağlayıcı hata kodunu Türkçe metne çevirir', async () => {
    window.location.hash = '#/sso?error=sso_not_allowed';
    await act(async () => { root.render(<App />); }); await flush();
    expect(api.ssoTicket).not.toHaveBeenCalled();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('izniniz yok');
    expect(window.location.hash).toBe('#/');
  });

  it('biçimi bozuk bilet sunucuya gönderilmez, genel hata gösterilir', async () => {
    window.location.hash = '#/sso?ticket=<script>';
    await act(async () => { root.render(<App />); }); await flush();
    expect(api.ssoTicket).not.toHaveBeenCalled();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('tamamlanamadı');
  });
});
