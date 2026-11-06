// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ providers: vi.fn(), login: vi.fn(), register: vi.fn() }));
vi.mock('../api', async (orig) => ({ ...(await orig<typeof import('../api')>()), api }));
const { Auth } = await import('./Auth');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };
const ssoLink = () => host.querySelector('a[href="/api/auth/oidc/login"]') as HTMLAnchorElement | null;

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('Giriş sayfası: tek oturum açma', () => {
  it('SSO kapalıysa düğme yoktur', async () => {
    api.providers.mockResolvedValue({ oidc: false });
    await act(async () => { root.render(<Auth onAuth={() => {}} />); }); await flush();
    expect(ssoLink()).toBeNull();
  });

  it('SSO açıksa sağlayıcı etiketiyle düğme gösterir', async () => {
    api.providers.mockResolvedValue({ oidc: true, oidcLabel: 'Microsoft ile giriş' });
    await act(async () => { root.render(<Auth onAuth={() => {}} />); }); await flush();
    expect(ssoLink()!.textContent).toBe('Microsoft ile giriş');
  });

  it('etiket yoksa varsayılan metni kullanır', async () => {
    api.providers.mockResolvedValue({ oidc: true });
    await act(async () => { root.render(<Auth onAuth={() => {}} />); }); await flush();
    expect(ssoLink()!.textContent).toBe('Şirket hesabıyla giriş');
  });

  it('sağlayıcı sorgusu başarısız olursa sayfa yine çalışır (düğme yok, hata yok)', async () => {
    api.providers.mockRejectedValue(new Error('ağ'));
    await act(async () => { root.render(<Auth onAuth={() => {}} />); }); await flush();
    expect(ssoLink()).toBeNull();
    expect(host.querySelector('[role=alert]')).toBeNull();
    expect(host.querySelector('input[type=password]')).not.toBeNull();
  });

  it('SSO dönüş hatasını başlangıçta gösterir', async () => {
    api.providers.mockResolvedValue({ oidc: true });
    await act(async () => { root.render(<Auth onAuth={() => {}} initialError="Giriş reddedildi." />); }); await flush();
    expect(host.querySelector('[role=alert]')!.textContent).toBe('Giriş reddedildi.');
  });
});
