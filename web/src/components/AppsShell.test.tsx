// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Route } from '../lib/session';
import type { WorkspaceInfo } from '../types';

vi.mock('../api', async (orig) => ({ ...(await orig<typeof import('../api')>()), api: { myTasks: vi.fn().mockResolvedValue([{ id: 't1' }, { id: 't2' }]) } }));
const { AppsShell } = await import('./AppsShell');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const ws = (role: string, isAccounting = false): WorkspaceInfo => ({ id: 'w', name: 'W', role, canAccept: false, isAccounting });

const render = async (workspace: WorkspaceInfo, route: Route = { name: 'apps' }) => {
  await act(async () => {
    root.render(<AppsShell route={route} user={{ id: 'u', displayName: 'Test' }} workspace={workspace} dark={false} onTheme={() => {}} onLogout={() => {}}><p id="content">içerik</p></AppsShell>);
    await Promise.resolve();
  });
};
const navLabels = () => [...host.querySelectorAll('.sb-nav .sb-item .label')].map((e) => e.textContent);

beforeEach(() => { host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('AppsShell rol bazlı menü', () => {
  it('app kullanıcısı yalnızca Uygulamalar ve Hesap bölümlerini görür', async () => {
    await render(ws('app_user'));
    expect(navLabels()).toEqual(['Uygulamalarım', 'Taleplerim', 'Onay kutusu', 'Belge doğrula', 'Parolamı değiştir']);
    expect(host.querySelector('#content')).not.toBeNull();
  });

  it('mimari ekibi projeleri görür ama yönetimi görmez', async () => {
    await render(ws('editor'));
    expect(navLabels()).toContain('Projeler');
    expect(navLabels()).not.toContain('Uygulama tasarla');
    expect(navLabels()).not.toContain('Kullanıcılar ve yetkiler');
  });

  it('master admin her bölümü görür', async () => {
    await render(ws('admin'));
    for (const l of ['Uygulamalarım', 'Onay kutusu', 'Muhasebe', 'Projeler', 'Uygulama tasarla', 'Kullanıcılar ve yetkiler']) expect(navLabels()).toContain(l);
  });

  it('muhasebe üyesi (app kullanıcısı) Muhasebe menüsünü görür, tasarımı görmez', async () => {
    await render(ws('app_user', true));
    expect(navLabels()).toContain('Muhasebe');
    expect(navLabels()).not.toContain('Uygulama tasarla');
  });

  it('onay kutusu rozeti bekleyen görev sayısını gösterir', async () => {
    await render(ws('app_user'));
    const inbox = [...host.querySelectorAll('.sb-item')].find((e) => e.textContent?.includes('Onay kutusu'))!;
    expect(inbox.querySelector('.count')?.textContent).toBe('2');
  });

  it('yetkisiz sayfada içerik yerine açıklama gösterir', async () => {
    await render(ws('app_user'), { name: 'design' });
    expect(host.querySelector('#content')).toBeNull();
    expect(host.textContent).toContain('Bu sayfaya erişiminiz yok');
    await render(ws('app_user'), { name: 'project', id: 'x' });
    expect(host.querySelector('#content')).toBeNull();
  });

  it('geçerli sayfa menüde işaretlenir', async () => {
    await render(ws('admin'), { name: 'designer', id: 'x' });
    const current = [...host.querySelectorAll('.sb-item[aria-current="page"] .label')].map((e) => e.textContent);
    expect(current).toEqual(['Uygulama tasarla']);
  });
});
