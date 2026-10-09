import { useEffect, useState } from 'react';
import { Link, NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { api } from './api/client';
import type { Source, SyncStatus } from './api/types';
import { formatCount, relativeTime } from './lib/format';
import { TendersPage } from './pages/TendersPage';

const SOURCE_NAME: Record<Source, string> = { bg: 'ЦАИС ЕОП', eu: 'TED' };

export default function App() {
  const { pathname } = useLocation();
  const current: Source = pathname.startsWith('/eu') ? 'eu' : 'bg';
  const [status, setStatus] = useState<SyncStatus[]>([]);

  useEffect(() => {
    const load = () => api.syncStatus().then(setStatus).catch(() => setStatus([]));
    load();
    const id = setInterval(load, 60_000);
    return () => clearInterval(id);
  }, []);

  const s = (src: Source) => status.find((x) => x.source === src);
  const cur = s(current);

  return (
    <div className={`app theme-${current}`}>
      <div className="stripe" aria-hidden="true" />
      <header className="masthead">
        <Link className="brand" to="/bg">Обществени поръчки</Link>
        <nav className="tabs" aria-label="Източник">
          <NavLink to="/bg" className="tab">
            България {s('bg') && <span className="tab-count">{formatCount(s('bg')!.activeCount)}</span>}
          </NavLink>
          <NavLink to="/eu" className="tab">
            Европа {s('eu') && <span className="tab-count">{formatCount(s('eu')!.activeCount)}</span>}
          </NavLink>
        </nav>
        <p className="sync" role="status">
          {cur?.running
            ? <>Обновява се от {SOURCE_NAME[current]}…</>
            : cur
              ? <>
                  {SOURCE_NAME[current]}: обновено {relativeTime(cur.lastSuccessAt)}
                  {cur.lastRunSuccess === false && <span className="sync-warn" title={cur.lastMessage ?? ''}>Последното обновяване е неуспешно</span>}
                </>
              : null}
        </p>
      </header>

      <Routes>
        <Route path="/" element={<Navigate to="/bg" replace />} />
        <Route path="/bg" element={<TendersPage key="bg" source="bg" />} />
        <Route path="/eu" element={<TendersPage key="eu" source="eu" />} />
        <Route path="*" element={<Navigate to="/bg" replace />} />
      </Routes>
    </div>
  );
}
