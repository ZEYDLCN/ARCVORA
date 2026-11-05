// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ demoStatus: vi.fn(), seedDemo: vi.fn(), removeDemo: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { DemoData } = await import('./DemoData');
const { ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };
const render = async () => { await act(async () => { root.render(<DemoData ws="w1" />); }); await flush(); };
const button = (t: string) => [...host.querySelectorAll('button')].find((b) => b.textContent === t) as HTMLButtonElement;
const click = async (t: string) => { await act(async () => { button(t).click(); }); await flush(); };
const none = { present: false, users: 0, groups: 0, apps: 0, requests: 0, attachments: 0 };
const some = { present: true, users: 6, groups: 1, apps: 3, requests: 9, attachments: 4 };

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); vi.restoreAllMocks(); });

describe('Demo veri sekmesi', () => {
  it('demo yokken yalnızca yükleme açıktır', async () => {
    api.demoStatus.mockResolvedValue(none);
    await render();
    expect(host.textContent).toContain('demo verisi yok');
    expect(button('Demo verisini yükle').disabled).toBe(false);
    expect(button('Demo verisini sil').disabled).toBe(true);
  });

  it('yükleyince geçici parolaları bir kez gösterir ve durumu yeniler', async () => {
    api.demoStatus.mockResolvedValueOnce(none).mockResolvedValue(some);
    api.seedDemo.mockResolvedValue({ users: [{ id: 'u1', email: 'demo.can@demo.arcvora.example', displayName: 'Can Yıldız (demo)', title: 'Satış Temsilcisi', temporaryPassword: 'AbCd-2345-xYz7' }], groups: 1, apps: 3, requests: 9, attachments: 4 });
    await render();
    await click('Demo verisini yükle');
    expect(host.textContent).toContain('AbCd-2345-xYz7');
    expect(host.textContent).toContain('yalnızca şimdi');
    expect(host.textContent).toContain('9');                       // durum yenilendi
    expect(button('Demo verisini yükle').disabled).toBe(true);
    expect(button('Demo verisini sil').disabled).toBe(false);
  });

  it('silmeden önce onay ister; iptalde hiçbir şey silinmez', async () => {
    api.demoStatus.mockResolvedValue(some);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    await render();
    await click('Demo verisini sil');
    expect(confirm).toHaveBeenCalled();
    expect(api.removeDemo).not.toHaveBeenCalled();
  });

  it('onaylayınca siler, parola tablosunu kaldırır ve durumu yeniler', async () => {
    api.demoStatus.mockResolvedValueOnce(some).mockResolvedValue(none);
    api.removeDemo.mockResolvedValue(undefined);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render();
    await click('Demo verisini sil');
    expect(api.removeDemo).toHaveBeenCalledWith('w1');
    expect(host.textContent).toContain('demo verisi yok');
  });

  it('sunucu hatasını (ör. gerçek talep bağımlılığı) gösterir', async () => {
    api.demoStatus.mockResolvedValue(some);
    api.removeDemo.mockRejectedValue(new ApiError(409, 'demo_in_use', 'Demo kullanıcılarla açılmış 1 gerçek talep var.'));
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render();
    await click('Demo verisini sil');
    expect(host.querySelector('[role=alert]')!.textContent).toContain('gerçek talep');
  });
});
