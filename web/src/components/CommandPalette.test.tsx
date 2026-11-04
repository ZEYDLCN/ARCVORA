// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CommandPalette, type PaletteItem } from './CommandPalette';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLDivElement;
let root: Root;

const picked: string[] = [];
const items: PaletteItem[] = [
  { id: 'a', label: 'Mimari', group: 'Bölümler', onSelect: () => picked.push('a') },
  { id: 'b', label: 'Gereksinimler', group: 'Bölümler', onSelect: () => picked.push('b') },
  { id: 'c', label: 'Uygulama API', hint: 'CMP-002', group: 'Nesneler', onSelect: () => picked.push('c') },
];

const render = (open: boolean, onClose: () => void = () => {}) =>
  act(() => { root.render(<CommandPalette open={open} onClose={onClose} items={items} placeholder="Ara…" />); });
const key = (target: EventTarget, k: string) => act(() => { target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true })); });
const options = () => [...host.querySelectorAll('[role="option"]')] as HTMLElement[];
const input = () => host.querySelector('input[role="combobox"]') as HTMLInputElement;
const type = (value: string) => act(() => {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input(), value);
  input().dispatchEvent(new Event('input', { bubbles: true }));
});

beforeEach(() => {
  vi.useFakeTimers();
  picked.length = 0;
  host = document.createElement('div');
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); });

describe('CommandPalette', () => {
  it('renders nothing while closed and a labelled modal dialog when open', () => {
    render(false);
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    render(true);
    const dialog = host.querySelector('[role="dialog"]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.getAttribute('aria-label')).toBe('Ara');
    expect(input().getAttribute('role')).toBe('combobox');
  });

  it('lists items grouped, with the first option selected', () => {
    render(true);
    expect(options().map((o) => o.textContent)).toEqual(['Mimari', 'Gereksinimler', 'Uygulama APICMP-002']);
    expect(host.querySelectorAll('.palette-group')).toHaveLength(2);
    expect(options()[0].getAttribute('aria-selected')).toBe('true');
  });

  it('moves the selection with the arrow keys and stops at both ends', () => {
    render(true);
    const dialog = host.querySelector('[role="dialog"]')!;
    key(dialog, 'ArrowUp');
    expect(options()[0].getAttribute('aria-selected')).toBe('true');
    key(dialog, 'ArrowDown'); key(dialog, 'ArrowDown'); key(dialog, 'ArrowDown'); key(dialog, 'ArrowDown');
    expect(options()[2].getAttribute('aria-selected')).toBe('true');
    expect(input().getAttribute('aria-activedescendant')).toBe('pi-c');
  });

  it('filters while typing, shows "Sonuç yok" for no match and resets the selection', () => {
    render(true);
    type('api');
    expect(options().map((o) => o.id)).toEqual(['pi-c']);
    type('zzzz');
    expect(host.querySelector('.palette-empty')?.textContent).toBe('Sonuç yok');
    expect(input().getAttribute('aria-activedescendant')).toBeNull();
  });

  it('Enter closes first, then runs the selected item', () => {
    const order: string[] = [];
    const list: PaletteItem[] = [{ id: 'x', label: 'Tek', group: 'g', onSelect: () => order.push('select') }];
    act(() => { root.render(<CommandPalette open onClose={() => order.push('close')} items={list} placeholder="" />); });
    key(host.querySelector('[role="dialog"]')!, 'Enter');
    expect(order).toEqual(['close', 'select']);
  });

  it('Enter with no results does nothing', () => {
    const onClose = vi.fn();
    render(true, onClose);
    type('zzzz');
    key(host.querySelector('[role="dialog"]')!, 'Enter');
    expect(onClose).not.toHaveBeenCalled();
    expect(picked).toEqual([]);
  });
});
