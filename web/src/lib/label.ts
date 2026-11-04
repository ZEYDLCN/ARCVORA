const ELLIPSIS = '…';

/** Satırın sonuna "…" ekler; satır sınırını aşmaz. */
function ellipsize(line: string, maxChars: number): string {
  return line.length < maxChars ? line + ELLIPSIS : line.slice(0, maxChars - 1) + ELLIPSIS;
}

/**
 * Düğüm adını en fazla `maxLines` satıra sarar. Sözcükler boşluktan bölünür; satırdan uzun tek bir sözcük sert biçimde kesilir.
 * Sığmayan kısım SON satırın sonunda "…" ile belirtilir (tam ad yine de fare üstü ipucunda durur).
 * Önceki davranış adı tek satırda kırpıyordu ("Entegrasyon i…"); diyagramın okunurluğu için artık sarılır.
 */
export function wrapLabel(name: string, maxChars: number, maxLines = 2): string[] {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return [''];

  const lines: string[] = [];
  let current = '';
  let i = 0;
  while (i < words.length && lines.length < maxLines) {
    const word = words[i];
    const candidate = current ? `${current} ${word}` : word;
    if (candidate.length <= maxChars) {
      current = candidate;
      i++;
    } else if (current) {
      lines.push(current); // satırı kapat; aynı sözcüğü yeni satırda dene
      current = '';
    } else {
      lines.push(word.slice(0, maxChars)); // satırdan uzun tek sözcük: sert kes, kalanı sonraki satıra bırak
      words[i] = word.slice(maxChars);
      if (!words[i]) i++;
    }
  }
  if (current && lines.length < maxLines) lines.push(current);

  const overflow = i < words.length;
  if (overflow) lines[lines.length - 1] = ellipsize(lines[lines.length - 1], maxChars);
  return lines;
}
