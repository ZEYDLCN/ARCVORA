import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import { go } from '../../components/AppsShell';
import { generatePassword, roleLabels, roleOptions } from '../../lib/roles';
import type { AccountingSettings, GroupInfo, Member } from '../../types';
import { errorText, type Ctx } from './common';
import { DemoData } from './DemoData';
import { EmailAdmin } from './EmailAdmin';
import { RequestList } from './MyRequests';

type Tab = 'users' | 'groups' | 'accounting' | 'requests' | 'email' | 'demo';
const tabs: { id: Tab; label: string }[] = [{ id: 'users', label: 'Kullanıcılar' }, { id: 'groups', label: 'Gruplar' }, { id: 'requests', label: 'Tüm talepler' }, { id: 'accounting', label: 'Muhasebe ayarları' }, { id: 'email', label: 'E-posta' }, { id: 'demo', label: 'Demo veri' }];

export function Admin({ sub, workspace, user }: Ctx & { sub: string }) {
  const tab = (tabs.some((t) => t.id === sub) ? sub : 'users') as Tab;
  return (
    <>
      <div className="page-head">
        <h1>Yönetim</h1>
        <p className="tagline">Kullanıcıları, gruplarını ve muhasebe aktarımını yönetin. Uygulama bazlı yetkiler “Uygulama tasarla” içindeki Yetkiler sekmesindedir.</p>
        <div className="tabs" style={{ marginTop: 10 }}><div className="seg" role="tablist">
          {tabs.map((t) => <button key={t.id} role="tab" aria-selected={tab === t.id} onClick={() => go({ name: 'admin', sub: t.id })}>{t.label}</button>)}
        </div></div>
      </div>
      <div className="page-body">
        {tab === 'users' && <Users ws={workspace.id} me={user.id} />}
        {tab === 'groups' && <Groups ws={workspace.id} />}
        {tab === 'requests' && <RequestList workspaceId={workspace.id} all />}
        {tab === 'accounting' && <AccountingSettingsForm ws={workspace.id} />}
        {tab === 'email' && <EmailAdmin ws={workspace.id} />}
        {tab === 'demo' && <DemoData ws={workspace.id} />}
      </div>
    </>
  );
}

// ------------------------------------------------------------------------------ kullanıcılar

