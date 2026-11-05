// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PickedFile } from '../lib/files';
import { FileField, type UploadApi } from './FileField';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const upload = vi.fn<(f: File) => Promise<PickedFile>>();
const remove = vi.fn<(id: string) => Promise<void>>();
const api: UploadApi = { upload, remove };

function Harness({ max = 2, initial = [] as PickedFile[], uploads = api as UploadApi | null }) {
  const [v, setV] = useState<PickedFile[]>(initial);
  return <FileField label="Fiş" value={v} maxFiles={max} uploads={uploads ?? undefined} onChange={setV} />;
}
const render = async (props: Parameters<typeof Harness>[0] = {}) => { await act(async () => { root.render(<Harness {...props} />); }); };
const flush = async () => { for (let i = 0; i < 5; i++) await act(async () => { await Promise.resolve(); }); };
const pick = async (...files: File[]) => {
  const input = host.querySelector('input[type=file]:not([capture])') as HTMLInputElement;
  Object.defineProperty(input, 'files', { value: files, configurable: true });
  await act(async () => { input.dispatchEvent(new Event('change', { bubbles: true })); });
  await flush();
};
const file = (name: string, size = 100, type = 'image/png') => new File([new Uint8Array(size)], name, { type });
const done = (name: string, i = 1): PickedFile => ({ id: `id${i}`, fileName: name, size: 100, contentType: 'image/png' });
const names = () => [...host.querySelectorAll('.file-name')].map((x) => x.textContent);

beforeEach(() => { vi.clearAllMocks(); host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('FileField', () => {
  it('seçilen dosyayı hemen yükler ve listeler', async () => {
    upload.mockResolvedValueOnce(done('fis.png'));
    await render();
    await pick(file('fis.png'));
    expect(upload).toHaveBeenCalledTimes(1);
    expect(names()).toEqual(['fis.png']);
  });

  it('birden çok dosyayı sırayla yükler ve sınırı aşanı eklemez', async () => {
    upload.mockResolvedValueOnce(done('a.png', 1)).mockResolvedValueOnce(done('b.png', 2));
    await render({ max: 2 });
    await pick(file('a.png'), file('b.png'), file('c.png'));
    expect(upload).toHaveBeenCalledTimes(2);
    expect(names()).toEqual(['a.png', 'b.png']);
    expect(host.querySelector('[role=alert]')!.textContent).toContain('c.png');
    expect(host.textContent).toContain('En fazla 2 dosya');
    expect([...host.querySelectorAll('button')].filter((b) => b.textContent === 'Dosya seç' || b.textContent === 'Kamera ile çek').every((b) => b.disabled)).toBe(true);
  });

  it('desteklenmeyen, boş ve büyük dosyaları yüklemeden reddeder', async () => {
    await render();
    await pick(file('virus.exe', 100, 'application/x-msdownload'), file('bos.png', 0), file('dev.pdf', 11 * 1024 * 1024, 'application/pdf'));
    expect(upload).not.toHaveBeenCalled();
    const alerts = host.querySelector('[role=alert]')!.textContent!;
    expect(alerts).toContain('virus.exe');
    expect(alerts).toContain('boş');
    expect(alerts).toContain('10 MB');
  });

  it('sunucu reddederse nedenini gösterir ve listeye eklemez', async () => {
    upload.mockRejectedValueOnce(new Error('Yalnızca PDF, PNG, JPEG veya WEBP dosyaları yüklenebilir.'));
    await render();
    await pick(file('fis.png'));
    expect(names()).toEqual([]);
    expect(host.querySelector('[role=alert]')!.textContent).toContain('Yalnızca PDF');
  });

  it('kaldırınca listeden çıkarır ve sunucudaki hazırlanmış dosyayı siler', async () => {
    remove.mockResolvedValue();
    await render({ initial: [done('a.png', 1), done('b.png', 2)] });
    await act(async () => { (host.querySelector('button[aria-label="a.png dosyasını kaldır"]') as HTMLButtonElement).click(); });
    expect(names()).toEqual(['b.png']);
    expect(remove).toHaveBeenCalledWith('id1');
  });

  it('silme başarısız olsa da listeden çıkar (süre dolunca sunucu temizler)', async () => {
    remove.mockRejectedValue(new Error('x'));
    await render({ initial: [done('a.png')] });
    await act(async () => { (host.querySelector('button[aria-label="a.png dosyasını kaldır"]') as HTMLButtonElement).click(); });
    await flush();
    expect(names()).toEqual([]);
  });

  it('kamera girişi telefonda arka kamerayı ister', async () => {
    await render();
    const cam = host.querySelector('input[capture]') as HTMLInputElement;
    expect(cam.getAttribute('capture')).toBe('environment');
    expect(cam.accept).toBe('image/*');
  });

  it('yükleme olanağı yoksa düğmeler devre dışıdır', async () => {
    await render({ uploads: null });
    expect([...host.querySelectorAll('button')].every((b) => b.disabled)).toBe(true);
  });

  it('tek dosyalık alanda çoklu seçim açılmaz', async () => {
    await render({ max: 1 });
    expect((host.querySelector('input[type=file]:not([capture])') as HTMLInputElement).multiple).toBe(false);
  });
});
