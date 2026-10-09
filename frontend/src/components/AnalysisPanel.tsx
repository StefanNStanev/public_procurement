import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from '../api/client';
import type { AnalysisKind, Estimate, EstimateResult, RoadmapResult, TenderListItem } from '../api/types';
import { formatDate, formatTime } from '../lib/format';
import { CostView } from './CostView';
import { RoadmapView } from './RoadmapView';
import { SparkIcon } from './icons';

const COPY: Record<AnalysisKind, { button: string; note: string; disclaimer: string }> = {
  cost: {
    button: 'Пусни AI анализ на разходите и печалбата',
    note: 'Оценява себестойността, препоръчителната оферта и печалбата по документите в ЦАИС ЕОП. Пуска се само при натискане и струва около $0,10–2.',
    disclaimer: 'Ориентировъчна оценка от AI по пазарни цени, без данни за конкретна фирма. Проверете количествата и цените преди да подадете оферта.',
  },
  roadmap: {
    button: 'Пусни AI анализ на стъпките за изпълнение',
    note: 'Подготвя предпоставките и стъпките от подаването на офертата до приключването на договора по документите в ЦАИС ЕОП. Пуска се само при натискане и струва около $0,10–2.',
    disclaimer: 'Ориентировъчен план от AI по документацията и обичайната практика по ЗОП. Не е правна консултация – сверете сроковете и изискванията с обявлението и документацията.',
  },
};

interface Props {
  tender: TenderListItem;
  kind: AnalysisKind;
  /** true след натискане на бутона на реда – анализът започва без втори клик. */
  autoStart?: boolean;
  onStarted?: () => void;
}

/**
 * Един вид AI анализ по документите в ЦАИС ЕОП. Анализът се пуска САМО при натискане на бутон;
 * при отваряне на поръчката се показва единствено вече запазен резултат (безплатно).
 */
export function AnalysisPanel({ tender, kind, autoStart, onStarted }: Props) {
  const [estimate, setEstimate] = useState<Estimate | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const copy = COPY[kind];

  const busy = estimate?.status === 'pending' || estimate?.status === 'running';

  const load = useCallback(async () => {
    try {
      setEstimate(await api.estimate(tender.id, kind));
      setError(null);
    } catch (e) {
      setError(errorMessage(e, 'Анализът не се зареди.'));
    } finally {
      setLoaded(true);
    }
  }, [tender.id, kind]);

  useEffect(() => {
    load();
    return () => clearTimeout(timer.current);
  }, [load]);

  // Докато тече анализът, проверяваме състоянието на всеки 3 секунди
  useEffect(() => {
    if (!busy) return;
    timer.current = setTimeout(load, 3000);
    return () => clearTimeout(timer.current);
  }, [busy, estimate, load]);

  const start = async (force: boolean) => {
    setStarting(true);
    setError(null);
    try {
      setEstimate(await api.startEstimate(tender.id, kind, force));
    } catch (e) {
      setError(errorMessage(e, 'Анализът не можа да започне.'));
    } finally {
      setStarting(false);
    }
  };

  // Бутонът на реда: стартираме, ако за поръчката още няма такъв анализ
  useEffect(() => {
    if (!autoStart || !loaded) return;
    onStarted?.();
    if (!estimate) start(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoStart, loaded]);

  if (!loaded) return null;

  return (
    <div className="estimate" aria-live="polite">
      {!estimate && (
        <div className="estimate-start">
          <button type="button" className="button button-ai" disabled={starting} onClick={() => start(false)}>
            <SparkIcon />
            {starting ? 'Стартиране…' : copy.button}
          </button>
          <p className="muted">{copy.note}</p>
        </div>
      )}

      {busy && (
        <div className="estimate-progress">
          <span className="loading-dot" aria-hidden="true" />
          <span>{estimate?.stage ?? 'Анализът тече…'}</span>
        </div>
      )}

      {estimate?.status === 'failed' && (
        <div className="notice notice-error">
          <p>Анализът не успя: {estimate.error ?? 'неизвестна грешка'}</p>
          <button type="button" className="link-button" onClick={() => start(true)} disabled={starting}>Опитай отново</button>
        </div>
      )}

      {error && <p className="notice notice-error">{error}</p>}

      {estimate?.status === 'done' && estimate.result && (
        <>
          {kind === 'cost'
            ? <CostView result={estimate.result as EstimateResult} tender={tender} />
            : <RoadmapView result={estimate.result as RoadmapResult} />}
          <AnalysisFooter estimate={estimate} disclaimer={copy.disclaimer} onRerun={() => start(true)} rerunning={starting} />
        </>
      )}
    </div>
  );
}

function AnalysisFooter({ estimate, disclaimer, onRerun, rerunning }: {
  estimate: Estimate; disclaimer: string; onRerun: () => void; rerunning: boolean;
}) {
  return (
    <>
      {estimate.documents && estimate.documents.length > 0 && (
        <details className="doc-list">
          <summary>Документи ({estimate.documents.filter((d) => d.included).length} от {estimate.documents.length} са анализирани)</summary>
          <ul>
            {estimate.documents.map((d, i) => (
              <li key={i} className={d.included ? undefined : 'muted'}>
                <span>{d.name}</span>
                <span>{d.status}</span>
              </li>
            ))}
          </ul>
        </details>
      )}

      <footer className="estimate-footer">
        <p className="muted">
          {disclaimer}
          <br />
          Изготвен на {formatDate(estimate.finishedAt)} в {formatTime(estimate.finishedAt)} ч. с {estimate.model}
          {estimate.costUsd != null && <>, разход ${estimate.costUsd.toFixed(2)}</>}.
        </p>
        <button type="button" className="button" disabled={rerunning} onClick={onRerun}>Нов AI анализ</button>
      </footer>
    </>
  );
}
