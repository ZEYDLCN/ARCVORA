import type { FlowDefinition, LeaveSpec } from '../../../lib/flow';

interface Props { def: FlowDefinition; issues: Map<string, string[]>; onChange: (leave: LeaveSpec) => void }

/**
 * İzin bakiyesi ayarı: talep gönderilince gün sayısı kişinin bakiyesinden ayrılır (yetersizse gönderilemez), reddedilirse/iptal edilirse iade edilir.
 * Tür alanı seçilirse yalnızca işaretli türler bakiyeden düşer (ör. mazeret izni düşmez).
 */
export function LeaveSettings({ def, issues, onChange }: Props) {
  const leave: LeaveSpec = def.completion?.leave ?? { enabled: false };
  const numbers = def.fields.filter((f) => f.type === 'number');
  const dates = def.fields.filter((f) => f.type === 'date');
  const selects = def.fields.filter((f) => f.type === 'select');
  const type = selects.find((f) => f.key === leave.typeField);
  const err = (path: string) => issues.get(`completion.leave.${path}`)?.map((m) => <div className="err" key={m} role="alert">{m}</div>);

  return (
    <div style={{ marginTop: 12 }}>
      <div className="check-row">
        <input id="lv-on" type="checkbox" checked={leave.enabled}
          onChange={(e) => onChange(e.target.checked ? { ...leave, enabled: true, daysField: leave.daysField ?? numbers[0]?.key, startField: leave.startField ?? dates[0]?.key } : { ...leave, enabled: false })} />
        <label htmlFor="lv-on" style={{ margin: 0 }}>İzin bakiyesinden düş <span className="muted">(gönderilince ayrılır, red/iptalde iade edilir)</span></label>
      </div>
      {leave.enabled && (
        <div className="mini-grid" style={{ marginTop: 8 }}>
          <div><label htmlFor="lv-days">Gün sayısı alanı</label>
            <select id="lv-days" value={leave.daysField ?? ''} onChange={(e) => onChange({ ...leave, daysField: e.target.value || undefined })}>
              <option value="">Seçin…</option>{numbers.map((f) => <option key={f.key} value={f.key}>{f.label}</option>)}</select>{err('daysField')}</div>
          <div><label htmlFor="lv-start">Başlangıç tarihi alanı</label>
            <select id="lv-start" value={leave.startField ?? ''} onChange={(e) => onChange({ ...leave, startField: e.target.value || undefined })}>
              <option value="">Seçin…</option>{dates.map((f) => <option key={f.key} value={f.key}>{f.label}</option>)}</select>{err('startField')}</div>
          <div><label htmlFor="lv-type">İzin türü alanı <span className="muted">(isteğe bağlı)</span></label>
            <select id="lv-type" value={leave.typeField ?? ''} onChange={(e) => onChange({ ...leave, typeField: e.target.value || undefined, countsFor: undefined })}>
              <option value="">Tüm talepler düşer</option>{selects.map((f) => <option key={f.key} value={f.key}>{f.label}</option>)}</select>{err('typeField')}</div>
          {type && !type.listId && (
            <div style={{ gridColumn: '1 / -1' }}>
              <div className="muted">Bakiyeden düşecek türler:</div>
              <div className="multi" role="group" aria-label="Bakiyeden düşecek türler">
                {(type.options ?? []).map((o) => (
                  <label key={o}><input type="checkbox" checked={(leave.countsFor ?? []).includes(o)}
                    onChange={(e) => onChange({ ...leave, countsFor: e.target.checked ? [...(leave.countsFor ?? []), o] : (leave.countsFor ?? []).filter((x) => x !== o) })} />{o}</label>
                ))}
              </div>
              {err('countsFor')}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
