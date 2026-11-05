import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../../api';
import { appIconKeys, AppIcon } from '../../components/AppIcon';
import { go } from '../../components/AppsShell';
import { DynamicForm } from '../../components/DynamicForm';
import { fieldTypeLabels, nextStepId, slugKey, type FieldType, type FlowDefinition, type FlowField, type FlowStep, type FormValues } from '../../lib/flow';
import { issuesByPath, validateFlow } from '../../lib/flowValidate';
import type { AppDetail, AppGrants, DirectoryEntry, FlowIssue, GroupInfo } from '../../types';
import { errorText, type Ctx } from './common';
import { FieldEditor } from './designer/FieldEditor';
import { StepEditor } from './designer/StepEditor';

const statusLabel = { draft: 'Taslak', published: 'Yayında', archived: 'Arşivde' } as const;
const statusTone = { draft: 'warn', published: 'ok', archived: 'neutral' } as const;

function newField(type: FieldType, key: string): FlowField {
  const label = fieldTypeLabels[type];
  const base: FlowField = { key, label, type };
  if (type === 'select' || type === 'multi_select') return { ...base, options: ['Seçenek 1', 'Seçenek 2'] };
  if (type === 'money') return { ...base, currency: 'TRY' };
  return base;
}

function move<T>(list: readonly T[], i: number, delta: -1 | 1): T[] {
  const j = i + delta;
  if (j < 0 || j >= list.length) return [...list];
  const copy = [...list];
  [copy[i], copy[j]] = [copy[j], copy[i]];
  return copy;
}

