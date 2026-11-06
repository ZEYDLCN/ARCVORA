import { useEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from '../../api';
import { go } from '../../components/AppsShell';
import { AppIcon } from '../../components/AppIcon';
import { DynamicForm } from '../../components/DynamicForm';
import { buildPayload, issuesByField, type FormValues } from '../../lib/flow';
import type { AppForm as AppFormData, DirectoryEntry, FlowIssue } from '../../types';
import { errorText, newKey, type Ctx } from './common';

export function AppForm({ id, workspace }: Ctx & { id: string }) {
  const [form, setForm] = useState<AppFormData | null>(null);
  const [people, setPeople] = useState<DirectoryEntry[]>([]);
  const [values, setValues] = useState<FormValues>({});
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // Çift tıklama/ağ tekrarı aynı talebi bir kez oluşturur (sunucuda Idempotency-Key).
  const key = useRef(newKey());

  useEffect(() => {
    api.appForm(id).then(setForm).catch((e) => setError(errorText(e, 'Form yüklenemedi.')));
    api.directory(workspace.id).then(setPeople).catch(() => setPeople([]));
  }, [id, workspace.id]);

  const uploads = useMemo(() => ({
    upload: async (file: File) => {
      const a = await api.uploadFile(id, file);
      return { id: a.id, fileName: a.fileName, size: a.size, contentType: a.contentType };
    },
    remove: (fileId: string) => api.deleteAttachment(fileId),
  }), [id]);

  // İzin bakiyesi tanımlı uygulamalarda kişinin güncel bakiyesi formun üstünde gösterilir.
  const [balance, setBalance] = useState<number | null>(null);
  const trackBalance = form?.definition.completion?.leave?.enabled === true;
  useEffect(() => { if (trackBalance) api.leaveMe(workspace.id).then((b) => setBalance(b.available)).catch(() => setBalance(null)); }, [trackBalance, workspace.id]);

  const steps = useMemo(() => form?.definition.steps ?? [], [form]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!form) return;
    setBusy(true);
    setError(null);
    setErrors({});
    try {
      const r = await api.submit(id, buildPayload(form.definition, values), key.current);
      go({ name: 'request', id: r.summary.id });
    } catch (err) {
      if (err instanceof ApiError && err.code === 'invalid_request' && Array.isArray(err.details)) {
        const grouped = issuesByField(err.details as FlowIssue[]);
        setErrors(grouped);
        setError('Formda düzeltilmesi gereken alanlar var.');
      } else setError(errorText(err));
      key.current = newKey();      // başarısız denemeden sonra yeni anahtar: düzeltilmiş form yeni istek sayılır
    } finally { setBusy(false); }
  };

  return (
    <>
      <div className="page-head">
        <p><button className="btn small" onClick={() => go({ name: 'apps' })}>← Uygulamalar</button></p>
        <div className="row"><AppIcon name={form?.app.icon ?? 'file'} /><h1>{form?.app.name ?? 'Yeni talep'}</h1></div>
        {form?.app.description && <p className="tagline">{form.app.description}</p>}
      </div>
      <div className="page-body">
        {error && !form && <div className="notice error" role="alert">{error}</div>}
        {!form && !error && <p className="muted">Yükleniyor…</p>}
        {form && (
          <form className="card" style={{ maxWidth: 820 }} onSubmit={submit} noValidate>
            {error && <div className="notice error" role="alert" style={{ marginBottom: 8 }}>{error}</div>}
            {errors._ && <div className="notice error" role="alert" style={{ marginBottom: 8 }}>{errors._.join(' ')}</div>}
            {trackBalance && balance !== null && <div className="notice" role="status" style={{ marginBottom: 8 }}>İçinde bulunduğumuz yıl izin bakiyeniz: <b>{String(balance).replace('.', ',')} gün</b></div>}
            <DynamicForm def={form.definition} values={values} errors={errors} people={people} disabled={busy} uploads={uploads} lists={form.lists} holidays={form.holidays}
              onChange={(k, v) => { setValues((s) => ({ ...s, [k]: v })); setErrors((s) => (s[k] ? { ...s, [k]: [] } : s)); }} />
            <h3 style={{ marginTop: 22 }}>Onay akışı</h3>
            <div className="step-strip" aria-label="Onay adımları">
              {steps.map((s, i) => (
                <span key={s.id} style={{ display: 'contents' }}>
                  {i > 0 && <span className="arrow" aria-hidden="true">→</span>}
                  <span className="step" title={s.conditions?.length ? 'Yalnızca koşul sağlanırsa çalışır' : undefined}>{s.name}{s.conditions?.length ? ' (koşullu)' : ''}</span>
                </span>
              ))}
            </div>
            <p style={{ marginTop: 14 }}><button className="btn primary" disabled={busy}>{busy ? 'Gönderiliyor…' : 'Talebi gönder'}</button></p>
          </form>
        )}
      </div>
    </>
  );
}
