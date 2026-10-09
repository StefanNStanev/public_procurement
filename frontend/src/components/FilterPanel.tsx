import type { Facets, Filters, MultiKey, Source } from '../api/types';
import { FacetGroup } from './FacetGroup';

interface Props {
  source: Source;
  facets: Facets | null;
  filters: Filters;
  toggle: (key: MultiKey, value: string) => void;
  set: (key: 'deadlineFrom' | 'deadlineTo' | 'minValue' | 'maxValue', value: string | undefined) => void;
}

export function FilterPanel({ source, facets, filters, toggle, set }: Props) {
  const f = facets;
  const sel = (k: MultiKey) => filters[k] ?? [];
  const hasDivision = sel('divisions').length > 0;

  return (
    <div className="filters">
      <FacetGroup title="Вид поръчка" values={f?.contractTypes ?? []} selected={sel('contractTypes')} onToggle={(v) => toggle('contractTypes', v)} />
      <FacetGroup title="Вид процедура" values={f?.procedures ?? []} selected={sel('procedures')} onToggle={(v) => toggle('procedures', v)} initialVisible={6} />
      <FacetGroup title="Насока" values={f?.divisions ?? []} selected={sel('divisions')} onToggle={(v) => toggle('divisions', v)} showKey initialVisible={10} />
      <FacetGroup
        title="Предмет"
        values={f?.cpv ?? []}
        selected={sel('cpv')}
        onToggle={(v) => toggle('cpv', v)}
        showKey
        searchAbove={6}
        hint={hasDivision ? undefined : 'Най-честите CPV кодове. Изберете насока, за да видите всички в нея.'}
        defaultOpen={hasDivision || sel('cpv').length > 0}
      />

      {source === 'bg' ? (
        <FacetGroup
          title="Община"
          values={f?.municipalities ?? []}
          selected={sel('municipalities')}
          onToggle={(v) => toggle('municipalities', v)}
          searchAbove={0}
          initialVisible={8}
        />
      ) : (
        <>
          <FacetGroup title="Държава" values={f?.countries ?? []} selected={sel('countries')} onToggle={(v) => toggle('countries', v)} searchAbove={0} />
          <FacetGroup
            title="Град"
            values={f?.cities ?? []}
            selected={sel('cities')}
            onToggle={(v) => toggle('cities', v)}
            searchAbove={0}
            hint={sel('countries').length ? undefined : 'Най-активните градове. Изберете държава, за да видите повече.'}
          />
        </>
      )}

      <section className="facet">
        <h3 className="facet-static">Краен срок за оферти</h3>
        <div className="range">
          <label>
            <span>от</span>
            <input type="date" value={filters.deadlineFrom ?? ''} onChange={(e) => set('deadlineFrom', e.target.value || undefined)} />
          </label>
          <label>
            <span>до</span>
            <input type="date" value={filters.deadlineTo ?? ''} onChange={(e) => set('deadlineTo', e.target.value || undefined)} />
          </label>
        </div>
      </section>

      <section className="facet">
        <h3 className="facet-static">Прогнозна стойност, €</h3>
        <div className="range">
          <label>
            <span>от</span>
            <input type="number" min={0} step={1000} inputMode="numeric" placeholder="0" value={filters.minValue ?? ''} onChange={(e) => set('minValue', e.target.value || undefined)} />
          </label>
          <label>
            <span>до</span>
            <input type="number" min={0} step={1000} inputMode="numeric" placeholder="без лимит" value={filters.maxValue ?? ''} onChange={(e) => set('maxValue', e.target.value || undefined)} />
          </label>
        </div>
        <p className="facet-hint">Поръчки без посочена стойност не се показват при този филтър.</p>
      </section>
    </div>
  );
}
