import { z } from 'zod'
import {
  CatalogItemKind,
  TaxCategory,
  type CatalogWriteRequest,
} from '@/lib/api/catalog'
import { UNIT_CODES } from './units'

// Client-side zod schema MIRRORING the 02-05 server FluentValidation rules
// (CatalogValidators.cs). The server stays authoritative — this is UX only: it blocks
// obviously-invalid input before the round-trip and localises messages.

/** Minimal translator signature (i18next `t` is assignable to this). */
export type Translate = (key: string) => string

// Optional numeric field: an empty input becomes `undefined`; otherwise a number.
const optionalNumber = (extra: (s: z.ZodNumber) => z.ZodNumber = (s) => s) =>
  z.preprocess(
    (v) => (v === '' || v === null || v === undefined ? undefined : Number(v)),
    extra(z.number()).optional(),
  )

// Required numeric field: an empty/blank input fails as "required" rather than NaN.
const requiredNumber = (msg: string, extra: (s: z.ZodNumber) => z.ZodNumber) =>
  z.preprocess(
    (v) => (v === '' || v === null || v === undefined ? undefined : Number(v)),
    extra(z.number({ error: msg })),
  )

export function makeCatalogSchema(t: Translate) {
  return z.object({
    itemNumber: z
      .string()
      .trim()
      .min(1, t('form.errors.required'))
      .max(64, t('form.errors.itemNumberMax')),
    name: z
      .string()
      .trim()
      .min(1, t('form.errors.required'))
      .max(200, t('form.errors.nameMax')),
    description: z.string().trim().max(2000).optional().or(z.literal('')),
    // Product=1 / Service=2 (numeric wire values).
    kind: z.union([
      z.literal(CatalogItemKind.Product),
      z.literal(CatalogItemKind.Service),
    ]),
    // Only a curated UN/ECE Rec 20 code is accepted (mirrors UnitOfMeasure.IsValid).
    unitCode: z.enum(UNIT_CODES, { error: t('form.errors.unitCode') }),
    netPrice: requiredNumber(t('form.errors.required'), (s) =>
      s.min(0, t('form.errors.netPriceMin')),
    ),
    currency: z
      .string()
      .trim()
      .length(3, t('form.errors.currency'))
      .default('EUR'),
    taxCategory: z.number().int(),
    vatRatePercent: optionalNumber((s) =>
      s.min(0, t('form.errors.vatRange')).max(100, t('form.errors.vatRange')),
    ),
    costPrice: optionalNumber((s) => s.min(0, t('form.errors.costPriceMin'))),
  })
}

// A default instance (identity translator) purely for static type inference.
export const catalogSchema = makeCatalogSchema((k) => k)
export type CatalogFormValues = z.infer<typeof catalogSchema>

/** Empty defaults for the create form (Product, Stück, EUR, standard rate). */
export function emptyCatalogForm(): CatalogFormValues {
  return {
    itemNumber: '',
    name: '',
    description: '',
    kind: CatalogItemKind.Product,
    unitCode: 'C62',
    netPrice: undefined as unknown as number,
    currency: 'EUR',
    taxCategory: TaxCategory.S,
    vatRatePercent: undefined,
    costPrice: undefined,
  }
}

/** Map validated form values to the API write DTO (drops empty strings → null). */
export function toCatalogWriteRequest(v: CatalogFormValues): CatalogWriteRequest {
  const nn = (s?: string | null) => (s && s.trim() ? s.trim() : null)
  return {
    itemNumber: v.itemNumber.trim(),
    name: v.name.trim(),
    description: nn(v.description),
    kind: v.kind as CatalogItemKind,
    unitCode: v.unitCode,
    netPrice: v.netPrice,
    currency: (v.currency ?? 'EUR').toUpperCase(),
    taxCategory: v.taxCategory as TaxCategory,
    vatRatePercent: v.vatRatePercent ?? null,
    costPrice: v.costPrice ?? null,
  }
}
