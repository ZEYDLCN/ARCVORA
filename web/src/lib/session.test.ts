import { describe, expect, it } from 'vitest';
import type { WorkspaceInfo } from '../types';
import { canAccess, defaultRoute, parseRoute, routeHash } from './session';

const id = '0f8fad5b-d9cb-469f-a165-70867728950e';
const ws = (role: string, isAccounting = false): WorkspaceInfo => ({ id: 'w', name: 'W', role, canAccept: false, isAccounting });

describe('parseRoute', () => {
  it('bilinen yolları ve kimlikleri ayrıştırır', () => {
    expect(parseRoute('#/apps')).toEqual({ name: 'apps' });
    expect(parseRoute(`#/apps/${id}`)).toEqual({ name: 'app', id });
    expect(parseRoute(`#/requests/${id}`)).toEqual({ name: 'request', id });
    expect(parseRoute(`#/p/${id}`)).toEqual({ name: 'project', id });
    expect(parseRoute(`#/design/${id}`)).toEqual({ name: 'designer', id });
    expect(parseRoute('#/inbox')).toEqual({ name: 'inbox' });
    expect(parseRoute('#/account')).toEqual({ name: 'account' });
    expect(parseRoute('#/projects/')).toEqual({ name: 'projects' });
  });
  it('yönetim alt sekmeleri vardır, varsayılan kullanıcılardır', () => {
    expect(parseRoute('#/admin')).toEqual({ name: 'admin', sub: 'users' });
    expect(parseRoute('#/admin/groups')).toEqual({ name: 'admin', sub: 'groups' });
    expect(parseRoute('#/admin/ACCOUNTING')).toEqual({ name: 'admin', sub: 'accounting' });
    expect(parseRoute('#/admin/demo')).toEqual({ name: 'admin', sub: 'demo' });
    expect(parseRoute('#/admin/email')).toEqual({ name: 'admin', sub: 'email' });
    expect(parseRoute('#/admin/requests')).toEqual({ name: 'admin', sub: 'requests' });
    expect(parseRoute('#/admin/baska')).toEqual({ name: 'home' });
  });
  it('geçersiz kimlik, boş ve bilinmeyen yol home olur', () => {
    expect(parseRoute('')).toEqual({ name: 'home' });
    expect(parseRoute('#/')).toEqual({ name: 'home' });
    expect(parseRoute('#/apps/123')).toEqual({ name: 'home' });
    expect(parseRoute('#/bilinmeyen')).toEqual({ name: 'home' });
    expect(parseRoute(`#/apps/${id}/fazla`)).toEqual({ name: 'home' });
  });
  it('routeHash ile gidiş-dönüş tutarlıdır', () => {
    for (const r of [{ name: 'apps' }, { name: 'app', id }, { name: 'request', id }, { name: 'project', id }, { name: 'designer', id },
      { name: 'admin', sub: 'groups' }, { name: 'admin', sub: 'requests' }, { name: 'account' }, { name: 'inbox' }, { name: 'accounting' }, { name: 'verify' }, { name: 'delegations' }, { name: 'design' }, { name: 'projects' }, { name: 'requests' }] as const)
      expect(parseRoute(routeHash(r))).toEqual(r);
  });
});

describe('roller', () => {
  it('app kullanıcısı apps ana sayfasına gider, mimari ekibi projelere', () => {
    expect(defaultRoute(ws('app_user')).name).toBe('apps');
    expect(defaultRoute(ws('admin')).name).toBe('projects');
    expect(defaultRoute(ws('reader')).name).toBe('projects');
    expect(defaultRoute(undefined).name).toBe('apps');
  });
  it('app kullanıcısı mimari/yönetim/muhasebe sayfalarına giremez', () => {
    const u = ws('app_user');
    for (const n of ['projects', 'design', 'admin', 'accounting'] as const) expect(canAccess({ name: n }, u)).toBe(false);
    expect(canAccess({ name: 'verify' }, u)).toBe(true);
    expect(canAccess({ name: 'delegations' }, u)).toBe(true);     // vekâlet her üyeye açık           // belge doğrulama her üyeye açık
    expect(canAccess({ name: 'project', id }, u)).toBe(false);
    for (const n of ['apps', 'requests', 'inbox', 'account'] as const) expect(canAccess({ name: n }, u)).toBe(true);
  });
  it('muhasebe üyesi muhasebeye girer ama tasarıma giremez; admin her yere girer', () => {
    expect(canAccess({ name: 'accounting' }, ws('app_user', true))).toBe(true);
    expect(canAccess({ name: 'design' }, ws('app_user', true))).toBe(false);
    for (const n of ['projects', 'design', 'admin', 'accounting', 'apps'] as const) expect(canAccess({ name: n }, ws('admin'))).toBe(true);
    expect(canAccess({ name: 'projects' }, ws('editor'))).toBe(true);
    expect(canAccess({ name: 'admin' }, ws('editor'))).toBe(false);
  });
  it('oturum/çalışma alanı yoksa hiçbir sayfaya girilmez', () => expect(canAccess({ name: 'apps' }, undefined)).toBe(false));
});
