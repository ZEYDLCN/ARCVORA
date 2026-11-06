// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ myNotifications: vi.fn(), setMyNotifications: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { NotificationPrefs } = await import('./NotificationPrefs');
const { ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };
const box = () => host.querySelector('#np-email') as HTMLInputElement;

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('Bildirim tercihi', () => {
  it('mevcut tercihi gösterir ve değişikliği kaydeder', async () => {
    api.myNotifications.mockResolvedValue({ emailEnabled: true });
    api.setMyNotifications.mockResolvedValue({ emailEnabled: false });
    await act(async () => { root.render(<NotificationPrefs />); }); await flush();
    expect(box().checked).toBe(true);
    await act(async () => { box().click(); }); await flush();
    expect(api.setMyNotifications).toHaveBeenCalledWith(false);
    expect(box().checked).toBe(false);
  });

  it('kaydedilemezse eski değere döner ve hatayı gösterir', async () => {
    api.myNotifications.mockResolvedValue({ emailEnabled: true });
    api.setMyNotifications.mockRejectedValue(new ApiError(500, 'internal_error', 'Beklenmeyen bir hata oluştu.'));
    await act(async () => { root.render(<NotificationPrefs />); }); await flush();
    await act(async () => { box().click(); }); await flush();
    expect(box().checked).toBe(true);
    expect(host.querySelector('[role=alert]')!.textContent).toContain('Beklenmeyen');
  });

  it('yüklenene kadar kutu kapalıdır', async () => {
    api.myNotifications.mockReturnValue(new Promise(() => undefined));
    await act(async () => { root.render(<NotificationPrefs />); });
    expect(box().disabled).toBe(true);
  });
});
