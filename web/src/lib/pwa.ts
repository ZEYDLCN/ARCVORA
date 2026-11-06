// PWA: yalnızca üretim derlemesinde service worker kaydedilir (geliştirmede Vite'ın sıcak yüklemesiyle çakışmasın).

export interface SwHost {
  serviceWorker?: { register: (url: string, options?: { scope?: string }) => Promise<unknown> };
}

/** Kayıt denemesi başarısız olsa bile uygulama normal çalışır (service worker bir iyileştirmedir, gereklilik değildir). */
export async function registerServiceWorker(nav: SwHost | undefined, production: boolean): Promise<boolean> {
  if (!production || !nav?.serviceWorker) return false;
  try {
    await nav.serviceWorker.register('/sw.js', { scope: '/' });
    return true;
  } catch {
    return false;
  }
}

/** Ana ekrana eklenmiş (bağımsız pencere) olarak mı çalışıyor? */
export function isStandalone(win: { matchMedia?: (q: string) => { matches: boolean }; navigator?: { standalone?: boolean } }): boolean {
  return Boolean(win.matchMedia?.('(display-mode: standalone)').matches || win.navigator?.standalone);
}
