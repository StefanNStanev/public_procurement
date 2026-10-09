import type { Phase, PrerequisiteCategory, RoadmapResult } from '../api/types';

const CATEGORY: Record<PrerequisiteCategory, string> = {
  registration: 'Регистрации и подпис',
  eligibility: 'Лично състояние',
  economic: 'Икономическо и финансово състояние',
  technical: 'Технически и професионални способности',
  guarantee: 'Гаранции и застраховки',
  other: 'Други',
};
const CATEGORY_ORDER: PrerequisiteCategory[] = ['registration', 'eligibility', 'economic', 'technical', 'guarantee', 'other'];

const PHASE: Record<Phase, string> = {
  preparation: 'Подготовка на офертата',
  submission: 'Подаване',
  evaluation: 'Оценка и класиране',
  contract: 'Сключване на договор',
  execution: 'Изпълнение',
  completion: 'Приемане и приключване',
};
const PHASE_ORDER: Phase[] = ['preparation', 'submission', 'evaluation', 'contract', 'execution', 'completion'];

const RESPONSIBLE = { participant: 'Вие', buyer: 'Възложителят', both: 'Двете страни' } as const;

/** Резултат от анализа „Стъпки за изпълнение“: предпоставки, ключови дати и стъпки по фази. */
export function RoadmapView({ result: r }: { result: RoadmapResult }) {
  const groups = CATEGORY_ORDER
    .map((c) => ({ category: c, items: r.prerequisites.filter((p) => p.category === c) }))
    .filter((g) => g.items.length > 0);

  // Номерацията на стъпките е сквозна през всички фази
  let n = 0;
  const phases = PHASE_ORDER
    .map((phase) => ({ phase, steps: r.steps.filter((s) => s.phase === phase).map((s) => ({ ...s, n: ++n })) }))
    .filter((p) => p.steps.length > 0);

  return (
    <div className="estimate-result roadmap">
      <h4 className="estimate-title">Стъпки за изпълнение на поръчката</h4>
      <p className="estimate-summary">{r.summary}</p>

      {r.keyDates.length > 0 && (
        <ul className="key-dates" aria-label="Ключови срокове">
          {r.keyDates.map((d, i) => (
            <li key={i}>
              <span className="key-date-label">{d.label}</span>
              <span className="key-date-value">{d.date ?? 'не е посочен'}</span>
              {d.note && <span className="key-date-note">{d.note}</span>}
            </li>
          ))}
        </ul>
      )}

      <section className="prereq">
        <h5>Преди подаване на офертата</h5>
        <p className="prose">{r.prerequisitesOverview}</p>
        {groups.map((g) => (
          <div key={g.category} className="prereq-group">
            <h6>{CATEGORY[g.category]}</h6>
            <ul>
              {g.items.map((p, i) => (
                <li key={i}>
                  <span className={`req-badge ${p.mandatory ? 'req-must' : 'req-nice'}`}>
                    {p.mandatory ? 'задължително' : 'препоръчително'}
                  </span>
                  <div>
                    <strong>{p.title}</strong>
                    <p>{p.description}</p>
                    {(p.leadTime || p.source) && (
                      <p className="prereq-meta">
                        {p.leadTime && <span>Време за набавяне: {p.leadTime}</span>}
                        {p.source && <span>Източник: {p.source}</span>}
                      </p>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </section>

      <h5>Стъпки</h5>
      <ol className="phases">
        {phases.map((p, pi) => (
          <li key={p.phase} className="phase">
            <div className="phase-head">
              <span className="phase-num">{pi + 1}</span>
              <span>{PHASE[p.phase]}</span>
            </div>
            <ol className="steps">
              {p.steps.map((s) => (
                <li key={s.n} className="step">
                  <span className="step-num">{s.n}</span>
                  <div className="step-body">
                    <strong>{s.title}</strong>
                    <p className="step-meta">
                      <span className={`who who-${s.responsible}`}>{RESPONSIBLE[s.responsible]}</span>
                      {s.timing && <span>{s.timing}</span>}
                    </p>
                    <p>{s.description}</p>
                    {s.documents.length > 0 && (
                      <ul className="doc-chips" aria-label="Документи">
                        {s.documents.map((d, i) => <li key={i}>{d}</li>)}
                      </ul>
                    )}
                    {s.tip && <p className="step-tip"><strong>Съвет:</strong> {s.tip}</p>}
                  </div>
                </li>
              ))}
            </ol>
          </li>
        ))}
      </ol>

      {r.commonPitfalls.length > 0 && (
        <section className="pitfalls">
          <h5>Чести грешки, водещи до отстраняване</h5>
          <ul className="plain-list">{r.commonPitfalls.map((x, i) => <li key={i}>{x}</li>)}</ul>
        </section>
      )}

      {r.missingInformation.length > 0 && (
        <>
          <h5>Липсваща информация в документацията</h5>
          <ul className="plain-list">{r.missingInformation.map((x, i) => <li key={i}>{x}</li>)}</ul>
        </>
      )}
    </div>
  );
}
