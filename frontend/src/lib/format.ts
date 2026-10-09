const dateFmt = new Intl.DateTimeFormat('bg-BG', { day: '2-digit', month: '2-digit', year: 'numeric' });
const timeFmt = new Intl.DateTimeFormat('bg-BG', { hour: '2-digit', minute: '2-digit' });
const moneyFmt = new Intl.NumberFormat('bg-BG', { maximumFractionDigits: 0 });
const compactFmt = new Intl.NumberFormat('bg-BG', { notation: 'compact', maximumFractionDigits: 1 });

export const formatDate = (iso: string | null) => (iso ? dateFmt.format(new Date(iso)) : '—');
export const formatTime = (iso: string | null) => (iso ? timeFmt.format(new Date(iso)) : '');
export const formatCount = (n: number) => moneyFmt.format(n);

export function formatMoney(amount: number | null, currency: string | null) {
  if (amount == null) return null;
  const sym = currency === 'EUR' ? '€' : currency === 'BGN' ? 'лв.' : (currency ?? '');
  return `${moneyFmt.format(amount)} ${sym}`.trim();
}

export function formatMoneyCompact(eur: number | null) {
  return eur == null ? null : `${compactFmt.format(eur)} €`;
}

export type Urgency = 'urgent' | 'soon' | 'normal' | 'none';

/** Оставащо време до крайния срок: брой + мерна единица, за да се подравняват числата. */
export function countdown(iso: string | null): { value: string; unit: string; urgency: Urgency } {
  if (!iso) return { value: '—', unit: 'без срок', urgency: 'none' };
  const ms = new Date(iso).getTime() - Date.now();
  const hours = ms / 36e5;
  if (hours < 0) return { value: '0', unit: 'изтекъл', urgency: 'urgent' };
  if (hours < 24) {
    const h = Math.max(1, Math.floor(hours));
    return { value: String(h), unit: h === 1 ? 'час' : 'часа', urgency: 'urgent' };
  }
  const days = Math.floor(hours / 24);
  return {
    value: String(days),
    unit: days === 1 ? 'ден' : 'дни',
    urgency: days <= 3 ? 'urgent' : days <= 7 ? 'soon' : 'normal',
  };
}

export function relativeTime(iso: string | null) {
  if (!iso) return 'никога';
  const min = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (min < 1) return 'току-що';
  if (min < 60) return `преди ${min} мин.`;
  const h = Math.round(min / 60);
  if (h < 24) return `преди ${h} ч.`;
  return `на ${formatDate(iso)}`;
}