function Users({ ws, me }: { ws: string; me: string }) {
  const [members, setMembers] = useState<Member[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<{ email: string; password: string; reset?: boolean } | null>(null);
  const [form, setForm] = useState({ email: '', displayName: '', password: generatePassword(), role: 'app_user', managerId: '', invite: false });
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => { api.members(ws).then(setMembers).catch((e) => setError(errorText(e, 'Kullanıcılar yüklenemedi.'))); }, [ws]);
  useEffect(load, [load]);

  const guard = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try { await fn(); load(); } catch (e) { setError(errorText(e)); load(); } finally { setBusy(false); }
  };

  const create = (e: React.FormEvent) => {
    e.preventDefault();
    void guard(async () => {
      await api.createUser(ws, { email: form.email, displayName: form.displayName, password: form.password, role: form.role, managerId: form.managerId || undefined, sendInvite: form.invite || undefined });
      setCreated({ email: form.email, password: form.password });
      setForm({ email: '', displayName: '', password: generatePassword(), role: 'app_user', managerId: '', invite: false });
    });
  };

  return (
    <>
      {error && <div className="notice error" role="alert" style={{ marginBottom: 10 }}>{error}</div>}
      {created && (
        <div className="notice" role="status" style={{ marginBottom: 12 }}>
          <b>{created.reset ? 'Parola sıfırlandı.' : 'Kullanıcı oluşturuldu.'}</b> Geçici parolayı güvenli bir yolla iletin; kullanıcı ilk girişte değiştirecek.
          <div className="mono" style={{ marginTop: 6 }}>{created.email} · {created.password}</div>
        </div>
      )}
      <form className="card" style={{ maxWidth: 900, marginBottom: 16 }} onSubmit={create}>
        <h3>Yeni kullanıcı</h3>
        <div className="mini-grid">
          <div><label htmlFor="u-name">Ad soyad</label><input id="u-name" type="text" required maxLength={200} value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} /></div>
          <div><label htmlFor="u-mail">E-posta</label><input id="u-mail" type="email" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></div>
          <div>
            <label htmlFor="u-pass">Geçici parola</label>
            <input id="u-pass" type="text" required minLength={10} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
          </div>
          <div>
            <label htmlFor="u-role">Rol</label>
            <select id="u-role" value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
              {roleOptions.map((r) => <option key={r} value={r}>{roleLabels[r]}</option>)}
            </select>
          </div>
          <div>
            <label htmlFor="u-mgr">Doğrudan yönetici</label>
            <select id="u-mgr" value={form.managerId} onChange={(e) => setForm({ ...form, managerId: e.target.value })}>
              <option value="">Yok</option>
              {members?.map((m) => <option key={m.userId} value={m.userId}>{m.displayName}</option>)}
            </select>
          </div>
        </div>
        <div className="check-row" style={{ marginTop: 10 }}>
          <input id="u-invite" type="checkbox" checked={form.invite} onChange={(e) => setForm({ ...form, invite: e.target.checked })} />
          <label htmlFor="u-invite" style={{ margin: 0 }}>Davet e-postası gönder <span className="muted">(giriş adresini bildirir; parola e-postayla gönderilmez, ayrıca iletmelisiniz)</span></label>
        </div>
        <p className="row" style={{ marginTop: 12 }}>
          <button className="btn primary" disabled={busy}>Kullanıcıyı oluştur</button>
          <button type="button" className="btn" onClick={() => setForm({ ...form, password: generatePassword() })}>Parola üret</button>
        </p>
      </form>

      {members === null && !error && <p className="muted">Yükleniyor…</p>}
      {members && (
        <div className="card table-wrap">
          <table>
            <thead><tr><th>Ad</th><th>E-posta</th><th>Rol</th><th>Doğrudan yönetici</th><th></th></tr></thead>
            <tbody>
              {members.map((m) => (
                <tr key={m.userId}>
                  <td>{m.displayName}{m.userId === me && <span className="badge neutral" style={{ marginLeft: 6 }}>siz</span>}{m.mustChangePassword && <span className="badge warn" style={{ marginLeft: 6 }} title="Kullanıcı henüz parolasını değiştirmedi">geçici parola</span>}</td>
                  <td>{m.email}</td>
                  <td>
                    <select aria-label={`${m.displayName} rolü`} value={m.role} disabled={busy} onChange={(e) => void guard(() => api.updateMember(ws, m.userId, { role: e.target.value }))}>
                      {roleOptions.map((r) => <option key={r} value={r}>{roleLabels[r]}</option>)}
                    </select>
                  </td>
                  <td>
                    <select aria-label={`${m.displayName} yöneticisi`} value={m.managerId ?? ''} disabled={busy}
                      onChange={(e) => void guard(() => api.updateMember(ws, m.userId, e.target.value ? { managerId: e.target.value } : { clearManager: true }))}>
                      <option value="">Yok</option>
                      {members.filter((x) => x.userId !== m.userId).map((x) => <option key={x.userId} value={x.userId}>{x.displayName}</option>)}
                    </select>
                  </td>
                  <td>
                    <div className="actions">
                      {m.passwordResettable && (
                        <button className="btn small" disabled={busy} title="Yeni bir geçici parola üretir; kullanıcı ilk girişte değiştirir"
                          onClick={() => {
                            if (!window.confirm(`${m.displayName} için yeni bir geçici parola üretilsin mi? Mevcut parolası geçersiz olur.`)) return;
                            const password = generatePassword();
                            void guard(async () => { await api.resetPassword(ws, m.userId, password); setCreated({ email: m.email, password, reset: true }); });
                          }}>Parola sıfırla</button>
                      )}
                      {m.userId !== me && (
                        <button className="btn small danger" disabled={busy} onClick={() => { if (window.confirm(`${m.displayName} çalışma alanından çıkarılsın mı? Bekleyen onay görevleri master admin’e devredilir.`)) void guard(() => api.removeMember(ws, m.userId)); }}>Çıkar</button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

// ---------------------------------------------------------------------------------- gruplar

function Groups({ ws }: { ws: string }) {
  const [groups, setGroups] = useState<GroupInfo[] | null>(null);
  const [members, setMembers] = useState<Member[]>([]);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    api.groups(ws).then(setGroups).catch((e) => setError(errorText(e, 'Gruplar yüklenemedi.')));
    api.members(ws).then(setMembers).catch(() => setMembers([]));
  }, [ws]);
  useEffect(load, [load]);

  const guard = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try { await fn(); load(); } catch (e) { setError(errorText(e)); } finally { setBusy(false); }
  };

  return (
    <>
      {error && <div className="notice error" role="alert" style={{ marginBottom: 10 }}>{error}</div>}
      <form className="card inline-form" style={{ maxWidth: 700, marginBottom: 16 }} onSubmit={(e) => { e.preventDefault(); void guard(async () => { await api.createGroup(ws, name); setName(''); }); }}>
        <div><label htmlFor="g-name">Yeni grup adı</label><input id="g-name" type="text" required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} placeholder="Örn. Muhasebe, Genel müdür" /></div>
        <button className="btn primary" disabled={busy || !name.trim()}>Grup oluştur</button>
      </form>
      {groups === null && !error && <p className="muted">Yükleniyor…</p>}
      {groups?.length === 0 && <div className="empty-state"><b>Henüz grup yok</b><span>Gruplar, onaylayıcı ve muhasebe ekibi olarak kullanılır.</span></div>}
      {groups?.map((g) => <GroupCard key={g.id} group={g} members={members} busy={busy} onSave={(userIds) => guard(() => api.setGroupMembers(ws, g.id, userIds))}
        onRename={(n) => guard(() => api.renameGroup(ws, g.id, n))} onDelete={() => guard(() => api.deleteGroup(ws, g.id))} />)}
    </>
  );
}

function GroupCard({ group, members, busy, onSave, onRename, onDelete }: {
  group: GroupInfo; members: Member[]; busy: boolean; onSave: (ids: string[]) => Promise<void>; onRename: (n: string) => Promise<void>; onDelete: () => Promise<void>;
}) {
  const [picked, setPicked] = useState<string[]>(group.memberIds);
  const [name, setName] = useState(group.name);
  useEffect(() => { setPicked(group.memberIds); setName(group.name); }, [group]);
  const dirty = picked.length !== group.memberIds.length || picked.some((p) => !group.memberIds.includes(p));

  return (
    <div className="card" style={{ maxWidth: 700, marginBottom: 12 }}>
      <div className="inline-form">
        <div><label htmlFor={`gn-${group.id}`}>Ad</label><input id={`gn-${group.id}`} type="text" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} /></div>
        <button className="btn" disabled={busy || !name.trim() || name === group.name} onClick={() => void onRename(name)}>Adı kaydet</button>
        <button className="btn danger" disabled={busy} onClick={() => { if (window.confirm(`“${group.name}” grubu silinsin mi?`)) void onDelete(); }}>Sil</button>
      </div>
      <h3 style={{ marginTop: 14 }}>Üyeler ({picked.length})</h3>
      <div className="person-pick" role="group" aria-label={`${group.name} üyeleri`}>
        {members.map((m) => (
          <label key={m.userId}>
            <input type="checkbox" checked={picked.includes(m.userId)} onChange={(e) => setPicked(e.target.checked ? [...picked, m.userId] : picked.filter((x) => x !== m.userId))} />
            {m.displayName} <span className="muted">{m.email}</span>
          </label>
        ))}
      </div>
      <p style={{ marginTop: 10 }}><button className="btn primary" disabled={busy || !dirty} onClick={() => void onSave(picked)}>Üyeleri kaydet</button></p>
    </div>
  );
}

// ------------------------------------------------------------------------ muhasebe ayarları

function AccountingSettingsForm({ ws }: { ws: string }) {
  const [settings, setSettings] = useState<AccountingSettings | null>(null);
  const [groups, setGroups] = useState<GroupInfo[]>([]);
  const [url, setUrl] = useState('');
  const [secret, setSecret] = useState('');
  const [group, setGroup] = useState('');
  const [clearSecret, setClearSecret] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);

  const apply = (s: AccountingSettings) => { setSettings(s); setUrl(s.webhookUrl ?? ''); setGroup(s.accountingGroupId ?? ''); setSecret(''); setClearSecret(false); };
  useEffect(() => {
    api.accountingSettings(ws).then(apply).catch((e) => setError(errorText(e, 'Ayarlar yüklenemedi.')));
    api.groups(ws).then(setGroups).catch(() => setGroups([]));
  }, [ws]);

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setSaved(false);
    const body: Record<string, unknown> = {};
    if (url.trim() === '' && settings?.webhookUrl) body.clearWebhook = true; else if (url.trim() !== (settings?.webhookUrl ?? '')) body.webhookUrl = url.trim();
    if (clearSecret) body.clearSecret = true; else if (secret) body.webhookSecret = secret;
    if (group === '' && settings?.accountingGroupId) body.clearGroup = true; else if (group && group !== settings?.accountingGroupId) body.accountingGroupId = group;
    try { apply(await api.updateAccountingSettings(ws, body)); setSaved(true); } catch (err) { setError(errorText(err)); } finally { setBusy(false); }
  };

  if (!settings && !error) return <p className="muted">Yükleniyor…</p>;
  return (
    <form className="card" style={{ maxWidth: 720 }} onSubmit={save}>
      {error && <div className="notice error" role="alert">{error}</div>}
      {saved && <div className="notice" role="status">Ayarlar kaydedildi.</div>}
      <label htmlFor="a-group">Muhasebe grubu</label>
      <select id="a-group" value={group} onChange={(e) => setGroup(e.target.value)}>
        <option value="">Yok (yalnızca master admin)</option>
        {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
      </select>
      <div className="help muted">Bu grubun üyeleri muhasebe kuyruğunu görür, kayıtları işler ve CSV indirir.</div>

      <label htmlFor="a-url">Webhook adresi <span className="muted">(isteğe bağlı)</span></label>
      <input id="a-url" type="text" inputMode="url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://muhasebe.sirket.local/hook" />
      <div className="help muted">Onaylanan her talep bu adrese JSON olarak POST edilir. Boş bırakırsanız kayıtlar kuyrukta bekler ve elle işlenir.</div>

      <label htmlFor="a-secret">Gizli anahtar {settings?.hasSecret && <span className="badge ok">tanımlı</span>}</label>
      <input id="a-secret" type="password" autoComplete="new-password" minLength={16} value={secret} disabled={clearSecret} onChange={(e) => setSecret(e.target.value)}
        placeholder={settings?.hasSecret ? 'Değiştirmek için yeni anahtar yazın' : 'En az 16 karakter'} />
      <div className="help muted">İstek gövdesi bu anahtarla HMAC-SHA256 imzalanır (<span className="mono">X-Arcvora-Signature</span>). Anahtar bir daha görüntülenmez.</div>
      {settings?.hasSecret && <div className="check-row"><input id="a-clear" type="checkbox" checked={clearSecret} onChange={(e) => setClearSecret(e.target.checked)} /><label htmlFor="a-clear" style={{ margin: 0 }}>Gizli anahtarı kaldır</label></div>}
      <p style={{ marginTop: 16 }}><button className="btn primary" disabled={busy}>Kaydet</button></p>
    </form>
  );
}
