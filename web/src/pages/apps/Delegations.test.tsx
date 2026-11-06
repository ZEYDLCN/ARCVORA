// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Delegation } from '../../types';

const api = vi.hoisted(() => ({ delegations: vi.fn(), directory: vi.fn(), createDelegation: vi.fn(), revokeDelegation: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { Delegations } = await import('./Delegations');
const { ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const me = { id: 'me', displayName: 'Ben' };
const ws = (role = 'app_user') => ({ id: 'w1', name: 'W', role, canAccept: false });
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };
const render = async (role = 'app_user') => { await act(async () => { root.render(<Delegations user={me} workspace={ws(role)} />); }); await flush(); };
const setSel = (id: string, v: string) => act(() => {
  const el = host.querySelector(`#${id}`) as HTMLSelectElement;
  Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event('change', { bubbles: true }));
});
const button = (t: string) => [...host.querySelectorAll('button')].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
const d = (over: Partial<Delegation> = {}): Delegation => ({ id: 'd1', fromUserId: 'me', fromName: 'Ben', toUserId: 'u2', toName: 'Veli', startsOn: '2026-11-10', endsOn: '2026-11-14', status: 'active', createdAt: '2026-11-05T10:00:00Z', ...over });

beforeEach(() => {
  vi.clearAllMocks();
  api.delegations.mockResolvedValue([]);
  api.directory.mockResolvedValue([{ id: 'me', displayName: 'Ben' }, { id: 'u2', displayName: 'Veli' }, { id: 'u3', displayName: 'Selin' }]);
  host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); vi.restoreAllMocks(); });

describe('Vekâletlerim', () => {
  it('vekil listesinde kendinizi göstermez ve kişi seçilene kadar düğme kapalıdır', async () => {
    await render();
    const opts = [...(host.querySelector('#dg-to') as HTMLSelectElement).options].map((o) => o.textContent);
    expect(opts).toEqual(['Seçin…', 'Veli', 'Selin']);
    expect(button('Vekâlet ver')!.disabled).toBe(true);
    setSel('dg-to', 'u2');
    expect(button('Vekâlet ver')!.disabled).toBe(false);
  });

  it('formu gönderir ve listeyi yeniler', async () => {
    api.createDelegation.mockResolvedValue(d());
    await render();
    setSel('dg-to', 'u2');
    await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    await flush();
    expect(api.createDelegation).toHaveBeenCalledWith('w1', expect.objectContaining({ toUserId: 'u2', startsOn: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), endsOn: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) }));
    expect(api.createDelegation.mock.calls[0][1]).not.toHaveProperty('fromUserId');
    expect(api.delegations.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it('sunucu hatasını (çakışma, geçersiz dönem) gösterir', async () => {
    api.createDelegation.mockRejectedValue(new ApiError(409, 'delegation_overlap', 'Bu kişinin aynı günlerde başka bir vekâleti var.'));
    await render();
    setSel('dg-to', 'u2');
    await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    await flush();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('başka bir vekâleti var');
  });

  it('vekâletleri durum etiketiyle listeler; vereni iptal, alan geri verir', async () => {
    api.delegations.mockResolvedValue([d(), d({ id: 'd2', fromUserId: 'u3', fromName: 'Selin', toUserId: 'me', toName: 'Ben' }), d({ id: 'd3', status: 'expired' })]);
    await render();
    expect(host.textContent).toContain('10.11.2026 – 14.11.2026');
    expect(host.textContent).toContain('Yürürlükte');
    expect(host.textContent).toContain('Süresi doldu');
    expect(button('İptal et')).toBeTruthy();
    expect(button('Geri ver')).toBeTruthy();
    expect([...host.querySelectorAll('button')].filter((b) => b.textContent === 'İptal et' || b.textContent === 'Geri ver')).toHaveLength(2);   // süresi dolanda düğme yok
  });

  it('iptal onay ister', async () => {
    api.delegations.mockResolvedValue([d()]);
    api.revokeDelegation.mockResolvedValue(d({ status: 'revoked' }));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    await render();
    await act(async () => { button('İptal et')!.click(); }); await flush();
    expect(api.revokeDelegation).not.toHaveBeenCalled();
    await act(async () => { button('İptal et')!.click(); }); await flush();
    expect(api.revokeDelegation).toHaveBeenCalledWith('w1', 'd1');
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it('yalnızca master admin başkası adına verebilir ve tümünü görebilir', async () => {
    await render('app_user');
    expect(host.querySelector('#dg-from')).toBeNull();
    expect(button('Tümünü göster')).toBeUndefined();
    act(() => root.unmount());
    root = createRoot(host);
    await render('admin');
    expect(host.querySelector('#dg-from')).not.toBeNull();
    setSel('dg-from', 'u3');
    setSel('dg-to', 'u2');
    api.createDelegation.mockResolvedValue(d());
    await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    await flush();
    expect(api.createDelegation.mock.calls[0][1]).toMatchObject({ fromUserId: 'u3', toUserId: 'u2' });
    await act(async () => { button('Tümünü göster')!.click(); }); await flush();
    expect(api.delegations).toHaveBeenLastCalledWith('w1', 'all');
  });
});
