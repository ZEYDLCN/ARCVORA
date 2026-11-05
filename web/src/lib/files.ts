// Ek yükleme: istemci tarafı ön kontroller (hızlı geri bildirim için). Kesin karar sunucudadır: tür sihirli baytlardan belirlenir.

export const MAX_FILE_BYTES = 10 * 1024 * 1024;
const ALLOWED_EXT = /\.(pdf|png|jpe?g|webp)$/i;
const ALLOWED_TYPES = new Set(['application/pdf', 'image/png', 'image/jpeg', 'image/webp']);

/** Yüklenmiş ve henüz talebe bağlanmamış dosya (formda tutulur). */
export interface PickedFile { id: string; fileName: string; size: number; contentType: string }

/** Seçilen dosya yüklenebilir mi? Değilse kullanıcıya gösterilecek neden. */
export function checkPick(file: Pick<File, 'name' | 'size' | 'type'>): string | null {
  if (file.size === 0) return `“${file.name}” boş bir dosya.`;
  if (file.size > MAX_FILE_BYTES) return `“${file.name}” en fazla 10 MB olabilir.`;
  const typeOk = file.type !== '' && ALLOWED_TYPES.has(file.type);
  if (!typeOk && !(file.type === '' && ALLOWED_EXT.test(file.name))) return `“${file.name}” desteklenmiyor. Yalnızca PDF, PNG, JPEG veya WEBP yüklenebilir.`;
  return null;
}

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const nf = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 });
  return bytes < 1024 * 1024 ? `${nf.format(bytes / 1024)} KB` : `${nf.format(bytes / 1024 / 1024)} MB`;
}
