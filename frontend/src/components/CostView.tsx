import type { EstimateResult, Severity, TenderListItem } from '../api/types';

const eur = new Intl.NumberFormat('bg-BG', { maximumFractionDigits: 0 });
const money = (n: number | null | undefined) => (n == null ? '—' : `${eur.format(n)} €`);
const pct = (n: number) => `${n.toLocaleString('bg-BG', { maximumFractionDigits: 1 })}%`;

const SEVERITY: Record<Severity, string> = { low: 'нисък', medium: 'среден', high: 'висок' };
const CONFIDENCE: Record<Severity, string> = { low: 'ниска', medium: 'средна', high: 'висока' };

/** Резултат от анализа „Разходи и печалба“. */
export function CostView({ result: r, tender }: { result: EstimateResult; tender: TenderListItem }) {
  const budget = r.estimatedValueFromDocs ?? tender.valueEur;
  const belowBudget = budget && r.recommendedBid.price ? (1 - r.recommendedBid.price / budget) * 100 : null;

  return (
    <div className="estimate-result">
      <h4 className="estimate-title">Оценка на разходите и печалбата</h4>

      <div className="figures">
        <Figure label="Прогнозна стойност" value={money(budget)} note={budget ? 'по документацията' : 'не е посочена'} />
        <Figure label="Себестойност" value={money(r.totalCost)} note={`от ${money(r.costRange.low)} до ${money(r.costRange.high)}`} />
        <Figure
          label="Препоръчителна оферта"
          value={money(r.recommendedBid.price)}
          note={belowBudget != null ? (belowBudget >= 0 ? `${pct(belowBudget)} под прогнозната` : `${pct(-belowBudget)} над прогнозната`) : undefined}
        />
        <Figure label="Печалба" value={money(r.recommendedBid.profit)} note={`марж ${pct(r.recommendedBid.marginPercent)}`} accent={r.recommendedBid.profit < 0 ? 'neg' : 'pos'} />
      </div>

      <p className="estimate-summary">{r.summary}</p>
      <p className={`confidence c-${r.confidence}`}>
        <strong>Увереност: {CONFIDENCE[r.confidence]}.</strong> {r.confidenceReason}
      </p>

      <dl className="estimate-terms">
        {r.awardCriterion && <div><dt>Критерий за възлагане</dt><dd>{r.awardCriterion}</dd></div>}
        {r.executionPeriod && <div><dt>Срок за изпълнение</dt><dd>{r.executionPeriod}</dd></div>}
        {r.paymentTerms && <div><dt>Плащане</dt><dd>{r.paymentTerms}</dd></div>}
      </dl>

      <h5>Разходи по пера</h5>
      <div className="table-wrap">
        <table className="cost-table">
          <thead>
            <tr><th>Перо</th><th>Какво включва</th><th className="num">Сума</th></tr>
          </thead>
          <tbody>
            {r.costBreakdown.map((c, i) => (
              <tr key={i}>
                <td>{c.category}</td>
                <td>{c.description}<small>{c.basis}</small></td>
                <td className="num">{money(c.amount)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr><td colSpan={2}>Общо себестойност</td><td className="num">{money(r.totalCost)}</td></tr>
          </tfoot>
        </table>
      </div>

      <h5>Защо тази цена</h5>
      <p className="prose">{r.recommendedBid.rationale}</p>

      {r.lots.length > 0 && (
        <>
          <h5>По обособени позиции</h5>
          <div className="table-wrap">
            <table className="cost-table">
              <thead>
                <tr><th>Позиция</th><th className="num">Прогнозна</th><th className="num">Себестойност</th><th className="num">Оферта</th><th className="num">Печалба</th></tr>
              </thead>
              <tbody>
                {r.lots.map((l, i) => (
                  <tr key={i}>
                    <td>{l.name}</td>
                    <td className="num">{money(l.estimatedValue)}</td>
                    <td className="num">{money(l.totalCost)}</td>
                    <td className="num">{money(l.recommendedBid)}</td>
                    <td className={`num ${l.profit < 0 ? 'neg' : ''}`}>{money(l.profit)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {r.risks.length > 0 && (
        <>
          <h5>Рискове</h5>
          <ul className="risk-list">
            {[...r.risks].sort((a, b) => rank(b.severity) - rank(a.severity)).map((x, i) => (
              <li key={i}>
                <span className={`sev sev-${x.severity}`}>{SEVERITY[x.severity]}</span>
                <div><strong>{x.title}</strong><p>{x.description}</p></div>
              </li>
            ))}
          </ul>
        </>
      )}

      <div className="estimate-columns">
        {r.requirements.length > 0 && <NoteList title="Ключови изисквания" items={r.requirements} />}
        {r.penalties.length > 0 && <NoteList title="Неустойки" items={r.penalties} />}
      </div>

      {(r.assumptions.length > 0 || r.missingInformation.length > 0) && (
        <div className="estimate-columns">
          {r.assumptions.length > 0 && (
            <div><h5>Допускания</h5><ul className="plain-list">{r.assumptions.map((a, i) => <li key={i}>{a}</li>)}</ul></div>
          )}
          {r.missingInformation.length > 0 && (
            <div><h5>Липсваща информация</h5><ul className="plain-list">{r.missingInformation.map((a, i) => <li key={i}>{a}</li>)}</ul></div>
          )}
        </div>
      )}

    </div>
  );
}

function Figure({ label, value, note, accent }: { label: string; value: string; note?: string; accent?: 'pos' | 'neg' }) {
  return (
    <div className={`figure ${accent ? `figure-${accent}` : ''}`}>
      <span className="figure-label">{label}</span>
      <span className="figure-value">{value}</span>
      {note && <span className="figure-note">{note}</span>}
    </div>
  );
}

function NoteList({ title, items }: { title: string; items: { title: string; description: string }[] }) {
  return (
    <div>
      <h5>{title}</h5>
      <ul className="note-list">
        {items.map((x, i) => <li key={i}><strong>{x.title}</strong> {x.description}</li>)}
      </ul>
    </div>
  );
}

const rank = (s: Severity) => (s === 'high' ? 3 : s === 'medium' ? 2 : 1);
