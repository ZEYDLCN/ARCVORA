import { useEffect, useMemo, useRef, useState } from 'react';
import { IconSearch } from './icons';

export interface PaletteItem { id: string; label: string; hint?: string; group: string; keywords?: string; onSelect: () => void }

const fold = (s: string) => s.replace(/İ/g, 'i').replace(/I/g, 'ı').toLowerCase();

export function filterItems(items: PaletteItem[], query: string, limit = 40): PaletteItem[] {
  const q = fold(query.trim());
  if (!q) return items.slice(0, limit);
  const words = q.split(/\s+/);
  return items
    .map((it) => ({ it, hay: fold(`${it.id} ${it.label} ${it.hint ?? ''} ${it.keywords ?? ''}`) }))
    .filter(({ hay }) => words.every((w) => hay.includes(w)))
    .sort((a, b) => Number(fold(b.it.label).startsWith(q)) - Number(fold(a.it.label).startsWith(q)))
    .slice(0, limit)
    .map(({ it }) => it);
}

/** ⌘K / Ctrl+K arama paleti: bölümlere, nesnelere ve projelere hızlı geçiş. */
export function CommandPalette({ open, onClose, items, placeholder }: { open: boolean; onClose: () => void; items: PaletteItem[]; placeholder: string }) {
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const input = useRef<HTMLInputElement>(null);
  const results = useMemo(() => filterItems(items, query), [items, query]);

  useEffect(() => {
    if (!open) return;
    setQuery(''); setActive(0);
    // Paleti açan öğeyi hatırla; kapanınca odağı ona geri ver (WAI-ARIA dialog deseni). Aksi halde odak body'ye düşer.
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const t = setTimeout(() => input.current?.focus(), 0);
    // Modal: Escape, odak diyaloğun içinde olmasa bile paleti kapatır (odak kutuya henüz geçmemiş veya dışarı düşmüş olabilir).
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.preventDefault(); onClose(); } };
    window.addEventListener('keydown', onEsc);
    return () => {
      window.removeEventListener('keydown', onEsc);
      clearTimeout(t);
      if (opener && opener.isConnected && opener !== document.body) opener.focus();
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  useEffect(() => { setActive(0); }, [query]);

  if (!open) return null;

  const choose = (it: PaletteItem | undefined) => { if (it) { onClose(); it.onSelect(); } };
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') { e.preventDefault(); onClose(); }
    else if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => Math.min(results.length - 1, a + 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(0, a - 1)); }
    else if (e.key === 'Enter') { e.preventDefault(); choose(results[active]); }
  };

  let lastGroup = '';
  return (
    <div className="palette-backdrop" onMouseDown={onClose}>
      <div className="palette" role="dialog" aria-modal="true" aria-label="Ara" onMouseDown={(e) => e.stopPropagation()} onKeyDown={onKey}>
        <div className="palette-input">
          <IconSearch size={18} />
          <input ref={input} value={query} onChange={(e) => setQuery(e.target.value)} placeholder={placeholder}
            role="combobox" aria-expanded="true" aria-controls="palette-list" aria-activedescendant={results[active] ? `pi-${results[active].id}` : undefined} />
          <kbd>Esc</kbd>
        </div>
        <ul id="palette-list" role="listbox" className="palette-list">
          {results.length === 0 && <li className="palette-empty">Sonuç yok</li>}
          {results.map((it, i) => {
            const header = it.group !== lastGroup ? (lastGroup = it.group) : null;
            return (
              <li key={`${it.group}-${it.id}`} role="presentation">
                {header && <div className="palette-group">{header}</div>}
                <button id={`pi-${it.id}`} role="option" aria-selected={i === active} className={i === active ? 'on' : ''}
                  onMouseEnter={() => setActive(i)} onClick={() => choose(it)}>
                  <span className="pl">{it.label}</span>
                  {it.hint && <span className="ph">{it.hint}</span>}
                </button>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
