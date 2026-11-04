import { useEffect, useState, type ReactNode } from 'react';
import { CommandPalette, type PaletteItem } from './CommandPalette';
import { IconCollapse, IconExpand, IconLogout, IconMenu, IconMoon, IconSearch, IconSun } from './icons';
import { Logo } from './Logo';

export interface NavItem { id: string; label: string; icon: ReactNode; badge?: number | string; active?: boolean; onClick: () => void }

interface Props {
  nav: NavItem[];
  navTitle?: string;
  user: { displayName: string };
  dark: boolean;
  onTheme: () => void;
  onLogout: () => void;
  searchItems: PaletteItem[];
  searchPlaceholder: string;
  children: ReactNode;
}

function readCollapsed(): boolean {
  try { return localStorage.getItem('arcvora.sidebar') === 'collapsed'; } catch { return false; }
}

/**
 * Uygulama kabuğu: taneli renkli zemin üzerinde yüzen beyaz yuvarlak kenarlı kenar çubuğu ve içerik kartı.
 * Kenar çubuğu daraltılabilir; mobilde çekmece olarak açılır. ⌘K / Ctrl+K arama paletini açar.
 */
export function Shell({ nav, navTitle, user, dark, onTheme, onLogout, searchItems, searchPlaceholder, children }: Props) {
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [drawer, setDrawer] = useState(false);
  const [palette, setPalette] = useState(false);

  useEffect(() => {
    try { localStorage.setItem('arcvora.sidebar', collapsed ? 'collapsed' : 'open'); } catch { /* yoksay */ }
  }, [collapsed]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPalette((p) => !p); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const isMac = typeof navigator !== 'undefined' && /mac|iphone|ipad/i.test(navigator.platform || navigator.userAgent);

  return (
    <div className="app" data-collapsed={collapsed} data-drawer={drawer}>
      <div className="bg" aria-hidden="true" />

      <header className="mobile-bar">
        <button className="icon-btn" onClick={() => setDrawer(true)} aria-label="Menüyü aç"><IconMenu /></button>
        <Logo size={28} />
        <span className="spacer" />
        <button className="icon-btn" onClick={() => setPalette(true)} aria-label="Ara"><IconSearch /></button>
      </header>
      {drawer && <div className="scrim" onClick={() => setDrawer(false)} aria-hidden="true" />}

      <aside className="sidebar" aria-label="Ana menü">
        <div className="sb-head">
          <Logo size={34} />
          <button className="icon-btn collapse" onClick={() => setCollapsed((c) => !c)} aria-label={collapsed ? 'Kenar çubuğunu genişlet' : 'Kenar çubuğunu daralt'}
            title={collapsed ? 'Genişlet' : 'Daralt'}>
            {collapsed ? <IconExpand /> : <IconCollapse />}
          </button>
        </div>

        <button className="sb-search" onClick={() => setPalette(true)} aria-label="Ara" title="Ara (Ctrl/⌘ + K)">
          <IconSearch size={20} />
          <span className="label">Ara</span>
          <kbd className="label">{isMac ? '⌘' : 'Ctrl'} K</kbd>
        </button>

        <nav className="sb-nav" aria-label={navTitle ?? 'Bölümler'}>
          {navTitle && <div className="sb-title label">{navTitle}</div>}
          {nav.map((n) => (
            <button key={n.id} className="sb-item" aria-current={n.active ? 'page' : undefined} title={n.label}
              onClick={() => { n.onClick(); setDrawer(false); }}>
              <span className="ico">{n.icon}</span>
              <span className="label">{n.label}</span>
              {n.badge !== undefined && n.badge !== 0 && <span className="count">{n.badge}</span>}
            </button>
          ))}
        </nav>

        <div className="sb-foot">
          <button className="sb-item" onClick={onTheme} title={dark ? 'Açık tema' : 'Koyu tema'}>
            <span className="ico">{dark ? <IconSun /> : <IconMoon />}</span><span className="label">{dark ? 'Açık tema' : 'Koyu tema'}</span>
          </button>
          <button className="sb-item" onClick={onLogout} title="Çıkış">
            <span className="ico"><IconLogout /></span><span className="label">Çıkış · {user.displayName}</span>
          </button>
        </div>
      </aside>

      <main className="main">{children}</main>

      <CommandPalette open={palette} onClose={() => setPalette(false)} items={searchItems} placeholder={searchPlaceholder} />
    </div>
  );
}
