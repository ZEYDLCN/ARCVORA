import { useEffect, useRef, useState } from 'react';
import { modelIdFromNode } from '../lib/model';

let counter = 0;

/**
 * Mermaid'i güvenli ayarlarla (securityLevel: 'strict') çizer: kullanıcı kaynaklı HTML ve tıklanabilir
 * yönergeler (click) devre dışıdır. Düğüm tıklaması DOM dinleyicisiyle bağlanır, Mermaid yönergesiyle değil.
 */
export function MermaidView({ source, dark, onSelect }: { source: string; dark: boolean; onSelect?: (id: string) => void }) {
  const host = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const mermaid = (await import('mermaid')).default;
        mermaid.initialize({
          startOnLoad: false,
          securityLevel: 'strict',
          theme: dark ? 'dark' : 'default',
          fontFamily: 'system-ui, sans-serif',
          flowchart: { htmlLabels: false },
        });
        const { svg } = await mermaid.render(`arcvora-mmd-${++counter}`, source);
        if (cancelled || !host.current) return;
        host.current.innerHTML = svg;
        setError(null);

        if (onSelect) {
          host.current.querySelectorAll<SVGGElement>('g[id]').forEach((g) => {
            const id = modelIdFromNode(g.id);
            if (!id || !/^(g\.node|node|entity|flowchart)/i.test(g.className.baseVal + ' ' + g.id)) return;
            g.setAttribute('data-node-id', id);
            g.setAttribute('tabindex', '0');
            g.setAttribute('role', 'button');
            g.addEventListener('click', () => onSelect(id));
            g.addEventListener('keydown', (e) => { if ((e as KeyboardEvent).key === 'Enter') onSelect(id); });
          });
        }
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Diyagram çizilemedi.');
      }
    })();
    return () => { cancelled = true; };
  }, [source, dark, onSelect]);

  return (
    <div>
      <div ref={host} className="diagram" role="img" aria-label="Mimari diyagram" />
      {error && (
        <div className="notice error" role="alert" style={{ marginTop: 8 }}>
          Diyagram çizilemedi: {error}. Metinsel karşılık ve kaynak aşağıdadır.
        </div>
      )}
    </div>
  );
}
