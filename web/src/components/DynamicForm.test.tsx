// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FlowDefinition } from '../lib/flow';
import { flowTemplates } from '../lib/templates';
import { DynamicForm } from './DynamicForm';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;
const leave = flowTemplates.find((t) => t.id === 'leave')!.definition;
const people = [{ id: 'u1', displayName: 'Ayşe' }, { id: 'u2', displayName: 'Burak' }];

const render = (props: Partial<React.ComponentProps<typeof DynamicForm>> = {}, def: FlowDefinition = leave) =>
  act(() => { root.render(<DynamicForm def={def} values={{}} onChange={() => {}} people={people} {...props} />); });
const control = (label: string) => {
  const l = [...host.querySelectorAll('label')].find((x) => x.textContent?.startsWith(label));
  if (!l) throw new Error(`etiket yok: ${label}`);
  return (l.htmlFor ? document.getElementById(l.htmlFor) : l.parentElement) as HTMLElement;
};
const setValue = (el: HTMLElement, value: string) => act(() => {
  const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto, 'value')!.set!.call(el, value);
  el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
});

beforeEach(() => { host = document.createElement('div'); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });

describe('DynamicForm', () => {
  it('her alan türü için uygun denetim çizer ve zorunlu alanı işaretler', () => {
    render();
    expect(control('İzin türü').tagName).toBe('SELECT');
    expect((control('Başlangıç tarihi') as HTMLInputElement).type).toBe('date');
    expect(control('Vekil').tagName).toBe('SELECT');
    expect(control('Açıklama').tagName).toBe('TEXTAREA');
    expect([...host.querySelectorAll('label')].find((l) => l.textContent?.startsWith('İzin türü'))!.textContent).toContain('*');
    expect([...host.querySelectorAll('label')].find((l) => l.textContent?.startsWith('Açıklama'))!.textContent).not.toContain('*');
    // kişi alanı çalışma alanı üyelerini listeler
    expect([...(control('Vekil') as HTMLSelectElement).options].map((o) => o.text)).toEqual(['Seçin…', 'Ayşe', 'Burak']);
  });

  it('değişiklikleri anahtar ve değerle bildirir', () => {
    const onChange = vi.fn();
    render({ onChange });
    setValue(control('İzin türü'), 'Rapor');
    setValue(control('Başlangıç tarihi'), '2026-11-10');
    expect(onChange).toHaveBeenCalledWith('kind', 'Rapor');
    expect(onChange).toHaveBeenCalledWith('start', '2026-11-10');
  });

  it('hesaplanan gün sayısını salt okunur ve canlı gösterir', () => {
    render({ values: { start: '2026-11-10', end: '2026-11-14' } });
    const days = control('Gün sayısı');
    expect(days.tagName).toBe('DIV');
    expect(days.textContent).toBe('5');
    render({ values: { start: '2026-11-14', end: '2026-11-10' } });
    expect(control('Gün sayısı').textContent).toBe('—');
  });

  it('bitiş tarihinin alt sınırı başlangıç tarihidir', () => {
    render({ values: { start: '2026-11-10' } });
    expect((control('Bitiş tarihi') as HTMLInputElement).min).toBe('2026-11-10');
  });

  it('sunucu hatalarını alanın altında erişilebilir şekilde gösterir', () => {
    render({ errors: { kind: ['İzin türü zorunlu.'] } });
    const select = control('İzin türü');
    expect(select.getAttribute('aria-invalid')).toBe('true');
    const err = document.getElementById(select.getAttribute('aria-describedby')!)!;      // useId() kimliklerinde ':' olur; CSS seçici yerine getElementById
    expect(err.textContent).toBe('İzin türü zorunlu.');
    expect(err.getAttribute('role')).toBe('alert');
  });

  it('devre dışı bırakılınca tüm denetimler kapanır', () => {
    render({ disabled: true });
    for (const el of host.querySelectorAll('input, select, textarea')) expect((el as HTMLInputElement).disabled).toBe(true);
  });

  it('çoklu seçim ve onay kutusu', () => {
    const def: FlowDefinition = {
      ...leave, fields: [
        { key: 'tags', label: 'Etiketler', type: 'multi_select', options: ['a', 'b'] },
        { key: 'ok', label: 'Kabul ediyorum', type: 'checkbox', required: true },
      ],
    };
    const onChange = vi.fn();
    render({ def, onChange, values: { tags: ['a'] } });
    const boxes = [...host.querySelectorAll('.multi input')] as HTMLInputElement[];
    expect(boxes.map((b) => b.checked)).toEqual([true, false]);
    act(() => boxes[1].click());
    expect(onChange).toHaveBeenCalledWith('tags', ['a', 'b']);
    const ok = control('Kabul ediyorum') as HTMLInputElement;
    expect(ok.type).toBe('checkbox');
    act(() => ok.click());
    expect(onChange).toHaveBeenCalledWith('ok', true);
  });

  it('etiketteki HTML metin olarak görünür, işaretleme olarak çalışmaz (XSS)', () => {
    const def: FlowDefinition = { ...leave, fields: [{ key: 'x', label: '<img src=x onerror=alert(1)>', type: 'text', helpText: '<b>kalın</b>' }] };
    render({ def });
    expect(host.querySelector('img')).toBeNull();
    expect(host.querySelector('b')).toBeNull();
    expect(host.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(host.textContent).toContain('<b>kalın</b>');
  });
});
