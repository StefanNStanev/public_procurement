import type { Facets, Filters, MultiKey } from '../api/types';

interface Props {
  filters: Filters;
  facets: Facets | null;
  toggle: (key: MultiKey, value: string) => void;
  clearKey: (key: 'q' | 'deadlineFrom' | 'deadlineTo' | 'minValue' | 'maxValue') => void;
  clearAll: () => void;
}

const FACET_OF: Record<MultiKey, keyof Facets> = {
  contractTypes: 'contractTypes',
  procedures: 'procedures',
  divisions: 'divisions',
  municipalities: 'municipalities',
  countries: 'countries',
  cities: 'cities',
};

export function ActiveFilters({ filters, facets, toggle, clearKey, clearAll }: Props) {
  const chips: { id: string; label: string; onRemove: () => void }[] = [];

  (Object.keys(FACET_OF) as MultiKey[]).forEach((key) => {
    const list = (facets?.[FACET_OF[key]] ?? []) as { key: string; label: string }[];
    for (const value of filters[key] ?? []) {
      const found = list.find((x) => x.key === value);
      let label = found?.label ?? value;
      if (key === 'divisions') label = `${value} ${found ? found.label : ''}`.trim();
      if (key === 'cities' && !found) label = value.split('|')[1] ?? value;
      chips.push({ id: `${key}:${value}`, label, onRemove: () => toggle(key, value) });
    }
  });
  if (filters.q) chips.push({ id: 'q', label: `„${filters.q}“`, onRemove: () => clearKey('q') });
  if (filters.deadlineFrom) chips.push({ id: 'df', label: `срок от ${filters.deadlineFrom.split('-').reverse().join('.')}`, onRemove: () => clearKey('deadlineFrom') });
  if (filters.deadlineTo) chips.push({ id: 'dt', label: `срок до ${filters.deadlineTo.split('-').reverse().join('.')}`, onRemove: () => clearKey('deadlineTo') });
  if (filters.minValue) chips.push({ id: 'min', label: `над ${Number(filters.minValue).toLocaleString('bg-BG')} €`, onRemove: () => clearKey('minValue') });
  if (filters.maxValue) chips.push({ id: 'max', label: `под ${Number(filters.maxValue).toLocaleString('bg-BG')} €`, onRemove: () => clearKey('maxValue') });

  if (chips.length === 0) return null;

  return (
    <div className="chips" aria-label="Приложени филтри">
      {chips.map((c) => (
        <button key={c.id} type="button" className="chip" onClick={c.onRemove} title="Премахни филтъра">
          <span>{c.label}</span>
          <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><path d="M2 2l6 6M8 2 2 8" stroke="currentColor" strokeWidth="1.5" /></svg>
        </button>
      ))}
      {chips.length > 1 && (
        <button type="button" className="link-button" onClick={clearAll}>
          Изчисти всички
        </button>
      )}
    </div>
  );
}
