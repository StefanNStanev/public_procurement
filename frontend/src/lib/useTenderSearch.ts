import { useEffect, useState } from 'react';
import axios from 'axios';
import { api } from '../api/client';
import type { Facets, Filters, Paged, Source, TenderListItem } from '../api/types';

interface State {
  list: Paged<TenderListItem> | null;
  facets: Facets | null;
  loading: boolean;
  error: string | null;
}

/** Зарежда списъка и фасетите при всяка промяна на филтрите (с debounce за текстовото търсене). */
export function useTenderSearch(source: Source, filters: Filters) {
  const [state, setState] = useState<State>({ list: null, facets: null, loading: true, error: null });
  const key = JSON.stringify(filters);

  useEffect(() => {
    const ctrl = new AbortController();
    setState((s) => ({ ...s, loading: true, error: null }));
    const timer = setTimeout(async () => {
      try {
        const [list, facets] = await Promise.all([
          api.list(source, filters, ctrl.signal),
          api.facets(source, filters, ctrl.signal),
        ]);
        setState({ list, facets, loading: false, error: null });
      } catch (e) {
        if (axios.isCancel(e)) return;
        const msg = axios.isAxiosError(e) && !e.response
          ? 'Сървърът не отговаря. Проверете дали API-то работи на порт 5080.'
          : 'Поръчките не се заредиха. Опитайте отново след малко.';
        setState((s) => ({ ...s, loading: false, error: msg }));
      }
    }, 250);
    return () => {
      clearTimeout(timer);
      ctrl.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source, key]);

  return state;
}
