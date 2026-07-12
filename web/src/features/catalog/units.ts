// Curated UN/ECE Recommendation 20 unit-of-measure codes (EN 16931 BT-130), mirroring
// the server's `Numera.Modules.Catalog.UnitOfMeasure.Codes`. Only the CODE is stored on
// a catalog item; these German display labels are a frontend concern (the backend
// deliberately does NOT model a code table). Keep this in sync with UnitOfMeasure.cs.

import { CatalogItemKind } from '@/lib/api/catalog'

/** UN/ECE Rec 20 code → German display label. */
export const UNIT_LABELS: Record<string, string> = {
  C62: 'Stück',
  H87: 'Stück (Verpackung)',
  HUR: 'Stunde',
  DAY: 'Tag',
  MON: 'Monat',
  KGM: 'Kilogramm',
  MTR: 'Meter',
  MTK: 'Quadratmeter',
  LTR: 'Liter',
  KWH: 'Kilowattstunde',
}

/** The allowed UN/ECE Rec 20 codes, in presentation order (mirrors the server set). */
export const UNIT_CODES = [
  'C62',
  'H87',
  'HUR',
  'DAY',
  'MON',
  'KGM',
  'MTR',
  'MTK',
  'LTR',
  'KWH',
] as const

export type UnitCode = (typeof UNIT_CODES)[number]

/** True if `code` is one of the curated allowed codes. */
export function isValidUnitCode(code: string | null | undefined): code is UnitCode {
  return !!code && (UNIT_CODES as readonly string[]).includes(code)
}

/** Friendly "label (CODE)" for a unit code; falls back to the raw code if unknown. */
export function unitLabel(code: string): string {
  const label = UNIT_LABELS[code]
  return label ? `${label} (${code})` : code
}

/**
 * The sensible default unit for a kind — Stück (C62) for products, Stunde (HUR) for
 * services — mirroring `UnitOfMeasure.DefaultFor` on the server.
 */
export function defaultUnitFor(kind: CatalogItemKind): UnitCode {
  return kind === CatalogItemKind.Service ? 'HUR' : 'C62'
}
