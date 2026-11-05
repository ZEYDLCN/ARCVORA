// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ verifyDocument: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { Verify } = await import('./Verify');
const { ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const ctx = { user: { id: 'u', displayName: 'T' }, workspace: { id: 'w1', name: 'W', role: 'app_user', canAccept: false } };
const flush = async () => { for (let i = 0; i < 4; i++) await act(async () => { await Promise.resolve(); }); };
const type = (id: string, v: string) => act(() => {
  const el = host.querySelector(`#${id}`) as HTMLInputElement;
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event('input', { bubbles: true }));
});
const submit = async () => { await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }); await flush(); };

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); act(() => root.render(<Verify {...ctx} />)); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('Belge doğrula', () => {
  it('alanlar boşken düğme kapalıdır', () => {
    expect((host.querySelector('form button') as HTMLButtonElement).disabled).toBe(true);
    type('vf-number', 'IZN-2026-0001');
    expect((host.querySelector('form button') as HTMLButtonElement).disabled).toBe(true);
    type('vf-code', 'ABCD-1234-EF56');
    expect((host.querySelector('form button') as HTMLButtonElement).disabled).toBe(false);
  });

  it('geçerli kodda belgenin güncel durumunu gösterir', async () => {
    api.verifyDocument.mockResolvedValue({ valid: true, number: 'IZN-2026-0001', status: 'approved', statusLabel: 'Onaylandı', appName: 'İzin Talebi' });
    type('vf-number', ' IZN-2026-0001 '); type('vf-code', 'abcd-1234-ef56');
    await submit();
    expect(api.verifyDocument).toHaveBeenCalledWith('w1', 'IZN-2026-0001', 'abcd-1234-ef56');
    expect(host.querySelector('[role=status]')!.textContent).toContain('Belge geçerli');
    expect(host.textContent).toContain('Onaylandı');
  });

  it('geçersiz kodda ayrıntı vermeden uyarır', async () => {
    api.verifyDocument.mockResolvedValue({ valid: false });
    type('vf-number', 'IZN-2026-0001'); type('vf-code', '0000-0000-0000');
    await submit();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('Doğrulanamadı');
    expect(host.querySelector('[role=status]')).toBeNull();
  });

  it('sunucu hatasını gösterir', async () => {
    api.verifyDocument.mockRejectedValue(new ApiError(404, 'not_found', 'Çalışma alanı bulunamadı.'));
    type('vf-number', 'X'); type('vf-code', 'Y');
    await submit();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('bulunamadı');
  });
});
