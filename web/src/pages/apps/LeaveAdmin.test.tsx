// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FlowDefinition } from '../../lib/flow';

const api = vi.hoisted(() => ({
  holidays: vi.fn(), addHoliday: vi.fn(), deleteHoliday: vi.fn(), seedHolidays: vi.fn(), leaveBalances: vi.fn(), addLeaveEntry: vi.fn(),
  distributeLeave: vi.fn(), carryoverLeave: vi.fn(),
}));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { LeaveAdmin } = await import('./LeaveAdmin');
const { LeaveSettings } = await import('./designer/LeaveSettings');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 6; i++) await act(async () => { await Promise.resolve(); }); };
const button = (t: string) => [...host.querySelectorAll('button')].find((b) => b.textContent?.includes(t)) as HTMLButtonElement | undefined;
const click = async (el: Element | undefined) => { await act(async () => { (el as HTMLElement).click(); }); await flush(); };
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
const year = new Date().getFullYear();

beforeEach(() => {
  vi.clearAllMocks();
  api.holidays.mockResolvedValue([{ id: 'h1', date: `${year}-10-28`, name: 'Cumhuriyet Bayramı Arifesi', halfDay: true }]);
  api.leaveBalances.mockResolvedValue([{ userId: 'u1', name: 'Can', hireDate: '2020-03-01', entitled: 14, adjusted: -2, carried: 3.5, used: 5, available: 10.5 }]);
  host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); vi.restoreAllMocks(); });

describe('İzin ve tatiller', () => {
  const mount = async () => { await act(async () => { root.render(<LeaveAdmin ws="w1" />); }); await flush(); };

  it('tatilleri (yarım gün rozetiyle) ve bakiyeleri Türkçe sayı biçimiyle gösterir', async () => {
    await mount();
    expect(host.textContent).toContain('Cumhuriyet Bayramı Arifesi');
    expect(host.textContent).toContain('yarım gün');
    expect(host.textContent).toContain('10,5');                 // kalan
    expect(host.textContent).toContain('3,5');                  // devir
    expect(api.holidays).toHaveBeenCalledWith('w1', year);
  });

  it('sabit tatilleri ekler ve sonucu bildirir; tatil ekleme/silme çalışır', async () => {
    api.seedHolidays.mockResolvedValue({ added: 8, skipped: 0 });
    api.addHoliday.mockResolvedValue({});
    api.deleteHoliday.mockResolvedValue(undefined);
    await mount();
    await click(button('Sabit resmî tatilleri ekle'));
    expect(api.seedHolidays).toHaveBeenCalledWith('w1', year);
    expect(host.querySelector('[role=status]')!.textContent).toContain('8 tatil eklendi');
    type('hl-date', `${year}-03-20`); type('hl-name', 'Ramazan Bayramı');
    await act(async () => { host.querySelectorAll('form')[0].dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }); await flush();
    expect(api.addHoliday).toHaveBeenCalledWith('w1', { date: `${year}-03-20`, name: 'Ramazan Bayramı', halfDay: false });
    await click(host.querySelector('button[aria-label="Cumhuriyet Bayramı Arifesi tatilini sil"]')!);
    expect(api.deleteHoliday).toHaveBeenCalledWith('w1', 'h1');
  });

  it('kıdem dağıtımı, devir ve düzeltme kaydı sunucuya doğru gider', async () => {
    api.distributeLeave.mockResolvedValue({ created: 3, skippedExisting: 1, skippedNoHireDate: 2, skippedNoEntitlement: 0 });
    api.carryoverLeave.mockResolvedValue({ created: 2, totalDays: 7.5, skippedExisting: 0 });
    api.addLeaveEntry.mockResolvedValue({});
    await mount();
    await click(button('Kıdem haklarını dağıt'));
    expect(api.distributeLeave).toHaveBeenCalledWith('w1', year);
    expect(host.querySelector('[role=status]')!.textContent).toContain('3 kişiye hak yazıldı');
    type('lv-cap', '5');
    await click(button(`bakiyesini ${year + 1}`));
    expect(api.carryoverLeave).toHaveBeenCalledWith('w1', year, 5);
    expect(host.querySelector('[role=status]')!.textContent).toContain('7,5 gün devredildi');
    pick('le-user', 'u1'); pick('le-kind', 'adjustment'); type('le-days', '-1,5'); type('le-note', 'düzeltme');
    await act(async () => { host.querySelectorAll('form')[1].dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }); await flush();
    expect(api.addLeaveEntry).toHaveBeenCalledWith('w1', { userId: 'u1', year, kind: 'adjustment', days: -1.5, note: 'düzeltme' });
  });
});

describe('Tasarlayıcı: izin bakiyesi ayarı', () => {
  const def: FlowDefinition = {
    schema: 1, prefix: 'IZN', summaryFields: [], steps: [],
    fields: [
      { key: 'start', label: 'Başlangıç', type: 'date' }, { key: 'days', label: 'Gün', type: 'number' },
      { key: 'kind', label: 'Tür', type: 'select', options: ['Yıllık', 'Mazeret'] },
    ],
  };

  it('açınca gün/başlangıç alanlarını otomatik seçer; tür seçilince hangi türlerin düşeceğini sorar', async () => {
    const changes: unknown[] = [];
    const render = async (d: FlowDefinition) => { await act(async () => { root.render(<LeaveSettings def={d} issues={new Map()} onChange={(l) => changes.push(l)} />); }); };
    await render(def);
    expect(host.querySelector('#lv-days')).toBeNull();
    await click(host.querySelector('#lv-on')!);
    expect(changes.at(-1)).toMatchObject({ enabled: true, daysField: 'days', startField: 'start' });

    await render({ ...def, completion: { leave: { enabled: true, daysField: 'days', startField: 'start' } } });
    pick('lv-type', 'kind');
    expect(changes.at(-1)).toMatchObject({ typeField: 'kind', countsFor: undefined });
    await render({ ...def, completion: { leave: { enabled: true, daysField: 'days', startField: 'start', typeField: 'kind' } } });
    await click(host.querySelector('input[type=checkbox][id=""]') ?? [...host.querySelectorAll('.multi input')][0]);
    expect(changes.at(-1)).toMatchObject({ countsFor: ['Yıllık'] });
  });

  it('sunucu/yerel bulguları ilgili alanın altında gösterir', async () => {
    const issues = new Map([['completion.leave.daysField', ['Gün sayısı alanı bir sayı alanı olmalı.']]]);
    await act(async () => { root.render(<LeaveSettings def={{ ...def, completion: { leave: { enabled: true } } }} issues={issues} onChange={() => undefined} />); });
    expect(host.querySelector('[role=alert]')!.textContent).toContain('sayı alanı');
  });
});
