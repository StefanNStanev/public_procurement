import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { Filters, MultiKey } from '../api/types';

const MULTI: MultiKey[] = ['contractTypes', 'procedures', 'arrangements', 'divisions', 'municipalities', 'countries', 'cities'];
const SINGLE = ['q', 'deadlineFrom', 'deadlineTo', 'minValue', 'maxValue', 'sort', 'page'] as const;
type SingleKey = (typeof SINGLE)[number];

/** Филтрите се пазят в URL-а – резултатите могат да се споделят с линк. */
export function useFilters() {
  const [params, setParams] = useSearchParams();

  const filters = useMemo<Filters>(() => {
    const f: Filters = {};
    for (const k of MULTI) {
      const v = params.getAll(k);
      if (v.length) f[k] = v;
    }
    for (const k of SINGLE) {
      const v = params.get(k);
      if (v) (f as Record<SingleKey, string>)[k] = v;
    }
    return f;
  }, [params]);

  const update = useCallback(
    (mutate: (p: URLSearchParams) => void, resetPage = true) => {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          mutate(next);
          if (resetPage) next.delete('page');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  const toggle = useCallback(
    (key: MultiKey, value: string) =>
      update((p) => {
        const values = p.getAll(key);
        p.delete(key);
        const next = values.includes(value) ? values.filter((v) => v !== value) : [...values, value];
        next.forEach((v) => p.append(key, v));
        if (key === 'countries') {
          const cities = p.getAll('cities').filter((c) => next.length === 0 || next.includes(c.split('|')[0]));
          p.delete('cities');
          cities.forEach((c) => p.append('cities', c));
        }
      }),
    [update],
  );

  const set = useCallback(
    (key: SingleKey, value: string | undefined) =>
      update((p) => (value ? p.set(key, value) : p.delete(key)), key !== 'page'),
    [update],
  );

  const clearKey = useCallback((key: MultiKey | SingleKey) => update((p) => p.delete(key)), [update]);
  const clearAll = useCallback(() => update((p) => [...p.keys()].forEach((k) => k !== 'sort' && p.delete(k))), [update]);

  return { filters, toggle, set, clearKey, clearAll };
}
