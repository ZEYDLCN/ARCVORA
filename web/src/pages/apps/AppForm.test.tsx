// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flowTemplates } from '../../lib/templates';

const mocks = vi.hoisted(() => ({ appForm: vi.fn(), directory: vi.fn(), submit: vi.fn(), uploadFile: vi.fn(), deleteAttachment: vi.fn() }));
vi.mock('../../api', async (orig) => ({ ...(await orig<typeof import('../../api')>()), api: mocks }));
const { ApiError } = await import('../../api');
const { AppForm } = await import('./AppForm');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const leave = flowTemplates.find((t) => t.id === 'leave')!.definition;
const ctx = { user: { id: 'u', displayName: 'T' }, workspace: { id: 'w', name: 'W', role: 'app_user', canAccept: false } };

const flush = async () => { for (let i = 0; i < 4; i++) await act(async () => { await Promise.resolve(); }); };
const render = async () => { await act(async () => { root.render(<AppForm id="app1" {...ctx} />); }); await flush(); };
const ctl = (label: string) => {
  const l = [...host.querySelectorAll('label')].find((x) => x.textContent?.startsWith(label))!;
  return document.getElementById(l.htmlFor) as HTMLInputElement | HTMLSelectElement;
};
const set = (el: HTMLInputElement | HTMLSelectElement, v: string) => act(() => {
  const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto, 'value')!.set!.call(el, v);
  el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
});
const submit = async () => { await act(async () => { host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }); await flush(); };

beforeEach(() => {
  vi.clearAllMocks();
  window.location.hash = '#/apps/app1';
  mocks.appForm.mockResolvedValue({ app: { id: 'app1', workspaceId: 'w', name: 'İzin Talebi', description: 'Yıllık izin', icon: 'calendar', versionNumber: 1 }, definition: leave });
  mocks.directory.mockResolvedValue([{ id: 'u9', displayName: 'Vekil' }]);
  host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('AppForm', () => {
  it('formu yayınlanmış tanımdan çizer ve onay akışını önceden gösterir', async () => {
    await render();
    expect(host.querySelector('h1')!.textContent).toBe('İzin Talebi');
    expect(ctl('İzin türü')).not.toBeNull();
    const strip = host.querySelector('.step-strip')!.textContent!;
    expect(strip).toContain('Yönetici onayı');
    expect(strip).toContain('(koşullu)');                 // 10 günden uzun izin adımı
  });

  it('girdiyi temizleyip sunucuya Idempotency-Key ile gönderir ve talep sayfasına geçer', async () => {
    mocks.submit.mockResolvedValue({ summary: { id: 'req-9' } });
    await render();
    set(ctl('İzin türü'), 'Rapor');
    set(ctl('Başlangıç tarihi'), '2026-11-10');
    set(ctl('Bitiş tarihi'), '2026-11-12');
    await submit();
    expect(mocks.submit).toHaveBeenCalledTimes(1);
    const [id, payload, key] = mocks.submit.mock.calls[0];
    expect(id).toBe('app1');
    expect(payload).toEqual({ kind: 'Rapor', start: '2026-11-10', end: '2026-11-12' });      // hesaplanan gün gönderilmez
    expect(typeof key).toBe('string');
    expect(key.length).toBeGreaterThan(8);
    expect(window.location.hash).toBe('#/requests/req-9');
  });

  it('sunucu bulgularını ilgili alanın altına koyar ve düzeltmeden sonra o alanın hatasını siler', async () => {
    mocks.submit.mockRejectedValue(new ApiError(422, 'invalid_request', 'Form geçersiz.', [
      { code: 'required', path: 'data.kind', message: "'İzin türü' zorunlu." },
      { code: 'not_before', path: 'data.end', message: 'Bitiş başlangıçtan önce olamaz.' },
    ]));
    await render();
    await submit();
    expect(host.textContent).toContain("'İzin türü' zorunlu.");
    expect(host.textContent).toContain('Bitiş başlangıçtan önce olamaz.');
    expect(host.querySelector('.notice.error')!.textContent).toContain('düzeltilmesi gereken alanlar');
    expect(window.location.hash).toBe('#/apps/app1');           // sayfadan ayrılmaz

    set(ctl('İzin türü'), 'Rapor');
    expect(host.textContent).not.toContain("'İzin türü' zorunlu.");
    expect(host.textContent).toContain('Bitiş başlangıçtan önce olamaz.');
  });

  it('başarısız denemeden sonra yeni bir idempotency anahtarı kullanır', async () => {
    mocks.submit.mockRejectedValueOnce(new ApiError(422, 'invalid_request', 'x', [])).mockResolvedValueOnce({ summary: { id: 'r' } });
    await render();
    await submit();
    await submit();
    expect(mocks.submit.mock.calls[0][2]).not.toBe(mocks.submit.mock.calls[1][2]);
  });

  it('yetki/yayın hatalarını gösterir', async () => {
    mocks.appForm.mockRejectedValue(new ApiError(403, 'no_app_access', 'Bu uygulamayı kullanma yetkiniz yok.'));
    await render();
    expect(host.querySelector('[role="alert"]')!.textContent).toContain('yetkiniz yok');
    expect(host.querySelector('form')).toBeNull();
  });

  describe('ek alanı', () => {
    const receiptDef = {
      schema: 1, prefix: 'MSF', summaryFields: ['amount'],
      fields: [{ key: 'amount', label: 'Tutar', type: 'money', required: true }, { key: 'receipt', label: 'Fiş', type: 'file', maxFiles: 2 }],
      steps: [{ id: 's1', name: 'Yönetici', approvers: { type: 'manager' } }],
    };
    const pickFile = async (name: string) => {
      const input = host.querySelector('input[type=file]:not([capture])') as HTMLInputElement;
      Object.defineProperty(input, 'files', { value: [new File([new Uint8Array(10)], name, { type: 'image/png' })], configurable: true });
      await act(async () => { input.dispatchEvent(new Event('change', { bubbles: true })); });
      await flush();
    };

    beforeEach(() => {
      mocks.appForm.mockResolvedValue({ app: { id: 'app1', workspaceId: 'w', name: 'Masraf', description: '', icon: 'wallet', versionNumber: 1 }, definition: receiptDef });
    });

    it('dosyayı uygulama kimliğiyle yükler ve talep gönderilirken yalnızca kimlikleri yollar', async () => {
      mocks.uploadFile.mockResolvedValue({ id: 'f1', fileName: 'fis.png', size: 10, contentType: 'image/png' });
      mocks.submit.mockResolvedValue({ summary: { id: 'req-1' } });
      await render();
      set(ctl('Tutar'), '125,50');
      await pickFile('fis.png');
      expect(mocks.uploadFile).toHaveBeenCalledWith('app1', expect.any(File));
      expect(host.textContent).toContain('fis.png');
      await submit();
      expect(mocks.submit.mock.calls[0][1]).toEqual({ amount: 125.5, receipt: ['f1'] });
    });

    it('sunucu "geçersiz dosya" derse hatayı ek alanının altında gösterir', async () => {
      mocks.submit.mockRejectedValue(new ApiError(422, 'invalid_request', 'Form geçersiz.', [
        { code: 'invalid_file', path: 'data.receipt', message: 'Eklenen dosya geçersiz veya zaten kullanılmış. Dosyayı yeniden yükleyin.' },
      ]));
      await render();
      set(ctl('Tutar'), '10');
      await submit();
      expect(host.querySelector('.field .err')!.textContent).toContain('yeniden yükleyin');
    });
  });
});
