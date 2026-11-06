// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
  apps: vi.fn(), searchRequests: vi.fn(), downloadFile: vi.fn(), report: vi.fn(), audit: vi.fn(), retention: vi.fn(), updateRetention: vi.fn(),
  retentionPreview: vi.fn(), retentionPurge: vi.fn(), formerUsers: vi.fn(), anonymizeUser: vi.fn(),
}));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { AdminRequests } = await import('./AdminRequests');
const { Reports } = await import('./Reports');
const { AuditLog } = await import('./AuditLog');
const { Retention } = await import('./Retention');
const { qs, ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 6; i++) await act(async () => { await Promise.resolve(); }); };
const mount = async (el: React.ReactElement) => { await act(async () => { root.render(el); }); await flush(); };
const button = (t: string) => [...host.querySelectorAll('button')].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
const click = async (t: string) => { await act(async () => { button(t)!.click(); }); await flush(); };
const type = (id: string, v: string) => act(() => {
  const el = host.querySelector(`#${id}`) as HTMLInputElement;
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event('input', { bubbles: true }));
});
const pick = (id: string, v: string) => act(() => {
  const el = host.querySelector(`#${id}`) as HTMLSelectElement;
  Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event('change', { bubbles: true }));
});
const submit = async () => { await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }); await flush(); };

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); vi.restoreAllMocks(); });

const req = (n: number) => ({ id: `r${n}`, workspaceId: 'w', number: `IZN-2026-000${n}`, appId: 'a', appName: 'İzin', icon: 'calendar', status: 'approved', summary: 'x', requesterId: 'u', requesterName: 'Can', createdAt: '2026-11-05T10:00:00Z' });

describe('qs', () => {
  it('boş değerleri atar ve değerleri kaçışlar', () => {
    expect(qs({ a: '1', b: '', c: undefined, d: false, e: 'ş ö&' })).toBe('?a=1&e=%C5%9F%20%C3%B6%26');
    expect(qs({})).toBe('');
  });
});

describe('Tüm talepler', () => {
  beforeEach(() => { api.apps.mockResolvedValue([{ id: 'a1', name: 'İzin' }]); });

  it('süzgeçlerle arar, sayfalar ve CSV isteğine aynı süzgeçleri taşır', async () => {
    api.searchRequests.mockResolvedValue({ items: [req(1), req(2)], total: 60, page: 1, pageSize: 25 });
    await mount(<AdminRequests ws="w1" />);
    expect(host.textContent).toContain('60 talep · sayfa 1/3');
    type('rq-q', 'yemek'); pick('rq-st', 'approved');
    await submit();
    expect(api.searchRequests).toHaveBeenLastCalledWith('w1', expect.objectContaining({ q: 'yemek', status: 'approved', page: 1, pageSize: 25 }));
    await click('Sonraki →');
    expect(api.searchRequests).toHaveBeenLastCalledWith('w1', expect.objectContaining({ q: 'yemek', page: 2 }));
    api.downloadFile.mockResolvedValue(undefined);
    await click('CSV indir');
    expect(api.downloadFile.mock.calls[0][0]).toBe('/workspaces/w1/reports/requests.csv?q=yemek&status=approved');
  });

  it('sonuç yoksa boş durumu, hata varsa hatayı gösterir', async () => {
    api.searchRequests.mockResolvedValueOnce({ items: [], total: 0, page: 1, pageSize: 25 });
    await mount(<AdminRequests ws="w1" />);
    expect(host.textContent).toContain('Bu aramaya uyan talep bulunmuyor');
    api.searchRequests.mockRejectedValueOnce(new ApiError(422, 'invalid_date', 'from yyyy-AA-gg biçiminde olmalı.'));
    await submit();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('yyyy-AA-gg');
  });
});

