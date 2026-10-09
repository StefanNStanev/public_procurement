import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { AnalysisKind, Source, TenderDetail, TenderListItem } from '../api/types';
import { countdown, formatDate, formatMoney, formatTime } from '../lib/format';
import { AiSection } from './AiSection';
import { SparkIcon } from './icons';

interface Props {
  t: TenderListItem;
  source: Source;
}

export function TenderRow({ t, source }: Props) {
  const [open, setOpen] = useState(false);
  const [detail, setDetail] = useState<TenderDetail | null>(null);
  const [failed, setFailed] = useState(false);
  const [fullText, setFullText] = useState(false);
  /** Натиснат е AI бутон на реда – съответният анализ започва веднага. */
  const [aiRequest, setAiRequest] = useState<AnalysisKind | null>(null);
  const requestAi = (kind: AnalysisKind) => {
    setAiRequest(kind);
    setOpen(true);
  };
  const cd = countdown(t.deadline);

  useEffect(() => {
    if (!open || detail) return;
    const ctrl = new AbortController();
    api.detail(source, t.id, ctrl.signal).then(setDetail).catch(() => !ctrl.signal.aborted && setFailed(true));
    return () => ctrl.abort();
  }, [open, detail, source, t.id]);

  const place = source === 'bg'
    ? t.municipality ?? t.city ?? 'Неопределена община'
    : [t.city, t.countryName].filter(Boolean).join(', ');
  const value = formatMoney(t.estimatedValue, t.currency);
  const description = detail?.description ?? '';
  const longText = description.length > 600;

  return (
    <li className={`row ct-${t.contractType ?? 'none'} ${open ? 'is-open' : ''}`}>
      <div className={`deadline u-${cd.urgency}`} title={t.deadline ? `Краен срок: ${formatDate(t.deadline)} ${formatTime(t.deadline)}` : 'Няма посочен краен срок'}>
        <span className="deadline-value">{cd.value}</span>
        <span className="deadline-unit">{cd.unit}</span>
      </div>

      <div className="row-main">
        <button type="button" className="row-title" aria-expanded={open} onClick={() => setOpen(!open)}>
          {t.title}
        </button>
        <p className="row-buyer">{t.buyerName ?? 'Възложителят не е посочен'}</p>
        <p className="row-tags">
          <span className="tag-place">{place}</span>
          {t.contractTypeLabel && <span className={`tag tag-${t.contractType}`}>{t.contractTypeLabel}</span>}
          {t.divisionLabel && <span className="tag tag-muted" title={t.cpvName ?? undefined}>{t.divisionLabel}</span>}
        </p>
      </div>

      <div className="row-value">
        {value ? <span>{value}</span> : <span className="muted">без стойност</span>}
        {source === 'bg' && (
          <div className="ai-buttons">
            <button type="button" className="ai-button" onClick={() => requestAi('cost')}
              title="Себестойност, препоръчителна оферта и печалба по документите в ЦАИС ЕОП">
              <SparkIcon />
              AI разходи
            </button>
            <button type="button" className="ai-button" onClick={() => requestAi('roadmap')}
              title="Предпоставки и стъпки от подаването на офертата до приключването на договора">
              <SparkIcon />
              AI стъпки
            </button>
          </div>
        )}
      </div>

      {open && (
        <div className="row-detail">
          <dl>
            <div><dt>Краен срок</dt><dd>{t.deadline ? `${formatDate(t.deadline)}, ${formatTime(t.deadline)} ч.` : 'не е посочен'}</dd></div>
            <div><dt>Публикувана</dt><dd>{formatDate(t.publishedAt)}</dd></div>
            <div><dt>Процедура</dt><dd>{t.procedureLabel ?? '—'}</dd></div>
            {source === 'bg'
              ? <div><dt>Област</dt><dd>{t.region ?? '—'}</dd></div>
              : <div><dt>Държава</dt><dd>{t.countryName ?? '—'}</dd></div>}
            {t.noticeNumber && <div><dt>{source === 'bg' ? 'Номер в ЦАИС ЕОП' : 'Номер в TED'}</dt><dd>{t.noticeNumber}</dd></div>}
          </dl>

          {failed && <p className="muted">Подробностите не се заредиха.</p>}
          {detail && (
            <>
              {description && (
                <div className={`row-description ${longText && !fullText ? 'is-clamped' : ''}`}>
                  {description.split(/\n+/).map((p, i) => <p key={i}>{p}</p>)}
                </div>
              )}
              {longText && (
                <button type="button" className="link-button" onClick={() => setFullText(!fullText)}>
                  {fullText ? 'Скрий' : 'Покажи цялото описание'}
                </button>
              )}
              {detail.cpvCodes.length > 0 && (
                <ul className="cpv-list">
                  {detail.cpvCodes.map((c) => (
                    <li key={c.code}><code>{c.code}</code> {c.name ?? ''}</li>
                  ))}
                </ul>
              )}
            </>
          )}

          <a className="source-link" href={t.url} target="_blank" rel="noreferrer">
            {source === 'bg' ? 'Отвори поръчката в ЦАИС ЕОП' : 'Отвори обявлението в TED'}
          </a>

          {source === 'bg' && <AiSection tender={t} request={aiRequest} onRequestHandled={() => setAiRequest(null)} />}
        </div>
      )}
    </li>
  );
}

