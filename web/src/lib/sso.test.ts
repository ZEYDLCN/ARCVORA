import { describe, expect, it } from 'vitest';
import { parseSsoHash, ssoErrorText } from './sso';

describe('parseSsoHash', () => {
  it('SSO olmayan adreslerde null döner', () => {
    for (const h of ['', '#/', '#/apps', '#/ssoX', '#/sso/evil', '#sso']) expect(parseSsoHash(h)).toBeNull();
  });
  it('bileti ve hatayı okur', () => {
    const ticket = 'abcDEF123_-.' + 'x'.repeat(30);
    expect(parseSsoHash(`#/sso?ticket=${ticket}`)).toEqual({ ticket, error: undefined });
    expect(parseSsoHash('#/sso?error=sso_not_allowed')).toEqual({ ticket: undefined, error: 'sso_not_allowed' });
    expect(parseSsoHash('#/sso')).toEqual({ ticket: undefined, error: undefined });
  });
  it('beklenmeyen karakterli bileti yok sayar, beklenmeyen hata kodunu genel hataya çevirir', () => {
    expect(parseSsoHash('#/sso?ticket=<script>alert(1)</script>')!.ticket).toBeUndefined();
    expect(parseSsoHash('#/sso?ticket=kisa')!.ticket).toBeUndefined();
    expect(parseSsoHash('#/sso?error=' + encodeURIComponent('<img src=x>'))!.error).toBe('sso_failed');
  });
});

describe('ssoErrorText', () => {
  it('bilinen kodlar için açıklama, bilinmeyenler için genel mesaj verir', () => {
    expect(ssoErrorText('sso_not_allowed')).toContain('izniniz yok');
    expect(ssoErrorText('sso_expired')).toContain('süresi doldu');
    expect(ssoErrorText('uydurma')).toContain('tamamlanamadı');
    expect(ssoErrorText(undefined)).toContain('tamamlanamadı');
  });
});
