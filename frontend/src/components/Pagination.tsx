interface Props {
  page: number;
  pageSize: number;
  total: number;
  onPage: (p: number) => void;
}

export function Pagination({ page, pageSize, total, onPage }: Props) {
  const pages = Math.ceil(total / pageSize);
  if (pages <= 1) return null;

  const around = new Set([1, pages, page - 1, page, page + 1].filter((p) => p >= 1 && p <= pages));
  const list = [...around].sort((a, b) => a - b);

  return (
    <nav className="pagination" aria-label="Страници">
      <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>Предишна</button>
      {list.map((p, i) => (
        <span key={p} className="pagination-item">
          {i > 0 && p - list[i - 1] > 1 && <span className="muted">…</span>}
          <button type="button" aria-current={p === page ? 'page' : undefined} onClick={() => onPage(p)}>{p}</button>
        </span>
      ))}
      <button type="button" disabled={page >= pages} onClick={() => onPage(page + 1)}>Следваща</button>
    </nav>
  );
}
