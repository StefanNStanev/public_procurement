import { useEffect, useId, useState } from 'react';
import type { AnalysisKind, TenderListItem } from '../api/types';
import { AnalysisPanel } from './AnalysisPanel';

const TABS: { kind: AnalysisKind; label: string }[] = [
  { kind: 'cost', label: 'Разходи и печалба' },
  { kind: 'roadmap', label: 'Стъпки за изпълнение' },
];

interface Props {
  tender: TenderListItem;
  /** Анализ, поискан от бутон на реда; null след като е обработен. */
  request: AnalysisKind | null;
  onRequestHandled: () => void;
}

/** Двата AI анализа като раздели. И двата остават заредени, за да продължи следенето на текущ анализ. */
export function AiSection({ tender, request, onRequestHandled }: Props) {
  const [active, setActive] = useState<AnalysisKind>(request ?? 'cost');
  const id = useId();

  useEffect(() => {
    if (request) setActive(request);
  }, [request]);

  return (
    <section className="ai-section">
      <div className="ai-tabs" role="tablist" aria-label="AI анализи">
        {TABS.map((t) => (
          <button
            key={t.kind}
            type="button"
            role="tab"
            id={`${id}-${t.kind}-tab`}
            aria-controls={`${id}-${t.kind}`}
            aria-selected={active === t.kind}
            className="ai-tab"
            onClick={() => setActive(t.kind)}
          >
            {t.label}
          </button>
        ))}
      </div>
      {TABS.map((t) => (
        <div key={t.kind} id={`${id}-${t.kind}`} role="tabpanel" aria-labelledby={`${id}-${t.kind}-tab`} hidden={active !== t.kind}>
          <AnalysisPanel
            tender={tender}
            kind={t.kind}
            autoStart={request === t.kind}
            onStarted={onRequestHandled}
          />
        </div>
      ))}
    </section>
  );
}
