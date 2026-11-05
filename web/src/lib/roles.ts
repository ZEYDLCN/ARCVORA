export const roleLabels: Record<string, string> = {
  admin: 'Master admin', editor: 'Düzenleyici (mimari)', reader: 'Okuyucu (mimari)', app_user: 'Uygulama kullanıcısı',
};
export const roleOptions = ['app_user', 'reader', 'editor', 'admin'] as const;

const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';

/** Geçici parola önerisi: karıştırılan karakterler (0/O, 1/l/I) olmadan, tarayıcının güvenli rastgele kaynağından. */
export function generatePassword(length = 14, random: (n: number) => Uint32Array = (n) => crypto.getRandomValues(new Uint32Array(n))): string {
  const buf = random(length);
  return Array.from(buf, (v) => ALPHABET[v % ALPHABET.length]).join('');
}
