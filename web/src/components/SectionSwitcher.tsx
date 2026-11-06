import { IconChevronDown, IconSliders } from './icons';

export interface Section { id: string; label: string }

/**
 * Bölüm seçici: geniş ekranda sekme çubuğu, telefonda simgeli açılır liste (referans tasarım: “Yönetim ⌄”).
 * İkisi de DOM'da durur, CSS biri gösterir; böylece klavye/ekran okuyucu ve testler her iki biçimde de çalışır.
 */
export function SectionSwitcher({ sections, value, onChange, label }: { sections: Section[]; value: string; onChange: (id: string) => void; label: string }) {
  return (
    <div className="tabs" style={{ marginTop: 10 }}>
      <div className="seg desktop-tabs" role="tablist" aria-label={label}>
        {sections.map((s) => <button key={s.id} role="tab" aria-selected={value === s.id} onClick={() => onChange(s.id)}>{s.label}</button>)}
      </div>
      <div className="tab-select">
        <span className="ts-ico" aria-hidden="true"><IconSliders size={18} /></span>
        {/* Değer boş tutulur: kutu bölüm adını değil menü adını (“Yönetim”) gösterir; sayfa başlığı zaten geçerli bölümü söyler. */}
        <select aria-label={label} value="" onChange={(e) => { if (e.target.value) onChange(e.target.value); }}>
          <option value="" disabled>{label}</option>
          {sections.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
        </select>
        <span className="ts-caret" aria-hidden="true"><IconChevronDown size={18} /></span>
      </div>
    </div>
  );
}
