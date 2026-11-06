// Tek oturum açma (OIDC) dönüşü: sunucu tarayıcıyı `#/sso?ticket=…` (başarı) veya `#/sso?error=…` (hata) adresine yönlendirir.

export interface SsoReturn { ticket?: string; error?: string }

/** Adres çubuğu dönüşü SSO'dan mı? Değilse null. Değer ve hata kodları yalnızca beklenen karakterlerden oluşur; aksi hâlde yok sayılır. */
export function parseSsoHash(hash: string): SsoReturn | null {
  const m = /^#\/sso(?:\?(.*))?$/.exec(hash);
  if (!m) return null;
  const q = new URLSearchParams(m[1] ?? '');
  const ticket = q.get('ticket') ?? undefined;
  const error = q.get('error') ?? undefined;
  return {
    ticket: ticket && /^[A-Za-z0-9_.-]{20,2000}$/.test(ticket) ? ticket : undefined,
    error: error && /^[a-z_]{1,40}$/.test(error) ? error : error ? 'sso_failed' : undefined,
  };
}

const messages: Record<string, string> = {
  sso_denied: 'Giriş sağlayıcıda iptal edildi veya reddedildi.',
  sso_expired: 'Giriş oturumunun süresi doldu veya bu tarayıcıda başlatılmadı. Lütfen yeniden deneyin.',
  sso_not_allowed: 'Bu hesapla giriş yapma izniniz yok. Hesabınızın açılması için yöneticinizle görüşün.',
  sso_email_unverified: 'E-posta adresiniz sağlayıcıda doğrulanmamış.',
  sso_no_email: 'Sağlayıcı e-posta adresinizi iletmedi; giriş yapılamadı.',
  sso_conflict: 'Bu e-posta başka bir kimliğe bağlı. Yöneticinizle görüşün.',
  sso_failed: 'Tek oturum açma tamamlanamadı. Lütfen yeniden deneyin.',
};

export function ssoErrorText(code: string | undefined): string {
  return (code && messages[code]) || messages.sso_failed;
}

/** Adres çubuğundaki bileti/hatayı temizler (geçmişte ve yer imlerinde kalmasın). */
export function clearSsoHash(): void {
  try { history.replaceState(null, '', window.location.pathname + window.location.search + '#/'); } catch { window.location.hash = '#/'; }
}
