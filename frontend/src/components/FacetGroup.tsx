import { useEffect, useId, useMemo, useState } from 'react';
import type { FacetValue } from '../api/types';
import { formatCount } from '../lib/format';

interface Props {
  title: string;
  values: FacetValue[];
  selected: string[];
  onToggle: (key: string) => void;
  /** Показва поле за търсене при повече от толкова стойности. */
  searchAbove?: number;
  initialVisible?: number;
  showKey?: boolean;
  hint?: string;
  defaultOpen?: boolean;
}

export function FacetGroup({
  title, values, selected, onToggle, searchAbove = 12, initialVisible = 8, showKey, hint, defaultOpen = true,
}: Props) {
  const [open, setOpen] = useState(defaultOpen);
  const [query, setQuery] = useState('');
  const [expanded, setExpanded] = useState(false);
  const id = useId();

  // Отваря се автоматично, когато стане релевантна (напр. „Предмет“ след избор на насока)
  useEffect(() => {
    if (defaultOpen) setOpen(true);
  }, [defaultOpen]);

  const items = useMemo(() => {
    // Избраните стойности винаги стоят най-горе, дори ако вече нямат резултати
    const byKey = new Map(values.map((v) => [v.key, v]));
    const pinned = selected.map((k) => byKey.get(k) ?? { key: k, label: k, count: 0 });
    const rest = values.filter((v) => !selected.includes(v.key));
    const q = query.trim().toLowerCase();
    const filtered = q
      ? rest.filter((v) => v.label.toLowerCase().includes(q) || v.key.toLowerCase().startsWith(q) || v.group?.toLowerCase().includes(q))
      : rest;
    return { pinned, rest: filtered };
  }, [values, selected, query]);

  const visible = expanded || query ? items.rest : items.rest.slice(0, Math.max(0, initialVisible - items.pinned.length));
  const hidden = items.rest.length - visible.length;

  return (
    <section className="facet">
      <h3>
        <button type="button" className="facet-toggle" aria-expanded={open} aria-controls={id} onClick={() => setOpen(!open)}>
          <span>{title}</span>
          {selected.length > 0 && <span className="facet-badge">{selected.length}</span>}
          <svg className="chev" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M3 4.5 6 7.5 9 4.5" fill="none" stroke="currentColor" strokeWidth="1.5" /></svg>
        </button>
      </h3>
      {/* Тялото е винаги в DOM-а и се отваря/затваря плавно (grid-template-rows 0fr → 1fr) */}
      <div id={id} className={`facet-collapse ${open ? 'is-open' : ''}`} inert={!open}>
        <div className="facet-collapse-inner">
          <div className="facet-body">
          {values.length > searchAbove && (
            <input
              className="facet-search"
              type="search"
              placeholder={`Търсене в ${title.toLowerCase()}`}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          )}
          {hint && <p className="facet-hint">{hint}</p>}
          <ul>
            {[...items.pinned, ...visible].map((v) => (
              <li key={v.key}>
                <label className={selected.includes(v.key) ? 'is-on' : undefined}>
                  <input type="checkbox" checked={selected.includes(v.key)} onChange={() => onToggle(v.key)} />
                  <span className="facet-label">
                    {showKey && <span className="facet-code">{v.key}</span>}
                    {v.label}
                    {v.group && <small>{v.group}</small>}
                  </span>
                  <span className="facet-count">{formatCount(v.count)}</span>
                </label>
              </li>
            ))}
          </ul>
          {items.pinned.length + visible.length === 0 && <p className="facet-hint">Няма съвпадения.</p>}
          {!query && hidden > 0 && (
            <button type="button" className="link-button" onClick={() => setExpanded(true)}>
              Покажи още {hidden}
            </button>
          )}
          {!query && expanded && items.rest.length > initialVisible && (
            <button type="button" className="link-button" onClick={() => setExpanded(false)}>
              Покажи по-малко
            </button>
          )}
        </div>
        </div>
      </div>
    </section>
  );
}
