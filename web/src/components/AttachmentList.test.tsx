// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AttachmentInfo } from '../types';

const api = vi.hoisted(() => ({ attachmentBlob: vi.fn() }));
vi.mock('../api', async (orig) => ({ ...(await orig<typeof import('../api')>()), api }));
const { AttachmentList } = await import('./AttachmentList');

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const att = (over: Partial<AttachmentInfo> = {}): AttachmentInfo => ({ id: 'a1', fieldKey: 'receipt', fileName: 'fis.png', contentType: 'image/png', size: 2048, sha256: 'x', uploadedBy: 'u', createdAt: '2026-11-05T10:00:00Z', ...over });
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };

beforeEach(() => {
  vi.clearAllMocks();
  host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host);
  URL.createObjectURL = vi.fn(() => 'blob:fake');
  URL.revokeObjectURL = vi.fn();
});
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('AttachmentList', () => {
  it('boşken "Ek yok" der', async () => {
    await act(async () => { root.render(<AttachmentList attachments={[]} />); });
    expect(host.textContent).toContain('Ek yok');
  });

  it('adı, alan etiketini ve boyutu gösterir; görselin önizlemesini yükler', async () => {
    api.attachmentBlob.mockResolvedValue(new Blob(['x'], { type: 'image/png' }));
    await act(async () => { root.render(<AttachmentList attachments={[att()]} labels={{ receipt: 'Fiş' }} />); });
    await flush();
    expect(host.textContent).toContain('fis.png');
    expect(host.textContent).toContain('Fiş · 2 KB');
    const img = host.querySelector('img')!;
    expect(img.getAttribute('src')).toBe('blob:fake');
    expect(img.getAttribute('alt')).toBe('fis.png');
  });

  it('PDF için önizleme istemez, yalnızca simge gösterir', async () => {
    await act(async () => { root.render(<AttachmentList attachments={[att({ fileName: 'fatura.pdf', contentType: 'application/pdf' })]} />); });
    await flush();
    expect(api.attachmentBlob).not.toHaveBeenCalled();
    expect(host.querySelector('img')).toBeNull();
  });

  it('indir düğmesi dosyayı getirir ve kaydeder; hata olursa söyler', async () => {
    api.attachmentBlob.mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' }));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await act(async () => { root.render(<AttachmentList attachments={[att({ fileName: 'fatura.pdf', contentType: 'application/pdf' })]} />); });
    await act(async () => { (host.querySelector('button[aria-label="fatura.pdf dosyasını indir"]') as HTMLButtonElement).click(); });
    await flush();
    expect(api.attachmentBlob).toHaveBeenCalledWith('a1');
    expect(click).toHaveBeenCalledTimes(1);

    api.attachmentBlob.mockRejectedValue(new Error('x'));
    await act(async () => { (host.querySelector('button[aria-label="fatura.pdf dosyasını indir"]') as HTMLButtonElement).click(); });
    await flush();
    expect(host.querySelector('[role=alert]')!.textContent).toContain('indirilemedi');
    click.mockRestore();
  });

  it('dosya adındaki HTML metin olarak gösterilir, çalıştırılmaz', async () => {
    await act(async () => { root.render(<AttachmentList attachments={[att({ fileName: '<img src=x onerror=alert(1)>.pdf', contentType: 'application/pdf' })]} />); });
    expect(host.querySelector('li img')).toBeNull();
    expect(host.textContent).toContain('<img src=x onerror=alert(1)>.pdf');
  });
});