export function Designer({ id, workspace }: Ctx & { id: string }) {
  const ws = workspace.id;
  const [detail, setDetail] = useState<AppDetail | null>(null);
  const [draft, setDraft] = useState<FlowDefinition | null>(null);
  const [meta, setMeta] = useState({ name: '', description: '', icon: 'file' });
  const [dirty, setDirty] = useState(false);
  const [tab, setTab] = useState<'design' | 'access'>('design');
  const [people, setPeople] = useState<DirectoryEntry[]>([]);
  const [groups, setGroups] = useState<GroupInfo[]>([]);
  const [serverIssues, setServerIssues] = useState<FlowIssue[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<FormValues>({});
  const [addType, setAddType] = useState<FieldType>('text');

  const apply = useCallback((d: AppDetail) => {
    setDetail(d);
    setDraft(d.draft);
    setMeta({ name: d.summary.name, description: d.summary.description, icon: d.summary.icon });
    setServerIssues(d.issues);
    setDirty(false);
  }, []);

  const load = useCallback(() => { api.appDetail(id).then(apply).catch((e) => setError(errorText(e, 'Uygulama yüklenemedi.'))); }, [id, apply]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    api.directory(ws).then(setPeople).catch(() => setPeople([]));
    api.groups(ws).then(setGroups).catch(() => setGroups([]));
  }, [ws]);

  // Kaydedilmemiş değişiklik varken sekmeyi kapatmaya karşı uyarı.
  useEffect(() => {
    if (!dirty) return;
    const h = (e: BeforeUnloadEvent) => { e.preventDefault(); };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  const local = useMemo(() => (draft ? validateFlow(draft) : []), [draft]);
  const byPath = useMemo(() => issuesByPath(local), [local]);
  // Anlık doğrulamanın bilemeyeceği, yalnızca sunucunun bulabildiği sorunlar (ör. silinmiş grup); kaydedilmemiş taslakta eskimiş sayılır.
  const serverOnly = dirty ? [] : serverIssues.filter((i) => !local.some((l) => l.code === i.code && l.path === i.path));
  const allIssues = [...local, ...serverOnly];

  const update = (fn: (d: FlowDefinition) => FlowDefinition) => { setDraft((d) => (d ? fn(d) : d)); setDirty(true); setNotice(null); };
  const fieldErrors = (i: number) => [...byPath.entries()].filter(([p]) => p.startsWith(`fields[${i}]`)).flatMap(([, m]) => m);
  const stepErrors = (i: number) => [
    ...[...byPath.entries()].filter(([p]) => p.startsWith(`steps[${i}]`)).flatMap(([, m]) => m),
    ...serverOnly.filter((x) => x.path.startsWith(`steps[${i}]`)).map((x) => x.message),
  ];

  const save = async (): Promise<AppDetail | null> => {
    if (!detail || !draft) return null;
    setBusy(true);
    setError(null);
    try {
      const d = await api.updateApp(id, { baseRevision: detail.summary.revision, name: meta.name, description: meta.description, icon: meta.icon, definition: draft });
      apply(d);
      setNotice('Taslak kaydedildi.');
      return d;
    } catch (e) {
      setError(e instanceof ApiError && e.code === 'revision_conflict' ? 'Taslak başka bir yönetici tarafından değiştirilmiş. Sayfayı yenileyin; kendi değişikliklerinizi not alın.' : errorText(e));
      return null;
    } finally { setBusy(false); }
  };

  const publish = async () => {
    if (dirty && !(await save())) return;
    setBusy(true);
    setError(null);
    try {
      const d = await api.publishApp(id);
      apply(d);
      setNotice(`Yayınlandı: v${d.summary.versionNumber}. Yetkili çalışanlar artık kullanabilir.`);
    } catch (e) {
      if (e instanceof ApiError && e.code === 'invalid_definition' && Array.isArray(e.details)) setServerIssues(e.details as FlowIssue[]);
      setError(errorText(e));
    } finally { setBusy(false); }
  };

  const archive = async (archived: boolean) => {
    setBusy(true);
    try { await api.archiveApp(id, archived); load(); } catch (e) { setError(errorText(e)); } finally { setBusy(false); }
  };

  const remove = async () => {
    if (!window.confirm('Bu taslak kalıcı olarak silinsin mi?')) return;
    try { await api.deleteApp(id); go({ name: 'design' }); } catch (e) { setError(errorText(e)); }
  };

  if (error && !detail) return <div className="page-body"><div className="notice error" role="alert">{error}</div></div>;
  if (!detail || !draft) return <div className="page-body"><p className="muted">Yükleniyor…</p></div>;

  const s = detail.summary;
  const dateFields = (i: number) => draft.fields.filter((f, k) => f.type === 'date' && k !== i);
  const summaryPicked = draft.summaryFields;

  return (
    <>
      <div className="page-head">
        <p><button className="btn small" onClick={() => go({ name: 'design' })}>← Uygulamalar</button></p>
        <div className="row"><AppIcon name={meta.icon} /><h1>{meta.name || 'Adsız uygulama'}</h1></div>
        <div className="status-row" style={{ marginTop: 6 }}>
          <span className={`badge ${statusTone[s.status]}`}>{statusLabel[s.status]}</span>
          {s.versionNumber > 0 && <span className="badge neutral">Yayın v{s.versionNumber}</span>}
          {dirty ? <span className="badge warn">Kaydedilmemiş değişiklik</span> : s.hasDraftChanges && s.versionNumber > 0 ? <span className="badge warn">Yayınlanmamış değişiklik</span> : null}
        </div>
        <div className="tabs" style={{ marginTop: 10 }}><div className="seg" role="tablist">
          <button role="tab" aria-selected={tab === 'design'} onClick={() => setTab('design')}>Tasarım</button>
          <button role="tab" aria-selected={tab === 'access'} onClick={() => setTab('access')}>Yetkiler</button>
        </div></div>
      </div>

      <div className="page-body">
        {error && <div className="notice error" role="alert" style={{ marginBottom: 10 }}>{error} <button className="btn small" onClick={load}>Yenile</button></div>}
        {notice && <div className="notice" role="status" style={{ marginBottom: 10 }}>{notice}</div>}

        {tab === 'access' ? <Access appId={id} people={people} groups={groups} /> : (
          <div className="designer">
            <div>
              <div className="card">
                <h3>Genel</h3>
                <div className="mini-grid">
                  <div><label htmlFor="d-name">Ad</label><input id="d-name" type="text" maxLength={200} value={meta.name} onChange={(e) => { setMeta({ ...meta, name: e.target.value }); setDirty(true); }} /></div>
                  <div><label htmlFor="d-prefix">Talep numarası öneki</label>
                    <input id="d-prefix" type="text" maxLength={10} value={draft.prefix} onChange={(e) => update((d) => ({ ...d, prefix: e.target.value.toUpperCase() }))} aria-invalid={byPath.has('prefix') || undefined} />
                    {byPath.get('prefix')?.map((m) => <div className="err" key={m} role="alert">{m}</div>)}
                  </div>
                  <div><label htmlFor="d-icon">Simge</label>
                    <select id="d-icon" value={meta.icon} onChange={(e) => { setMeta({ ...meta, icon: e.target.value }); setDirty(true); }}>{appIconKeys.map((k) => <option key={k} value={k}>{k}</option>)}</select></div>
                </div>
                <label htmlFor="d-desc">Açıklama</label>
                <input id="d-desc" type="text" maxLength={1000} value={meta.description} onChange={(e) => { setMeta({ ...meta, description: e.target.value }); setDirty(true); }} />
                <label>Listelerde gösterilecek özet alanları <span className="muted">(en fazla 3)</span></label>
                <div className="multi">
                  {draft.fields.map((f) => (
                    <label key={f.key}>
                      <input type="checkbox" checked={summaryPicked.includes(f.key)} disabled={!summaryPicked.includes(f.key) && summaryPicked.length >= 3}
                        onChange={(e) => update((d) => ({ ...d, summaryFields: e.target.checked ? [...d.summaryFields, f.key] : d.summaryFields.filter((k) => k !== f.key) }))} />
                      {f.label}
                    </label>
                  ))}
                </div>
              </div>

              <div className="card" style={{ marginTop: 14 }}>
                <h3>Alanlar</h3>
                {draft.fields.length === 0 && <p className="muted">Talep formunda sorulacak alanları ekleyin.</p>}
                {draft.fields.map((f, i) => (
                  <FieldEditor key={f.key} field={f} index={i} total={draft.fields.length} dates={dateFields(i)} errors={fieldErrors(i)}
                    onChange={(patch) => update((d) => ({ ...d, fields: d.fields.map((x, k) => (k === i ? { ...x, ...patch } : x)) }))}
                    onMove={(delta) => update((d) => ({ ...d, fields: move(d.fields, i, delta) }))}
                    onRemove={() => update((d) => ({
                      ...d, fields: d.fields.filter((_, k) => k !== i).map((x) => (x.notBefore === f.key ? { ...x, notBefore: undefined } : x)),
                      summaryFields: d.summaryFields.filter((k) => k !== f.key),
                    }))} />
                ))}
                <div className="inline-form" style={{ marginTop: 12 }}>
                  <div><label htmlFor="add-type">Yeni alan türü</label>
                    <select id="add-type" value={addType} onChange={(e) => setAddType(e.target.value as FieldType)}>
                      {(Object.keys(fieldTypeLabels) as FieldType[]).map((t) => <option key={t} value={t}>{fieldTypeLabels[t]}</option>)}</select></div>
                  <button className="btn" onClick={() => update((d) => ({ ...d, fields: [...d.fields, newField(addType, slugKey(fieldTypeLabels[addType], d.fields.map((f) => f.key)))] }))}>Alan ekle</button>
                </div>
                {byPath.get('fields')?.map((m) => <div className="err" key={m} role="alert">{m}</div>)}
              </div>

              <div className="card" style={{ marginTop: 14 }}>
                <h3>Onay adımları</h3>
                <p className="muted" style={{ marginTop: 0 }}>Adımlar sırayla çalışır. Koşulu sağlanmayan adım atlanır; tüm adımlar atlanırsa talep otomatik onaylanır.</p>
                {draft.steps.map((st: FlowStep, i) => (
                  <StepEditor key={st.id} step={st} index={i} total={draft.steps.length} fields={draft.fields} people={people} groups={groups} errors={stepErrors(i)}
                    onChange={(patch) => update((d) => ({ ...d, steps: d.steps.map((x, k) => (k === i ? { ...x, ...patch } : x)) }))}
                    onMove={(delta) => update((d) => ({ ...d, steps: move(d.steps, i, delta) }))}
                    onRemove={() => update((d) => ({ ...d, steps: d.steps.filter((_, k) => k !== i) }))} />
                ))}
                <p style={{ marginTop: 12 }}><button className="btn" onClick={() => update((d) => ({ ...d, steps: [...d.steps, { id: nextStepId(d.steps), name: 'Yeni adım', approvers: { type: 'manager' } }] }))}>Adım ekle</button></p>
                {byPath.get('steps')?.map((m) => <div className="err" key={m} role="alert">{m}</div>)}
              </div>

              <div className="card" style={{ marginTop: 14 }}>
                <h3>Tamamlanma</h3>
                <div className="check-row">
                  <input id="acc-on" type="checkbox" checked={!!draft.completion?.accounting?.enabled}
                    onChange={(e) => update((d) => ({ ...d, completion: { accounting: { enabled: e.target.checked, category: d.completion?.accounting?.category ?? slugKey(meta.name) } } }))} />
                  <label htmlFor="acc-on" style={{ margin: 0 }}>Onaylanınca muhasebe kuyruğuna ekle</label>
                </div>
                {draft.completion?.accounting?.enabled && (
                  <div style={{ maxWidth: 320 }}>
                    <label htmlFor="acc-cat">Muhasebe kategorisi</label>
                    <input id="acc-cat" type="text" maxLength={40} value={draft.completion.accounting.category ?? ''}
                      onChange={(e) => update((d) => ({ ...d, completion: { accounting: { enabled: true, category: e.target.value } } }))} />
                    {byPath.get('completion.accounting.category')?.map((m) => <div className="err" key={m} role="alert">{m}</div>)}
                  </div>
                )}
              </div>
            </div>

            <div>
              <div className="card">
                <h3>Önizleme</h3>
                <p className="muted" style={{ marginTop: 0 }}>Talep eden formu böyle görür. (Önizleme gönderilmez.)</p>
                {draft.fields.length === 0 ? <p className="muted">Henüz alan yok.</p> : (
                  <DynamicForm def={draft} values={preview} people={people} onChange={(k, v) => setPreview((p) => ({ ...p, [k]: v }))} />
                )}
                <h3 style={{ marginTop: 18 }}>Onay akışı</h3>
                <div className="step-strip">
                  {draft.steps.map((st, i) => (
                    <span key={st.id} style={{ display: 'contents' }}>
                      {i > 0 && <span className="arrow" aria-hidden="true">→</span>}
                      <span className="step">{st.name}{st.conditions?.length ? ' (koşullu)' : ''}</span>
                    </span>
                  ))}
                  {draft.steps.length === 0 && <span className="muted">Adım yok</span>}
                </div>
              </div>
              <div className="card" style={{ marginTop: 14 }} aria-live="polite">
                <h3>Sorunlar</h3>
                {allIssues.length === 0 ? <span className="badge ok">✓ Yayınlanabilir</span> : (
                  <ul className="issues">{allIssues.map((i, k) => <li key={`${i.path}-${k}`}><span className="mono-small">{i.path}</span> — {i.message}</li>)}</ul>
                )}
              </div>
            </div>
          </div>
        )}

        <div className="sticky-actions">
          <button className="btn primary" disabled={busy || !dirty} onClick={() => void save()}>Taslağı kaydet</button>
          <button className="btn primary" disabled={busy || local.length > 0 || (!dirty && !s.hasDraftChanges && s.status === 'published')} onClick={() => void publish()}
            title={local.length > 0 ? 'Önce sorunları giderin' : undefined}>Yayınla</button>
          {s.status === 'archived'
            ? <button className="btn" disabled={busy} onClick={() => void archive(false)}>Arşivden çıkar</button>
            : s.versionNumber > 0 && <button className="btn" disabled={busy} onClick={() => void archive(true)}>Arşivle</button>}
          {s.versionNumber === 0 && <button className="btn danger" disabled={busy} onClick={() => void remove()}>Taslağı sil</button>}
        </div>
      </div>
    </>
  );
}

// ------------------------------------------------------------------------------ yetkiler

function Access({ appId, people, groups }: { appId: string; people: DirectoryEntry[]; groups: GroupInfo[] }) {
  const [grants, setGrants] = useState<AppGrants | null>(null);
  const [saved, setSaved] = useState<AppGrants | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [ok, setOk] = useState(false);

  useEffect(() => { api.appGrants(appId).then((g) => { setGrants(g); setSaved(g); }).catch((e) => setError(errorText(e, 'Yetkiler yüklenemedi.'))); }, [appId]);
  if (!grants) return error ? <div className="notice error" role="alert">{error}</div> : <p className="muted">Yükleniyor…</p>;

  const toggle = (key: 'userIds' | 'groupIds', id: string, on: boolean) => { setOk(false); setGrants({ ...grants, [key]: on ? [...grants[key], id] : grants[key].filter((x) => x !== id) }); };
  const dirty = JSON.stringify([...grants.userIds].sort()) !== JSON.stringify([...(saved?.userIds ?? [])].sort())
    || JSON.stringify([...grants.groupIds].sort()) !== JSON.stringify([...(saved?.groupIds ?? [])].sort());

  return (
    <div className="card" style={{ maxWidth: 760 }}>
      {error && <div className="notice error" role="alert">{error}</div>}
      {ok && <div className="notice" role="status">Yetkiler kaydedildi.</div>}
      <p className="muted" style={{ marginTop: 0 }}>Yalnızca burada seçilen kişi ve grupların üyeleri bu uygulamayı kullanabilir. Master admin her uygulamayı otomatik kullanır. Onaylayıcılar, uygulama yetkisi olmasa da kendilerine atanan talepleri görür.</p>
      <h3>Gruplar</h3>
      <div className="person-pick" role="group" aria-label="Yetkili gruplar">
        {groups.length === 0 && <span className="muted">Henüz grup yok (Yönetim → Gruplar).</span>}
        {groups.map((g) => <label key={g.id}><input type="checkbox" checked={grants.groupIds.includes(g.id)} onChange={(e) => toggle('groupIds', g.id, e.target.checked)} />{g.name} <span className="muted">({g.memberIds.length} üye)</span></label>)}
      </div>
      <h3 style={{ marginTop: 14 }}>Kişiler</h3>
      <div className="person-pick" role="group" aria-label="Yetkili kişiler">
        {people.map((p) => <label key={p.id}><input type="checkbox" checked={grants.userIds.includes(p.id)} onChange={(e) => toggle('userIds', p.id, e.target.checked)} />{p.displayName}</label>)}
      </div>
      <p style={{ marginTop: 12 }}>
        <button className="btn primary" disabled={busy || !dirty} onClick={async () => {
          setBusy(true); setError(null);
          try { const g = await api.setAppGrants(appId, grants); setGrants(g); setSaved(g); setOk(true); } catch (e) { setError(errorText(e)); } finally { setBusy(false); }
        }}>Yetkileri kaydet</button>
      </p>
    </div>
  );
}
