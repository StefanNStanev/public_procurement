// Съответстват на DTO-тата в backend/Porachki.Api/Api/Dtos.cs.
// Алтернатива: генериране от /swagger/v1/swagger.json с openapi-generator (typescript-axios).

export type Source = 'bg' | 'eu';

export interface TenderListItem {
  id: number;
  source: Source;
  noticeNumber: string | null;
  title: string;
  buyerName: string | null;
  contractType: string | null;
  contractTypeLabel: string | null;
  procedureLabel: string | null;
  mainCpv: string | null;
  cpvName: string | null;
  division: string | null;
  divisionLabel: string | null;
  municipality: string | null;
  region: string | null;
  countryCode: string | null;
  countryName: string | null;
  city: string | null;
  publishedAt: string | null;
  deadline: string | null;
  estimatedValue: number | null;
  currency: string | null;
  valueEur: number | null;
  url: string;
}

export interface TenderDetail {
  tender: TenderListItem;
  description: string | null;
  cpvCodes: { code: string; name: string | null }[];
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface FacetValue {
  key: string;
  label: string;
  count: number;
  group?: string | null;
}

export interface Facets {
  contractTypes: FacetValue[];
  procedures: FacetValue[];
  divisions: FacetValue[];
  cpv: FacetValue[];
  municipalities: FacetValue[];
  countries: FacetValue[];
  cities: FacetValue[];
  total: number;
}

export interface SyncStatus {
  source: Source;
  lastRunAt: string | null;
  lastSuccessAt: string | null;
  lastRunSuccess: boolean | null;
  lastMessage: string | null;
  activeCount: number;
  running: boolean;
}

/** Филтрите живеят в URL-а, затова всички са низове/масиви от низове. */
export interface Filters {
  q?: string;
  contractTypes?: string[];
  procedures?: string[];
  divisions?: string[];
  cpv?: string[];
  municipalities?: string[];
  countries?: string[];
  cities?: string[];
  deadlineFrom?: string;
  deadlineTo?: string;
  minValue?: string;
  maxValue?: string;
  sort?: 'deadline' | 'published' | 'value';
  page?: string;
}

export type MultiKey = 'contractTypes' | 'procedures' | 'divisions' | 'cpv' | 'municipalities' | 'countries' | 'cities';

// ---------- AI оценка ----------

export type Severity = 'low' | 'medium' | 'high';

export interface EstimateResult {
  summary: string;
  currency: string;
  estimatedValueFromDocs: number | null;
  awardCriterion: string | null;
  executionPeriod: string | null;
  paymentTerms: string | null;
  costBreakdown: { category: string; description: string; amount: number; basis: string }[];
  totalCost: number;
  costRange: { low: number; high: number };
  recommendedBid: { price: number; marginPercent: number; profit: number; rationale: string };
  lots: { name: string; estimatedValue: number | null; totalCost: number; recommendedBid: number; profit: number }[];
  risks: { title: string; description: string; severity: Severity }[];
  requirements: { title: string; description: string }[];
  penalties: { title: string; description: string }[];
  assumptions: string[];
  missingInformation: string[];
  confidence: Severity;
  confidenceReason: string;
}

export interface EstimateDocument {
  name: string;
  status: string;
  sizeKb: number;
  chars: number;
  included: boolean;
}

export interface Estimate {
  id: number;
  status: 'pending' | 'running' | 'done' | 'failed';
  stage: string | null;
  createdAt: string;
  finishedAt: string | null;
  model: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  costUsd: number | null;
  result: EstimateResult | null;
  documents: EstimateDocument[] | null;
  error: string | null;
}
