// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { RequestDetail as Detail } from '../../types';

const api = vi.hoisted(() => ({ requestDetail: vi.fn(), approveTask: vi.fn(), rejectTask: vi.fn(), cancelRequest: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { RequestDetail } = await import('./RequestDetail');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;

const base = (over: Partial<Detail> = {}): Detail => ({
  summary: { id: 'r1', workspaceId: 'w', number: 'IZN-2026-0001', appId: 'a', appName: 'İzin Talebi', icon: 'calendar', status: 'in_review', summary: '10.11.2026', requesterId: 'u1', requesterName: 'Çalışan', createdAt: '2026-11-05T10:00:00Z', currentStepName: 'Yönetici onayı' },
  definition: {
    schema: 1, prefix: 'IZN', summaryFields: [],
    fields: [{ key: 'kind', label: 'İzin türü', type: 'select', options: ['Yıllık'] }, { key: 'note', label: 'Açıklama', type: 'long_text' }, { key: 'days', label: 'Gün', type: 'number', computed: { op: 'calendar_days', from: 'a', to: 'b' } }],
    steps: [{ id: 's1', name: 'Yönetici onayı', approvers: { type: 'manager' } }, { id: 's2', name: 'Genel müdür', approvers: { type: 'admin' } }],
  },
  versionNumber: 1, data: { kind: 'Yıllık', note: 'merhaba', days: 5 },
  tasks: [{ id: 't1', assigneeId: 'u2', stepIndex: 0, stepId: 's1', stepName: 'Yönetici onayı', status: 'pending', createdAt: '2026-11-05T10:00:00Z' }],
  events: [{ id: 1, at: '2026-11-05T10:00:00Z', actorId: 'u1', kind: 'submitted' }],
  people: { u1: 'Çalışan', u2: 'Yönetici' }, canCancel: false, ...over,
});

const render = async (id = 'r1') => { await act(async () => { root.render(<RequestDetail id={id} />); await Promise.resolve(); await Promise.resolve(); }); };
const button = (text: string) => [...host.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
const typeComment = (v: string) => act(() => {
  const el = host.querySelector('#decision-comment') as HTMLTextAreaElement;
  Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event('input', { bubbles: true }));
});

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('RequestDetail', () => {
  it('numarayı, durumu, alanları ve zaman çizelgesini gösterir; hesaplanan alan da görünür', async () => {
    api.requestDetail.mockResolvedValue(base());
    await render();
    expect(host.querySelector('h1')!.textContent).toBe('IZN-2026-0001');
    expect(host.textContent).toContain('İncelemede');
    expect(host.textContent).toContain('merhaba');
    const dd = [...host.querySelectorAll('dt')].find((d) => d.textContent === 'Gün')!.nextElementSibling!;
    expect(dd.textContent).toBe('5');
    expect(host.querySelector('.timeline')!.textContent).toContain('Çalışan talebi oluşturdu.');
    expect(host.textContent).toContain('Yönetici onayı');
  });

  it('onay kutusu yalnızca bekleyen görevi olan kullanıcıya görünür', async () => {
    api.requestDetail.mockResolvedValue(base());
    await render();
    expect(host.querySelector('.decision-box')).toBeNull();
    expect(button('Onayla')).toBeUndefined();

    api.requestDetail.mockResolvedValue(base({ myPendingTaskId: 't1' }));
    await render('r2');
    expect(host.querySelector('.decision-box')).not.toBeNull();
    expect(button('Onayla')).toBeDefined();
  });

  it('reddetmek gerekçe yazılana kadar kapalıdır; onay yorumla gönderilir', async () => {
    api.requestDetail.mockResolvedValue(base({ myPendingTaskId: 't1' }));
    api.approveTask.mockResolvedValue(base({ summary: { ...base().summary, status: 'approved' }, myPendingTaskId: undefined }));
    await render();
    expect(button('Reddet')!.disabled).toBe(true);
    await typeComment('uygun');
    expect(button('Reddet')!.disabled).toBe(false);

    const dispatched = vi.fn();
    window.addEventListener('arcvora:tasks-changed', dispatched);
    await act(async () => { button('Onayla')!.click(); await Promise.resolve(); await Promise.resolve(); });
    expect(api.approveTask).toHaveBeenCalledWith('t1', 'uygun');
    expect(dispatched).toHaveBeenCalled();              // menü rozeti tazelenir
    expect(host.textContent).toContain('Onaylandı');
    expect(host.querySelector('.decision-box')).toBeNull();
    window.removeEventListener('arcvora:tasks-changed', dispatched);
  });

  it('iptal yalnızca yetkiliye görünür ve onay ister', async () => {
    api.requestDetail.mockResolvedValue(base());
    await render();
    expect(button('Talebi iptal et')).toBeUndefined();

    api.requestDetail.mockResolvedValue(base({ canCancel: true }));
    api.cancelRequest.mockResolvedValue(base({ summary: { ...base().summary, status: 'cancelled' } }));
    await render('r3');
    vi.spyOn(window, 'confirm').mockReturnValueOnce(false);
    await act(async () => { button('Talebi iptal et')!.click(); });
    expect(api.cancelRequest).not.toHaveBeenCalled();
    vi.spyOn(window, 'confirm').mockReturnValueOnce(true);
    await act(async () => { button('Talebi iptal et')!.click(); await Promise.resolve(); await Promise.resolve(); });
    expect(api.cancelRequest).toHaveBeenCalledWith('r3');
  });

  it('kullanıcı girdisi HTML içerse bile metin olarak gösterilir (XSS)', async () => {
    api.requestDetail.mockResolvedValue(base({ data: { kind: 'Yıllık', note: '<img src=x onerror=alert(1)><script>alert(2)</script>' } }));
    await render();
    expect(host.querySelector('img')).toBeNull();
    expect(host.querySelector('script')).toBeNull();
    expect(host.textContent).toContain('<img src=x onerror=alert(1)>');
  });

  it('yönlendirilmiş (geri düşüş) görevin nedenini açıklar', async () => {
    const d = base({ myPendingTaskId: 't1' });
    d.tasks[0] = { ...d.tasks[0], fallback: 'manager_missing' };
    api.requestDetail.mockResolvedValue(d);
    await render();
    expect(host.querySelector('.decision-box')!.textContent).toContain('talep edenin yöneticisi tanımlı olmadığı için');
  });

  it('yükleme hatasında anlaşılır bir hata gösterir', async () => {
    api.requestDetail.mockRejectedValue(new Error('x'));
    await render();
    expect(host.querySelector('[role="alert"]')!.textContent).toContain('Talep yüklenemedi');
  });
});
