import { useEffect, useMemo, useRef, useState } from 'react';
import { IconInfo, IconPlus } from './icons';
import { KindIcons } from './icons';
import { TechLogo } from './TechLogo';
import { layoutComponents, NODE_H, NODE_W } from '../lib/canvas';
import { brandTitle, techLogoIds } from '../lib/techLogo';
import type { LogoId } from '../lib/logos.generated';
import type { Model } from '../types';

const kindLabel: Record<string, string> = {
  client: 'İstemci', api: 'API', module: 'Modül', worker: 'İşleyici', database: 'Veritabanı',
  external_service: 'Dış servis', queue: 'Kuyruk', storage: 'Depolama',
};
const edgeClass: Record<string, string> = { sync: 'sync', async: 'async', event: 'async', data: 'data' };
const clip = (s: string, n: number) => (s.length <= n ? s : s.slice(0, n - 1) + '…');

/**
 * Mimari tuval: aynı model sürümünden türetilen, marka logolu ve noktalı akış çizgili görünüm.
 * Mermaid görünümlerinin yerini almaz; dışa aktarılan çıktı yine Mermaid/Markdown'dır.
 */
export function ArchCanvas({ model, selected, onSelect }: { model: Model; selected: string | null; onSelect: (id: string) => void }) {
  const layout = useMemo(() => layoutComponents(model.components, model.relationships), [model.components, model.relationships]);
  const stack = useMemo(() => {
    const m = new Map<LogoId, number>();
    model.components.filter((c) => c.reviewStatus !== 'rejected').forEach((c) => techLogoIds(c.technology, 4).forEach((id) => m.set(id, (m.get(id) ?? 0) + 1)));
    return [...m].sort((a, b) => b[1] - a[1]);
  }, [model.components]);
  const wrap = useRef<HTMLDivElement>(null);
  const [boxWidth, setBoxWidth] = useState(0);
  const [zoom, setZoom] = useState<number | null>(null);   // null: sığdır

  useEffect(() => {
    const el = wrap.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setBoxWidth(el.clientWidth));
    ro.observe(el);
    setBoxWidth(el.clientWidth);
    return () => ro.disconnect();
  }, [layout.nodes.length]);

  // Varsayılan ölçek okunabilir kalır (en az %78; daha geniş mimaride yatay kaydırma). "Sığdır" tüm genişliğe sığdırır.
  const fitAll = boxWidth > 0 && layout.width > 0 ? Math.min(1, Math.max(0.4, (boxWidth - 24) / layout.width)) : 1;
  const scale = zoom ?? Math.max(0.78, fitAll);
  const step = (d: number) => setZoom(Math.min(1.6, Math.max(0.4, +(scale + d).toFixed(2))));

  // Seçilen bileşen kaydırma alanının dışındaysa görünür alana getirilir
  useEffect(() => {
    const el = wrap.current;
    const n = layout.nodes.find((x) => x.id === selected);
    if (!el || !n) return;
    const left = n.x * scale;
    const right = (n.x + NODE_W) * scale;
    if (left < el.scrollLeft + 16 || right > el.scrollLeft + el.clientWidth - 16) {
      el.scrollTo({ left: Math.max(0, left - el.clientWidth / 2 + (NODE_W * scale) / 2), behavior: 'smooth' });
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected]);

  if (layout.nodes.length === 0) {
    return <div className="empty-state"><b>Henüz bileşen yok.</b><span>Gereksinimleri çıkarıp “Mimari taslak oluştur” dediğinizde bileşenler burada belirir.</span></div>;
  }

  const connected = new Set<string>();
  if (selected) layout.edges.forEach((e) => { if (e.from === selected || e.to === selected || e.id === selected) { connected.add(e.from); connected.add(e.to); connected.add(e.id); } });
  const dim = (id: string) => selected !== null && layout.nodes.some((n) => n.id === selected) && !connected.has(id) && id !== selected;

  return (
    <div className="canvas-wrap">
      {stack.length > 0 && (
        <div className="stack-strip" aria-label="Teknoloji yığını">
          <span className="muted">Teknoloji yığını</span>
          {stack.map(([id, n]) => (
            <span key={id} className="stack-chip"><TechLogo id={id} size={18} surfaceDark={false} />{brandTitle(id)}{n > 1 && <small>×{n}</small>}</span>
          ))}
        </div>
      )}
      <div className="zoom" role="group" aria-label="Yakınlaştırma">
        <button className="icon-btn" onClick={() => step(-0.1)} aria-label="Uzaklaştır" title="Uzaklaştır">−</button>
        <span className="zoom-pct" aria-live="polite">%{Math.round(scale * 100)}</span>
        <button className="icon-btn" onClick={() => step(0.1)} aria-label="Yakınlaştır" title="Yakınlaştır"><IconPlus size={16} /></button>
        <button className="btn small" onClick={() => setZoom(fitAll)} title="Tüm mimariyi genişliğe sığdır">Sığdır</button>
        <button className="btn small" onClick={() => setZoom(null)} title="Okunabilir varsayılan ölçek">Sıfırla</button>
      </div>
      <div className="canvas-scroll" ref={wrap}>
      <svg className="canvas" viewBox={`0 0 ${layout.width} ${layout.height}`} width={layout.width * scale} height={layout.height * scale}
        role="group" aria-label="Mimari tuval: bileşenler ve aralarındaki akışlar">
        <defs>
          {(['sync', 'async', 'data'] as const).map((k) => (
            <marker key={k} id={`arrow-${k}`} viewBox="0 0 10 10" refX="8.5" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
              <path d="M1 1.5 9 5 1 8.5z" className={`arrow ${k}`} />
            </marker>
          ))}
          <radialGradient id="hub-glow"><stop offset="0" stopColor="var(--accent)" stopOpacity=".35" /><stop offset="1" stopColor="var(--accent)" stopOpacity="0" /></radialGradient>
          <filter id="node-shadow" x="-20%" y="-30%" width="140%" height="180%"><feDropShadow dx="0" dy="5" stdDeviation="7" floodColor="#1b1f3b" floodOpacity=".16" /></filter>
        </defs>

        {layout.hub && (() => {
          const h = layout.nodes.find((n) => n.id === layout.hub)!;
          return <ellipse cx={h.x + NODE_W / 2} cy={h.y + NODE_H / 2} rx={NODE_W * 0.95} ry={NODE_H * 1.7} fill="url(#hub-glow)" pointerEvents="none" />;
        })()}

        <g className="edges">
          {layout.edges.map((e) => {
            const cls = edgeClass[e.communication] ?? 'sync';
            return (
              <g key={e.id} className={`edge ${cls} ${dim(e.id) ? 'dim' : ''} ${selected === e.id ? 'on' : ''}`}
                onClick={() => onSelect(e.id)} tabIndex={0} role="button" aria-label={`${e.from} → ${e.to}: ${e.label || 'bağlantı'}`}
                onKeyDown={(ev) => { if (ev.key === 'Enter') onSelect(e.id); }}>
                <path d={e.d} className="hit" />
                <path d={e.d} className="line flow" markerEnd={`url(#arrow-${cls})`} />
                {e.label && (
                  <g transform={`translate(${e.lx} ${e.ly})`} className="elabel">
                    <title>{e.label}</title>
                    <rect x={-(Math.min(e.label.length, 18) * 2.9 + 8)} y={-10} width={Math.min(e.label.length, 18) * 5.8 + 16} height={20} rx={10} />
                    <text textAnchor="middle" dy="4">{clip(e.label, 18)}</text>
                  </g>
                )}
              </g>
            );
          })}
        </g>

        <g className="nodes">
          {layout.nodes.map((n) => {
            const ids = techLogoIds(`${n.technology ?? ''} ${n.name}`, 2);
            const Kind = KindIcons[n.kind] ?? KindIcons.module;
            return (
              <g key={n.id} transform={`translate(${n.x} ${n.y})`} data-node-id={n.id}
                className={`cnode ${n.kind} ${selected === n.id ? 'sel' : ''} ${dim(n.id) ? 'dim' : ''} ${layout.hub === n.id ? 'hub' : ''}`}
                tabIndex={0} role="button" aria-label={`${n.name} (${kindLabel[n.kind] ?? n.kind})`}
                onClick={() => onSelect(n.id)} onKeyDown={(ev) => { if (ev.key === 'Enter') onSelect(n.id); }}>
                <title>{n.name}{n.technology ? ` — ${n.technology}` : ''}</title>
                <rect className="card" width={NODE_W} height={NODE_H} rx={18} filter="url(#node-shadow)" />
                <g transform="translate(14 14)" className="badge-wrap">
                  {ids.length === 0 ? (
                    <><circle cx={19} cy={19} r={19} className="badge-bg" /><g transform="translate(9 9)" className="kind-icon">{Kind({ size: 20 })}</g></>
                  ) : ids.map((id, i) => (
                    <g key={id} transform={`translate(${i * 20} ${i * 0})`}>
                      <circle cx={19} cy={19} r={19} className="badge-bg" />
                      <g transform="translate(9 9)"><TechLogo id={id} size={20} surfaceDark={false} /></g>
                    </g>
                  ))}
                </g>
                <clipPath id={`clip-${n.id}`}><rect x={0} y={0} width={NODE_W - 10} height={NODE_H} /></clipPath>
                <g clipPath={`url(#clip-${n.id})`}>
                  <text x={14 + 38 + (ids.length > 1 ? 20 : 0) + 12} y={29} className="ntitle">{clip(n.name, ids.length > 1 ? 11 : 14)}</text>
                  <text x={14 + 38 + (ids.length > 1 ? 20 : 0) + 12} y={46} className="nsub">{clip(n.technology || kindLabel[n.kind] || n.kind, ids.length > 1 ? 13 : 17)}</text>
                </g>
              </g>
            );
          })}
        </g>
      </svg>
      </div>

      <div className="legend" aria-label="Çizgi türleri">
        <span><i className="swatch sync" /> Eşzamanlı çağrı</span>
        <span><i className="swatch async" /> Eşzamansız / olay</span>
        <span><i className="swatch data" /> Veri erişimi</span>
        <span className="hint"><IconInfo size={16} /> Ok yönü: isteği başlatandan hedefe. Bir bileşene tıklayınca bağlantıları öne çıkar.</span>
      </div>
    </div>
  );
}
