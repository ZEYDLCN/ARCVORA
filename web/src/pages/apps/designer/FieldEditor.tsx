import { appIconKeys } from '../../../components/AppIcon';
import { fieldTypeLabels, type FieldType, type FlowDefinition, type FlowField } from '../../../lib/flow';

interface Props {
  field: FlowField;
  index: number;
  total: number;
  dates: FlowField[];          // başka tarih alanları (notBefore / hesaplama için)
  lists?: { id: string; name: string }[];      // yönetilen listeler (seçim alanının seçenek kaynağı)
  errors: string[];
  onChange: (patch: Partial<FlowField>) => void;
  onMove: (delta: -1 | 1) => void;
  onRemove: () => void;
}

const optionsText = (f: FlowField) => (f.options ?? []).join('\n');

export function FieldEditor({ field: f, index, total, dates, lists = [], errors, onChange, onMove, onRemove }: Props) {
  const id = `f${index}`;
  const isDate = f.type === 'date';
  const canCompute = f.type === 'number' && dates.length >= 2;
  const clean = (v: string) => (v === '' ? undefined : Number(v));

  return (
    <div className="row-card" data-testid={`field-${f.key}`}>
      <div className="row-head">
        <b>{f.label || 'Adsız alan'}</b>
        <span className="badge neutral mono-small" title="Sunucuya gönderilen alan anahtarı">{f.key}</span>
        <button className="icon-btn" onClick={() => onMove(-1)} disabled={index === 0} aria-label="Yukarı taşı">↑</button>
        <button className="icon-btn" onClick={() => onMove(1)} disabled={index === total - 1} aria-label="Aşağı taşı">↓</button>
        <button className="btn small danger" onClick={onRemove}>Sil</button>
      </div>
      <div className="mini-grid">
        <div><label htmlFor={`${id}-l`}>Etiket</label><input id={`${id}-l`} type="text" maxLength={100} value={f.label} onChange={(e) => onChange({ label: e.target.value })} /></div>
        <div>
          <label htmlFor={`${id}-t`}>Tür</label>
          <select id={`${id}-t`} value={f.type} onChange={(e) => onChange({ type: e.target.value as FieldType, options: undefined, computed: undefined, notBefore: undefined, min: undefined, max: undefined, maxLength: undefined, maxFiles: undefined, listId: undefined })}>
            {(Object.keys(fieldTypeLabels) as FieldType[]).map((t) => <option key={t} value={t}>{fieldTypeLabels[t]}</option>)}
          </select>
        </div>
        <div className="check-row" style={{ alignSelf: 'end' }}>
          <input id={`${id}-r`} type="checkbox" checked={!!f.required} disabled={!!f.computed} onChange={(e) => onChange({ required: e.target.checked })} />
          <label htmlFor={`${id}-r`} style={{ margin: 0 }}>Zorunlu</label>
        </div>
      </div>

      {(f.type === 'select' || f.type === 'multi_select') && (
        <>
          <div><label htmlFor={`${id}-src`}>Seçenek kaynağı</label>
            <select id={`${id}-src`} value={f.listId ?? ''} onChange={(e) => onChange(e.target.value ? { listId: e.target.value, options: undefined } : { listId: undefined })}>
              <option value="">Elle gireceğim</option>
              {lists.map((l) => <option key={l.id} value={l.id}>Liste: {l.name}</option>)}
              {f.listId && !lists.some((l) => l.id === f.listId) && <option value={f.listId}>(bilinmeyen liste)</option>}
            </select></div>
          {!f.listId && (
            <div><label htmlFor={`${id}-o`}>Seçenekler <span className="muted">(her satıra bir tane)</span></label>
              <textarea id={`${id}-o`} rows={4} value={optionsText(f)} onChange={(e) => onChange({ options: e.target.value.split('\n') })} /></div>
          )}
          {f.listId && <p className="muted">Seçenekler “Yönetim → Listeler” sayfasından yönetilir; talep gönderilirken listenin o anki aktif öğeleri geçerlidir.</p>}
        </>
      )}
      {(f.type === 'number' || f.type === 'money') && !f.computed && (
        <div className="mini-grid">
          <div><label htmlFor={`${id}-mn`}>En küçük</label><input id={`${id}-mn`} type="number" step="any" value={f.min ?? ''} onChange={(e) => onChange({ min: clean(e.target.value) })} /></div>
          <div><label htmlFor={`${id}-mx`}>En büyük</label><input id={`${id}-mx`} type="number" step="any" value={f.max ?? ''} onChange={(e) => onChange({ max: clean(e.target.value) })} /></div>
          {f.type === 'money' && <div><label htmlFor={`${id}-c`}>Para birimi</label><input id={`${id}-c`} type="text" maxLength={3} value={f.currency ?? 'TRY'} onChange={(e) => onChange({ currency: e.target.value.toUpperCase() })} /></div>}
        </div>
      )}
      {(f.type === 'text' || f.type === 'long_text') && (
        <div className="mini-grid"><div><label htmlFor={`${id}-ml`}>En fazla karakter</label><input id={`${id}-ml`} type="number" min={1} max={4000} value={f.maxLength ?? ''} onChange={(e) => onChange({ maxLength: clean(e.target.value) })} /></div></div>
      )}
      {f.type === 'file' && (
        <div className="mini-grid"><div>
          <label htmlFor={`${id}-mf`}>En fazla dosya sayısı</label>
          <input id={`${id}-mf`} type="number" min={1} max={10} value={f.maxFiles ?? ''} placeholder="3" onChange={(e) => onChange({ maxFiles: clean(e.target.value) })} />
        </div></div>
      )}
      {isDate && dates.length > 0 && (
        <div className="mini-grid"><div>
          <label htmlFor={`${id}-nb`}>Şu tarihten önce olamaz</label>
          <select id={`${id}-nb`} value={f.notBefore ?? ''} onChange={(e) => onChange({ notBefore: e.target.value || undefined })}>
            <option value="">—</option>
            {dates.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}
          </select>
        </div></div>
      )}
      {canCompute && (
        <div>
          <div className="check-row">
            <input id={`${id}-cp`} type="checkbox" checked={!!f.computed} onChange={(e) => onChange(e.target.checked ? { computed: { op: 'calendar_days', from: dates[0].key, to: dates[1].key }, required: false } : { computed: undefined })} />
            <label htmlFor={`${id}-cp`} style={{ margin: 0 }}>İki tarihten otomatik hesapla (ör. gün sayısı)</label>
          </div>
        </div>
      )}
      {f.computed && (
        <div className="mini-grid">
          <div><label htmlFor={`${id}-op`}>Hesap</label>
            <select id={`${id}-op`} value={f.computed.op} onChange={(e) => onChange({ computed: { ...f.computed!, op: e.target.value as 'calendar_days' | 'weekdays' } })}>
              <option value="calendar_days">Takvim günü (uçlar dahil)</option><option value="weekdays">Hafta içi günü</option>
            </select></div>
          <div><label htmlFor={`${id}-fr`}>Başlangıç</label>
            <select id={`${id}-fr`} value={f.computed.from} onChange={(e) => onChange({ computed: { ...f.computed!, from: e.target.value } })}>
              {dates.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}</select></div>
          <div><label htmlFor={`${id}-to`}>Bitiş</label>
            <select id={`${id}-to`} value={f.computed.to} onChange={(e) => onChange({ computed: { ...f.computed!, to: e.target.value } })}>
              {dates.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}</select></div>
        </div>
      )}
      <div><label htmlFor={`${id}-h`}>Yardım metni <span className="muted">(isteğe bağlı)</span></label><input id={`${id}-h`} type="text" maxLength={200} value={f.helpText ?? ''} onChange={(e) => onChange({ helpText: e.target.value || undefined })} /></div>
      {errors.length > 0 && <ul className="issues" role="alert" style={{ color: 'var(--red)' }}>{errors.map((m) => <li key={m}>{m}</li>)}</ul>}
    </div>
  );
}

export const iconKeys = appIconKeys;
export type { FlowDefinition };
