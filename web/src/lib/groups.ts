/**
 * Kısmi kabul seçimi (§16.2). Sunucu bağımlılığı koparan seçimi zaten reddeder; burada kullanıcıya baştan
 * tutarlı bir seçim sunmak için bir grup açılınca bağımlı olduğu gruplar da açılır, kapatılınca ona bağlı gruplar kapanır.
 */
export type Deps = Record<string, string[]>;

export function withDependencies(deps: Deps, selected: Set<string>, toAdd: string): Set<string> {
  const next = new Set(selected);
  const stack = [toAdd];
  while (stack.length) {
    const g = stack.pop()!;
    if (next.has(g)) continue;
    next.add(g);
    (deps[g] ?? []).forEach((d) => stack.push(d));
  }
  return next;
}

export function withoutDependents(deps: Deps, selected: Set<string>, toRemove: string): Set<string> {
  const next = new Set(selected);
  const removed = new Set<string>([toRemove]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const g of next) {
      if (!removed.has(g) && (deps[g] ?? []).some((d) => removed.has(d))) {
        removed.add(g);
        changed = true;
      }
    }
  }
  removed.forEach((g) => next.delete(g));
  return next;
}
