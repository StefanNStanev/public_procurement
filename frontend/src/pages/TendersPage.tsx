import { useEffect, useState } from 'react';
import type { Source } from '../api/types';
import { ActiveFilters } from '../components/ActiveFilters';
import { FilterPanel } from '../components/FilterPanel';
import { Pagination } from '../components/Pagination';
import { TenderRow } from '../components/TenderRow';
import { formatCount } from '../lib/format';
import { useFilters } from '../lib/useFilters';
import { useTenderSearch } from '../lib/useTenderSearch';

const COPY: Record<Source, { heading: string; searchHint: string; empty: string }> = {
  bg: {
    heading: 'Активни поръчки в България',
    searchHint: 'Предмет, възложител или номер на поръчка',
    empty: 'Още няма заредени поръчки от ЦАИС ЕОП. Първата синхронизация започва няколко секунди след старта на API-то.',
  },
  eu: {
    heading: 'Активни поръчки в Европа',
    searchHint: 'Предмет, възложител или номер в TED',
    empty: 'Още няма заредени обявления от TED. Първото пълно зареждане отнема няколко минути.',
  },
};

export function TendersPage({ source }: { source: Source }) {
  const { filters, toggle, set, clearKey, clearAll } = useFilters();
  const { list, facets, loading, error } = useTenderSearch(source, filters);
  const [q, setQ] = useState(filters.q ?? '');
  const [drawer, setDrawer] = useState(false);
  const copy = COPY[source];

  // Синхронизира полето за търсене при навигация назад/напред
  useEffect(() => setQ(filters.q ?? ''), [filters.q]);

  // Текстовото търсене се прилага с малко закъснение
  useEffect(() => {
    if ((filters.q ?? '') === q) return;
    const t = setTimeout(() => set('q', q.trim() || undefined), 350);
    return () => clearTimeout(t);
  }, [q, filters.q, set]);

  const page = Number(filters.page ?? '1');
  const total = list?.total ?? 0;
  const anyFilter = Object.keys(filters).some((k) => k !== 'sort' && k !== 'page');

  return (
    <div className="page">
      <aside className={`sidebar ${drawer ? 'is-open' : ''}`} aria-label="Филтри">
        <div className="sidebar-head">
          <h2>Филтри</h2>
          <button type="button" className="link-button only-mobile" onClick={() => setDrawer(false)}>Готово</button>
        </div>
        <FilterPanel source={source} facets={facets} filters={filters} toggle={toggle} set={set} />
      </aside>

      <main className="results" aria-busy={loading}>
        <h1 className="results-heading">{copy.heading}</h1>

        <div className="toolbar">
          <input
            className="search"
            type="search"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder={copy.searchHint}
            aria-label="Търсене"
          />
          <button type="button" className="button only-mobile" onClick={() => setDrawer(true)}>Филтри</button>
        </div>

        <ActiveFilters filters={filters} facets={facets} toggle={toggle} clearKey={clearKey} clearAll={clearAll} />

        <div className="results-meta">
          <p>
            {list ? <><strong>{formatCount(total)}</strong> {total === 1 ? 'поръчка' : 'поръчки'}</> : ' '}
            {loading && <span className="loading-dot" aria-label="Зареждане" />}
          </p>
          <ul className="legend" aria-label="Цветове по вид поръчка">
            <li><span className="swatch ct-goods" />Доставки</li>
            <li><span className="swatch ct-services" />Услуги</li>
            <li><span className="swatch ct-works" />Строителство</li>
          </ul>
          <label className="sort">
            <span>Подреди по</span>
            <select value={filters.sort ?? 'deadline'} onChange={(e) => set('sort', e.target.value === 'deadline' ? undefined : e.target.value)}>
              <option value="deadline">най-близък срок</option>
              <option value="published">най-нови</option>
              <option value="value">най-висока стойност</option>
            </select>
          </label>
        </div>

        {error && <p className="notice notice-error">{error}</p>}

        {list && list.items.length > 0 && (
          <ul className="rows">
            {list.items.map((t) => <TenderRow key={t.id} t={t} source={source} />)}
          </ul>
        )}

        {list && list.items.length === 0 && !error && (
          <div className="empty">
            {anyFilter ? (
              <>
                <p>Няма активни поръчки с тези филтри.</p>
                <button type="button" className="button" onClick={clearAll}>Изчисти филтрите</button>
              </>
            ) : (
              <p>{copy.empty}</p>
            )}
          </div>
        )}

        {list && (
          <Pagination
            page={page}
            pageSize={list.pageSize}
            total={total}
            onPage={(p) => {
              set('page', p === 1 ? undefined : String(p));
              window.scrollTo({ top: 0 });
            }}
          />
        )}
      </main>
    </div>
  );
}
