// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ emailStatus: vi.fn(), updateEmail: vi.fn(), sendTestEmail: vi.fn(), emailOutbox: vi.fn(), retryEmail: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api }));
const { EmailAdmin } = await import('./EmailAdmin');
const { ApiError } = await import('../../api');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };
const render = async () => { await act(async () => { root.render(<EmailAdmin ws="w1" />); }); await flush(); };
const button = (t: string) => [...host.querySelectorAll('button')].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
const on = { configured: true, emailEnabled: true, remindersEnabled: true, pending: 2, failed: 1, from: 'noreply@firma.com' };
const row = (over = {}) => ({ id: 'm1', to: 'ali@firma.com', kind: 'task_assigned', subject: '[ARCVORA] Onayınız bekleniyor', status: 'failed', attempts: 3, lastError: 'SMTP kimlik doğrulaması başarısız', createdAt: '2026-11-05T10:00:00Z', ...over });

beforeEach(() => { vi.clearAllMocks(); api.emailStatus.mockResolvedValue(on); api.emailOutbox.mockResolvedValue([row()]); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('E-posta sekmesi', () => {
  it('durumu, gönderen adresi ve sayaçları gösterir', async () => {
    await render();
    expect(host.textContent).toContain('noreply@firma.com');
    expect(host.textContent).toContain('Bekleyen: 2');
    expect(host.textContent).toContain('Hatalı: 1');
    expect(host.textContent).not.toContain('yapılandırılmamış');
  });

  it('sunucuda yapılandırılmamışsa nasıl açılacağını söyler ve test düğmesi kapalıdır', async () => {
    api.emailStatus.mockResolvedValue({ ...on, configured: false, from: undefined });
    await render();
    expect(host.textContent).toContain('Email__Enabled=true');
    expect(button('Test e-postası gönder')!.disabled).toBe(true);
  });

  it('anahtarları sunucuya yazar', async () => {
    api.updateEmail.mockResolvedValue({ ...on, emailEnabled: false });
    await render();
    await act(async () => { (host.querySelector('#em-on') as HTMLInputElement).click(); }); await flush();
    expect(api.updateEmail).toHaveBeenCalledWith('w1', { emailEnabled: false });
    await act(async () => { (host.querySelector('#em-rem') as HTMLInputElement).click(); }); await flush();
    expect(api.updateEmail).toHaveBeenLastCalledWith('w1', { remindersEnabled: false });
  });

  it('test e-postasının sonucunu ve hatasını gösterir', async () => {
    api.sendTestEmail.mockResolvedValueOnce({ sent: true, to: 'admin@firma.com' }).mockRejectedValueOnce(new ApiError(502, 'email_failed', 'SMTP kimlik doğrulaması başarısız (kullanıcı adı/parola).'));
    await render();
    await act(async () => { button('Test e-postası gönder')!.click(); }); await flush();
    expect(host.querySelector('[role=status]')!.textContent).toContain('admin@firma.com');
    await act(async () => { button('Test e-postası gönder')!.click(); }); await flush();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('kimlik doğrulaması');
  });

  it('hatalı kaydı gösterir ve yeniden dener; gönderilmişte düğme yoktur', async () => {
    api.retryEmail.mockResolvedValue(row({ status: 'pending' }));
    api.emailOutbox.mockResolvedValue([row(), row({ id: 'm2', status: 'sent', lastError: undefined })]);
    await render();
    expect(host.textContent).toContain('SMTP kimlik doğrulaması başarısız (3. deneme)');
    expect([...host.querySelectorAll('button')].filter((b) => b.textContent === 'Yeniden dene')).toHaveLength(1);
    await act(async () => { button('Yeniden dene')!.click(); }); await flush();
    expect(api.retryEmail).toHaveBeenCalledWith('w1', 'm1');
  });

  it('süzgeç sunucuya durum olarak gider', async () => {
    await render();
    await act(async () => { button('Hatalı')!.click(); }); await flush();
    expect(api.emailOutbox).toHaveBeenLastCalledWith('w1', 'failed');
  });
});
