/**
 * Açılış animasyonunun ne zaman gösterileceği. Oturum başına bir kez: SPA içi gezinme veya sayfa yenileme
 * animasyonu tekrarlamaz. `?intro` adresi (örn. http://localhost:5173/?intro) animasyonu zorla gösterir (demo/inceleme).
 */
export const INTRO_KEY = 'arcvora.intro';

type Store = Pick<Storage, 'getItem' | 'setItem'>;

/** sessionStorage kapalı/engelli olabilir; erişim hatası uygulamayı etkilemez. */
export function safeSessionStorage(): Store | null {
  try { return typeof window !== 'undefined' ? window.sessionStorage : null; } catch { return null; }
}

export function shouldShowIntro(search: string, store: Store | null): boolean {
  if (/(^|[?&])intro(=|&|$)/.test(search)) return true;
  try { if (store?.getItem(INTRO_KEY)) return false; } catch { /* depolama yoksa bu yüklemede göster */ }
  return true;
}

export function markIntroSeen(store: Store | null): void {
  try { store?.setItem(INTRO_KEY, '1'); } catch { /* yoksay */ }
}
