// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppIcon, appIconKeys } from './AppIcon';
import { RequestCard } from './RequestCard';
import { SectionSwitcher } from './SectionSwitcher';
import { CardTitle, EmptyState, plainName, StatTile, SummaryBoxes, SummaryLine } from './ui';
import { RequestBadge } from './StatusBadge';
import type { SummaryPart } from '../types';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
let host: HTMLDivElement;
let root: Root;
const render = (node: React.ReactNode) => act(() => { root.render(node); });
beforeEach(() => { host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

const parts: SummaryPart[] = [
  { key: 'start', label: 'Başlangıç', kind: 'date', text: '26.11.2026' },
  { key: 'end', label: 'Bitiş', kind: 'date', text: '07.12.2026' },
  { key: 'kind', label: 'Tür', kind: 'text', text: 'Yıllık izin' },
];

describe('ortak arayüz parçaları', () => {
  it('plainName yalnızca demo kayıtlarda "(demo)" sonekini atar', () => {
    expect(plainName('Zeynep Arslan (demo)', true)).toBe('Zeynep Arslan');
    expect(plainName('Zeynep Arslan (Demo) ', true)).toBe('Zeynep Arslan');
    expect(plainName('Zeynep Arslan (demo)', false)).toBe('Zeynep Arslan (demo)');
    expect(plainName('Ali', true)).toBe('Ali');
  });

  it('özet: metin parçaları tek satır, tarih parçaları etiketli kutular olur', () => {
    render(<><SummaryLine parts={parts} /><SummaryBoxes parts={parts} /></>);
    expect(host.querySelector('.sb-line')!.textContent).toBe('Yıllık izin');
    const boxes = [...host.querySelectorAll('.mini-field')].map((b) => b.textContent);
    expect(boxes).toEqual(['Başlangıç26.11.2026', 'Bitiş07.12.2026']);
    expect(host.querySelectorAll('.mini-field svg').length).toBe(2);       // tarihlerde takvim simgesi
  });

  it('özet parçası yoksa düz yazı gösterilir, kutu çıkmaz; hiçbiri yoksa hiçbir şey çizilmez', () => {
    render(<><SummaryLine parts={[]} fallback="10.11.2026 · Yıllık" /><SummaryBoxes parts={[]} /></>);
    expect(host.querySelector('.sb-line')!.textContent).toBe('10.11.2026 · Yıllık');
    expect(host.querySelector('.mini-fields')).toBeNull();
    render(<><SummaryLine /><SummaryBoxes /></>);
    expect(host.innerHTML).toBe('');
  });

  it('en çok dört kutu gösterilir ve tutar/sayı simgesiz kutudur', () => {
    const many: SummaryPart[] = ['a', 'b', 'c', 'd', 'e'].map((k) => ({ key: k, label: k, kind: 'money' as const, text: `${k} TRY` }));
    render(<SummaryBoxes parts={many} />);
    expect(host.querySelectorAll('.mini-field').length).toBe(4);
    expect(host.querySelectorAll('.mini-field svg').length).toBe(0);
  });

  it('boş durum, kart başlığı ve istatistik kutusu içeriklerini erişilebilir biçimde taşır', () => {
    render(<><EmptyState title="Talep yok">Süzgece uyan yok.</EmptyState><CardTitle aside="son 30 gün">Özet</CardTitle><StatTile icon={<i />} label="Toplam" value={7} tone="ok" /></>);
    expect(host.querySelector('.empty-state b')!.textContent).toBe('Talep yok');
    expect(host.querySelector('.empty-state svg')).not.toBeNull();             // varsayılan belge simgesi
    expect(host.querySelector('.card-title h3')!.textContent).toBe('Özet');
    expect(host.querySelector('.ct-aside')!.textContent).toBe('son 30 gün');
    expect(host.querySelector('.stat-tile.ok .st-value')!.textContent).toBe('7');
    expect(host.querySelector('.stat-tile .st-ico')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('durum rozeti simge + etiket gösterir (eski unicode işaret yok)', () => {
    render(<><RequestBadge status="approved" /><RequestBadge status="in_review" /><RequestBadge status="rejected" /><RequestBadge status="cancelled" /></>);
    const badges = [...host.querySelectorAll('.badge')];
    expect(badges.map((b) => b.textContent)).toEqual(['Onaylandı', 'İncelemede', 'Reddedildi', 'İptal edildi']);
    expect(badges.every((b) => b.querySelector('svg') !== null)).toBe(true);
    expect(host.textContent).not.toMatch(/[✓✕◔]/);
  });

  it('her uygulama simgesi bir SVG çizer; bilinmeyen ad belge simgesine düşer', () => {
    for (const k of appIconKeys) { render(<AppIcon name={k} />); expect(host.querySelector('.app-icon svg'), k).not.toBeNull(); }
    render(<AppIcon name="yok-boyle-bir-simge" />);
    expect(host.querySelector('.app-icon svg')).not.toBeNull();
    expect(appIconKeys).toEqual(expect.arrayContaining(['file', 'calendar', 'wallet', 'receipt']));
  });
});

describe('talep kartı', () => {
  const base = { icon: 'calendar', number: 'DIZN-2026-0002', appName: 'İzin talebi', summary: 'x', badge: <RequestBadge status="in_review" />, onOpen: () => {} };

  it('numara, kişi (demo soneksiz) + Demo rozeti, kutular, zaman ve eylemi gösterir; tıklayınca açar', () => {
    const open = vi.fn();
    render(<RequestCard {...base} person="Zeynep Arslan (demo)" isDemo parts={parts} chips={<span className="chip-soft">Master admin onayı</span>} time="3.11.2026 04:54" onOpen={open} />);
    expect(host.querySelector('.rc-title')!.textContent).toBe('DIZN-2026-0002');
    expect(host.querySelector('.rc-sub')!.textContent).toContain('Zeynep Arslan');
    expect(host.querySelector('.rc-sub')!.textContent).not.toContain('(demo)');
    expect(host.querySelector('.chip-demo')!.textContent).toBe('Demo');
    expect(host.querySelectorAll('.mini-field').length).toBe(2);
    expect(host.querySelector('.rc-time')!.textContent).toBe('3.11.2026 04:54');
    expect(host.querySelector('.rc-action')!.textContent).toBe('Talebi incele');
    (host.querySelector('button.req-card') as HTMLButtonElement).click();
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('kendi talebinde kişi/rozet yok; parça yoksa düz özet görünür; not tırnak içinde', () => {
    render(<RequestCard {...base} summary="26.11.2026 · 07.12.2026 · Yıllık" note="Uygun, iyi tatiller." actionLabel="Aç" />);
    expect(host.querySelector('.chip-demo')).toBeNull();
    expect(host.querySelector('.rc-sub')!.textContent).toBe('İncelemede');
    expect(host.querySelector('.sb-line')!.textContent).toBe('26.11.2026 · 07.12.2026 · Yıllık');
    expect(host.querySelector('.rc-note')!.textContent).toBe('“Uygun, iyi tatiller.”');
    expect(host.querySelector('.rc-action')!.textContent).toBe('Aç');
    expect(host.querySelector('.rc-time')).toBeNull();
  });
});

describe('bölüm seçici', () => {
  const sections = [{ id: 'users', label: 'Kullanıcılar' }, { id: 'reports', label: 'Raporlar' }];

  it('sekme düğmesi ve açılır liste aynı bölümleri sunar; ikisi de değişikliği bildirir', () => {
    const change = vi.fn();
    render(<SectionSwitcher sections={sections} value="users" label="Yönetim" onChange={change} />);
    const tabs = [...host.querySelectorAll('[role=tab]')] as HTMLButtonElement[];
    expect(tabs.map((t) => t.textContent)).toEqual(['Kullanıcılar', 'Raporlar']);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    act(() => tabs[1].click());
    expect(change).toHaveBeenLastCalledWith('reports');

    const select = host.querySelector('select') as HTMLSelectElement;
    expect([...select.options].map((o) => o.textContent)).toEqual(['Yönetim', 'Kullanıcılar', 'Raporlar']);
    expect(select.value).toBe('');                       // kutu menü adını gösterir, geçerli bölümü değil
    expect(select.getAttribute('aria-label')).toBe('Yönetim');
    expect(select.options[0].disabled).toBe(true);
    const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!;
    act(() => { setter.call(select, 'users'); select.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(change).toHaveBeenLastCalledWith('users');
  });
});
