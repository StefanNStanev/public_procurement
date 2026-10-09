import axios from 'axios';
import type { Estimate, Facets, Filters, Paged, Source, SyncStatus, TenderDetail, TenderListItem } from './types';

const http = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '/api',
  timeout: 60_000,
  // ?divisions=45&divisions=72 (без [] – така го чете ASP.NET Core)
  paramsSerializer: { indexes: null },
});

function params(f: Filters, withPaging: boolean) {
  const { page, sort, ...rest } = f;
  const p: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(rest)) {
    if (v === undefined || v === '' || (Array.isArray(v) && v.length === 0)) continue;
    p[k] = v;
  }
  if (withPaging) {
    p.page = page ?? '1';
    p.pageSize = 50;
    if (sort) p.sort = sort;
  }
  return p;
}

export const api = {
  list: (source: Source, f: Filters, signal?: AbortSignal) =>
    http.get<Paged<TenderListItem>>(`/${source}/tenders`, { params: params(f, true), signal }).then((r) => r.data),

  facets: (source: Source, f: Filters, signal?: AbortSignal) =>
    http.get<Facets>(`/${source}/tenders/facets`, { params: params(f, false), signal }).then((r) => r.data),

  detail: (source: Source, id: number, signal?: AbortSignal) =>
    http.get<TenderDetail>(`/${source}/tenders/${id}`, { signal }).then((r) => r.data),

  syncStatus: () => http.get<SyncStatus[]>('/sync/status').then((r) => r.data),

  /** Последната AI оценка или null, ако няма. */
  estimate: (tenderId: number, signal?: AbortSignal) =>
    http
      .get<Estimate>(`/bg/tenders/${tenderId}/estimate`, { signal })
      .then((r) => r.data)
      .catch((e) => {
        if (axios.isAxiosError(e) && e.response?.status === 404) return null;
        throw e;
      }),

  startEstimate: (tenderId: number, force = false) =>
    http.post<Estimate>(`/bg/tenders/${tenderId}/estimate`, null, { params: force ? { force: true } : {} }).then((r) => r.data),
};

/** Текстът на грешка от ProblemDetails на API-то. */
export function errorMessage(e: unknown, fallback: string) {
  if (axios.isAxiosError(e)) {
    const data = e.response?.data as { title?: string } | undefined;
    if (data?.title) return data.title;
    if (!e.response) return 'Сървърът не отговаря.';
  }
  return fallback;
}