describe('Raporlar', () => {
  const report = {
    from: '2026-10-07', to: '2026-11-05', total: 7, byStatus: { in_review: 3, approved: 2, rejected: 1, cancelled: 1 },
    apps: [{ appId: 'a', name: 'İzin Talebi', total: 3, inReview: 1, approved: 1, rejected: 1, cancelled: 0, avgHours: 3.5, medianHours: 60 }],
    approvers: [{ userId: 'u', name: 'Müdür Mehmet', pending: 3, oldestPendingHours: 120, decided: 3 }],
    daily: [{ day: '2026-11-05', count: 7 }], overdue: [{ requestId: 'r1', number: 'IZN-2026-0003', appName: 'İzin', step: 'Yönetici onayı', assignee: 'Müdür Mehmet', ageHours: 120 }],
  };

  it('özeti, uygulama tablosunu, yükü ve geciken görevleri gösterir', async () => {
    api.report.mockResolvedValue(report);
    await mount(<Reports ws="w1" />);
    expect(host.textContent).toContain('Toplam');
    expect(host.textContent).toContain('İzin Talebi');
    expect(host.textContent).toContain('3,5 sa');                 // 3,5 saat
    expect(host.textContent).toContain('2,5 gün');                // 60 saat
    expect(host.textContent).toContain('5,0 gün bekliyor');
    expect(api.report).toHaveBeenCalledWith('w1', expect.objectContaining({ overdueHours: '48', includeDemo: false }));
  });

  it('demo veriyi dahil etme seçeneği raporu yeniler', async () => {
    api.report.mockResolvedValue(report);
    await mount(<Reports ws="w1" />);
    await act(async () => { (host.querySelector('#rp-demo') as HTMLInputElement).click(); }); await flush();
    expect(api.report).toHaveBeenLastCalledWith('w1', expect.objectContaining({ includeDemo: true }));
  });
});

describe('Denetim', () => {
  it('kayıtları kişi adıyla listeler, süzer ve ayrıntıyı açar', async () => {
    api.audit.mockResolvedValue({ items: [{ id: 1, at: '2026-11-05T10:00:00Z', actorName: 'Ayşe', action: 'group.created', objectType: 'group', objectId: 'abcdef1234567890', detail: { name: 'Muhasebe' } }], total: 1, page: 1, pageSize: 50 });
    await mount(<AuditLog ws="w1" />);
    expect(host.textContent).toContain('Ayşe');
    expect(host.textContent).toContain('group.created');
    expect(host.textContent).toContain('group · abcdef12');
    expect(host.querySelector('details pre')!.textContent).toContain('Muhasebe');
    pick('au-act', 'user.created');
    await submit();
    expect(api.audit).toHaveBeenLastCalledWith('w1', expect.objectContaining({ action: 'user.created', page: 1 }));
  });
});

describe('Saklama ve KVKK', () => {
  beforeEach(() => { api.formerUsers.mockResolvedValue([]); });

  it('süre seçimi kaydedilir; önizleme sonrası silme onay ister ve sonucu bildirir', async () => {
    api.retention.mockResolvedValueOnce({ autoPurge: false }).mockResolvedValue({ closedRequestMonths: 12, autoPurge: false });
    api.updateRetention.mockResolvedValue({ closedRequestMonths: 12, autoPurge: false });
    api.retentionPreview.mockResolvedValue({ eligible: 4, skippedAccounting: 1, months: 12 });
    api.retentionPurge.mockResolvedValue({ purged: 4, skipped: 1 });
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    await mount(<Retention ws="w1" />);
    expect(button('Önizle')!.disabled).toBe(true);                         // süre seçilmeden önizleme yok
    pick('rt-m', '12'); await flush();
    expect(api.updateRetention).toHaveBeenCalledWith('w1', { closedRequestMonths: 12 });
    await click('Önizle');
    expect(host.textContent).toContain('Silinecek: 4 talep');
    expect(host.textContent).toContain('korunacak: 1');
    await click('Önizlenenleri sil');
    expect(api.retentionPurge).not.toHaveBeenCalled();                     // onay verilmedi
    await click('Önizlenenleri sil');
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(api.retentionPurge).toHaveBeenCalledWith('w1');
    expect(host.querySelector('[role=status]')!.textContent).toContain('4 talep silindi');
  });

  it('otomatik silme süre belirlenmeden açılamaz; ayrılan kullanıcılar anonimleştirilir', async () => {
    api.retention.mockResolvedValue({ autoPurge: false });
    api.formerUsers.mockResolvedValueOnce([{ id: 'u9', displayName: 'Eski Kişi', email: 'eski@firma.com', createdAt: '2026-01-01T00:00:00Z' }]).mockResolvedValue([]);
    api.anonymizeUser.mockResolvedValue({ anonymized: true });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await mount(<Retention ws="w1" />);
    expect((host.querySelector('#rt-auto') as HTMLInputElement).disabled).toBe(true);
    expect(host.textContent).toContain('eski@firma.com');
    await click('Anonimleştir');
    expect(api.anonymizeUser).toHaveBeenCalledWith('w1', 'u9');
    expect(host.textContent).toContain('Hesap anonimleştirildi');
  });
});
