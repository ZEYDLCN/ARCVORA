import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '../api';
import { canAccess, canUseAccounting, canUseArchitecture, isAdmin, routeHash, type Route } from '../lib/session';
import type { WorkspaceInfo } from '../types';
import type { PaletteItem } from './CommandPalette';
import { IconChecklist, IconDiagram, IconFile, IconFolder, IconInbox, IconPackage, IconShield } from './icons';
import { Shell, type NavGroup, type NavItem } from './Shell';

export interface PageProps {
  user: { id: string; displayName: string };
  workspace: WorkspaceInfo;
  dark: boolean;
  onTheme: () => void;
  onLogout: () => void;
}

export const go = (r: Route) => { window.location.hash = routeHash(r); };

/** Onay kutusundaki bekleyen görev sayısı; karar verilince 'arcvora:tasks-changed' olayıyla tazelenir. */
function usePendingTasks(): number {
  const [n, setN] = useState(0);
  useEffect(() => {
    let alive = true;
    const load = () => { api.myTasks().then((t) => { if (alive) setN(t.length); }).catch(() => { /* rozet yalnızca kolaylıktır */ }); };
    load();
    window.addEventListener('arcvora:tasks-changed', load);
    return () => { alive = false; window.removeEventListener('arcvora:tasks-changed', load); };
  }, []);
  return n;
}

interface Props extends PageProps {
  route: Route;
  /** Mimari projeler sayfası kendi menü öğelerini ekler (yeni proje, örnek proje…). */
  archItems?: NavItem[];
  searchItems?: PaletteItem[];
  searchPlaceholder?: string;
  children: ReactNode;
}

/**
 * Rol bazlı ortak kabuk. App kullanıcısı yalnızca "Uygulamalar" bölümünü görür; mimari ekibi "Mimari projeler"i, master admin
 * ayrıca tasarım ve yönetimi görür. Menü yalnızca kolaylıktır: yetki her istekte sunucuda doğrulanır.
 */
export function AppsShell({ user, workspace, dark, onTheme, onLogout, route, archItems, searchItems, searchPlaceholder, children }: Props) {
  const pending = usePendingTasks();
  const at = (name: Route['name']) => route.name === name || (name === 'apps' && route.name === 'app') || (name === 'requests' && route.name === 'request')
    || (name === 'design' && route.name === 'designer');

  const groups = useMemo<NavGroup[]>(() => {
    const item = (r: Route, label: string, icon: ReactNode, badge?: number): NavItem => ({ id: r.name, label, icon, badge, active: at(r.name), onClick: () => go(r) });
    const out: NavGroup[] = [{
      id: 'apps', title: 'Uygulamalar', items: [
        item({ name: 'apps' }, 'Uygulamalarım', <IconChecklist />),
        item({ name: 'requests' }, 'Taleplerim', <IconFile />),
        item({ name: 'inbox' }, 'Onay kutusu', <IconInbox />, pending),
        ...(canUseAccounting(workspace) ? [item({ name: 'accounting' }, 'Muhasebe', <IconPackage />)] : []),
      ],
    }];
    if (canUseArchitecture(workspace)) out.push({ id: 'arch', title: 'Mimari', items: archItems ?? [item({ name: 'projects' }, 'Projeler', <IconFolder />)] });
    if (isAdmin(workspace)) out.push({
      id: 'admin', title: 'Yönetim', items: [
        item({ name: 'design' }, 'Uygulama tasarla', <IconDiagram />),
        item({ name: 'admin', sub: 'users' }, 'Kullanıcılar ve yetkiler', <IconShield />),
      ],
    });
    return out;
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workspace, pending, route.name, archItems]);

  const search = useMemo<PaletteItem[]>(() => [
    ...groups.flatMap((g) => g.items.map((i) => ({ id: `nav-${i.id}`, label: i.label, group: 'Git', onSelect: i.onClick }))),
    ...(searchItems ?? []),
  ], [groups, searchItems]);

  return (
    <Shell nav={groups[0].items} navTitle={groups[0].title} navGroups={groups.slice(1)} user={user} dark={dark} onTheme={onTheme} onLogout={onLogout}
      searchItems={search} searchPlaceholder={searchPlaceholder ?? 'Sayfa veya uygulama ara…'}>
      {canAccess(route, workspace) ? children : (
        <div className="page-body"><div className="empty-state"><b>Bu sayfaya erişiminiz yok</b><span>Yetki gerekiyorsa master admin ile görüşün.</span></div></div>
      )}
    </Shell>
  );
}
