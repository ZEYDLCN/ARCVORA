import { describe, expect, it } from 'vitest';
import { withDependencies, withoutDependents } from './groups';

const deps = { req: [], data: ['req'], api: ['req', 'data'], flow: ['req'], oq: ['req'] };

describe('group selection', () => {
  it('selecting a group pulls in everything it depends on, transitively', () => {
    expect([...withDependencies(deps, new Set(), 'api')].sort()).toEqual(['api', 'data', 'req']);
  });

  it('deselecting a group drops everything that depends on it', () => {
    const all = new Set(['req', 'data', 'api', 'flow', 'oq']);
    expect([...withoutDependents(deps, all, 'data')].sort()).toEqual(['flow', 'oq', 'req']);
    expect([...withoutDependents(deps, all, 'req')]).toEqual([]);
  });

  it('keeps unrelated groups', () => {
    const sel = new Set(['req', 'flow']);
    expect([...withoutDependents(deps, sel, 'flow')]).toEqual(['req']);
  });

  it('is stable for already satisfied selections', () => {
    const sel = new Set(['req', 'data']);
    expect([...withDependencies(deps, sel, 'data')].sort()).toEqual(['data', 'req']);
  });
});
