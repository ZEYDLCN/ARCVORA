import type { WorkspaceInfo } from '../types';

export type RouteName =
  | 'home' | 'projects' | 'project' | 'apps' | 'app' | 'requests' | 'request' | 'inbox' | 'accounting' | 'verify' | 'design' | 'designer' | 'admin' | 'account';

export interface Route { name: RouteName; id?: string; sub?: string }

const UUID = '([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})';
const rx = (path: string) => new RegExp(`^#/${path}$`, 'i');

/** Adres çubuğundaki (#/…) yolu rotaya çevirir. Tanınmayan yol "home" sayılır (rol bazlı ana sayfaya yönlenir). */
export function parseRoute(hash: string): Route {
  const h = hash.replace(/\/+$/, '');
  const id = (m: RegExpExecArray | null) => m?.[1];
  let m: RegExpExecArray | null;
  if (h === '#/projects') return { name: 'projects' };
  if ((m = rx(`p/${UUID}`).exec(h))) return { name: 'project', id: id(m) };
  if (h === '#/apps') return { name: 'apps' };
  if ((m = rx(`apps/${UUID}`).exec(h))) return { name: 'app', id: id(m) };
  if (h === '#/requests') return { name: 'requests' };
  if ((m = rx(`requests/${UUID}`).exec(h))) return { name: 'request', id: id(m) };
  if (h === '#/inbox') return { name: 'inbox' };
  if (h === '#/account') return { name: 'account' };
  if (h === '#/accounting') return { name: 'accounting' };
  if (h === '#/verify') return { name: 'verify' };
  if (h === '#/design') return { name: 'design' };
  if ((m = rx(`design/${UUID}`).exec(h))) return { name: 'designer', id: id(m) };
  if ((m = /^#\/admin(?:\/(users|groups|accounting|requests))?$/i.exec(h))) return { name: 'admin', sub: (m[1] ?? 'users').toLowerCase() };
  return { name: 'home' };
}

export function routeHash(r: Route): string {
  switch (r.name) {
    case 'projects': return '#/projects';
    case 'project': return `#/p/${r.id}`;
    case 'apps': return '#/apps';
    case 'app': return `#/apps/${r.id}`;
    case 'requests': return '#/requests';
    case 'request': return `#/requests/${r.id}`;
    case 'inbox': return '#/inbox';
    case 'account': return '#/account';
    case 'accounting': return '#/accounting';
    case 'verify': return '#/verify';
    case 'design': return '#/design';
    case 'designer': return `#/design/${r.id}`;
    case 'admin': return `#/admin/${r.sub ?? 'users'}`;
    default: return '#/';
  }
}

export const isAppUser = (ws?: WorkspaceInfo) => ws?.role === 'app_user';
export const isAdmin = (ws?: WorkspaceInfo) => ws?.role === 'admin';
/** Mimari projeler (ARCVORA) yalnızca app kullanıcısı olmayanlara açıktır. */
export const canUseArchitecture = (ws?: WorkspaceInfo) => !!ws && ws.role !== 'app_user';
export const canUseAccounting = (ws?: WorkspaceInfo) => !!ws && (ws.role === 'admin' || ws.isAccounting === true);

/** Oturum açınca gidilen sayfa: app kullanıcısı uygulamalarına, mimari ekibi projelerine. */
export function defaultRoute(ws?: WorkspaceInfo): Route {
  return isAppUser(ws) ? { name: 'apps' } : canUseArchitecture(ws) ? { name: 'projects' } : { name: 'apps' };
}

/** Arayüz tarafı kolaylık denetimi; asıl yetki her zaman sunucudadır. */
export function canAccess(r: Route, ws?: WorkspaceInfo): boolean {
  if (!ws) return false;
  switch (r.name) {
    case 'projects': case 'project': return canUseArchitecture(ws);
    case 'design': case 'designer': case 'admin': return isAdmin(ws);
    case 'accounting': return canUseAccounting(ws);
    default: return true;
  }
}
