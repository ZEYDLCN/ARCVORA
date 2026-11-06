// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ lists: vi.fn(), list: vi.fn(), createList: vi.fn(), updateList: vi.fn(), setListItems: vi.fn(), deleteList: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { ListsAdmin } = await import('./ListsAdmin');
const { ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 6; i++) await act(async () => { await Promise.resolve(); }); };
const render = async () => { await act(async () => { root.render(<ListsAdmin ws="w1" />); }); await flush(); };
const button = (t: string) => [...host.querySelectorAll('button')].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
const click = async (el: Element | undefined) => { await act(async () => { (el as HTMLElement).click(); }); await flush(); };
const type = (el: Element, v: string) => act(() => {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event('input', { bubbles: true }));
});
const detail = { id: 'l1', name: 'Birimler', items: [{ id: 'i1', value: 'Satış', active: true }, { id: 'i2', value: 'Depo', active: false }] };

beforeEach(() => {
  vi.clearAllMocks();
  api.lists.mockResolvedValue([{ id: 'l1', name: 'Birimler', itemCount: 2, activeCount: 1, updatedAt: '2026-11-05T10:00:00Z' }]);
  api.list.mockResolvedValue(detail);
  host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); vi.restoreAllMocks(); });

describe('Listeler sekmesi', () => {
  it('listeleri aktif/pasif sayılarıyla gösterir', async () => {
    await render();
    expect(host.textContent).toContain('Birimler');
    expect(host.textContent).toContain('1 aktif öğe · 1 pasif');
  });

  it('yeni liste satırlardan oluşturulur ve düzenleyicide açılır', async () => {
    api.createList.mockResolvedValue({ id: 'l2', name: 'Şehirler', items: [{ id: 'c1', value: 'Ankara', active: true }] });
    await render();
    type(host.querySelector('#ln-name')!, 'Şehirler');
    type(host.querySelector('#ln-items')!, 'Ankara\n\n  İzmir  \n');
    await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }); await flush();
    expect(api.createList).toHaveBeenCalledWith('w1', { name: 'Şehirler', items: ['Ankara', 'İzmir'] });
    expect(host.querySelector('[data-testid=list-editor]')!.textContent).toContain('Şehirler');
  });

  it('öğe düzenleme: yeniden adlandır, pasifleştir, sırala, sil, toplu ekle ve kaydet', async () => {
    api.setListItems.mockResolvedValue(detail);
    await render();
    await click(host.querySelector('.list .item')!);
    const first = host.querySelector('input[aria-label="Öğe 1"]')!;
    type(first, 'Satış ve Pazarlama');
    await click(host.querySelector('input[aria-label="Öğe 2 aktif"]')!);          // Depo tekrar aktif
    type(host.querySelector('#le-bulk')!, 'Üretim\nHukuk');
    await click(button('Satırları ekle'));
    await click(host.querySelector('button[aria-label="Öğe 4 yukarı"]')!);          // Hukuk, Üretim'in üstüne
    await click(host.querySelector('button[aria-label="Öğe 1 sil"]')!);             // Satış ve Pazarlama silinir
    await click(button('Öğeleri kaydet'));
    expect(api.setListItems).toHaveBeenCalledWith('w1', 'l1', [
      { id: 'i2', value: 'Depo', active: true },
      { value: 'Hukuk', active: true },
      { value: 'Üretim', active: true },
    ]);
    expect(host.querySelector('[role=status]')!.textContent).toContain('Öğeler kaydedildi');
  });

  it('kullanımdaki listenin silinmesi hatasını gösterir; onay verilmezse silinmez', async () => {
    api.deleteList.mockRejectedValue(new ApiError(409, 'list_in_use', 'Liste şu uygulamalarda kullanılıyor: İzin.'));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    await render();
    await click(host.querySelector('.list .item')!);
    await click(button('Listeyi sil'));
    expect(api.deleteList).not.toHaveBeenCalled();
    await click(button('Listeyi sil'));
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(host.querySelector('[role=alert]')!.textContent).toContain('kullanılıyor');
  });

  it('ad/açıklama ayrı kaydedilir', async () => {
    api.updateList.mockResolvedValue({ ...detail, name: 'Departmanlar' });
    await render();
    await click(host.querySelector('.list .item')!);
    type(host.querySelector('#le-name')!, 'Departmanlar');
    await click(button('Adı/açıklamayı kaydet'));
    expect(api.updateList).toHaveBeenCalledWith('w1', 'l1', { name: 'Departmanlar', description: '' });
  });
});
