import { z } from 'zod'
import {
  DocumentType,
  TaxCategory,
  type CreateSalesDocumentRequest,
  type SalesLineRequest,
} from '@/lib/api/documents'

// Client-side zod schema MIRRORING the 03-04 server FluentValidation rules
// (SalesDocumentValidators.cs). The server stays authoritative — this is UX only: it
// blocks obviously-invalid input before the round-trip and localises messages.

/** Minimal translator signature (i18next `t` is assignable to this). */
export type Translate = (key: string) => string

// Numeric field coerced from the string an <input type="number"> yields; an empty/blank
// value fails as "required" (not NaN).
const numberField = (msg: string, extra: (s: z.ZodNumber) => z.ZodNumber) =>
  z.preprocess(
    (v) => (v === '' || v === null || v === undefined ? undefined : Number(v)),
    extra(z.number({ error: msg })),
  )

export function makeLineSchema(t: Translate) {
  return z.object({
    // Provenance only — the CATL-02 picker stamps this; free-text lines leave it null.
    catalogItemId: z.string().nullish(),
    name: z
      .string()
      .trim()
      .min(1, t('form.errors.lineName'))
      .max(400, t('form.errors.lineNameMax')),
    description: z.string().trim().max(2000).optional().or(z.literal('')),
    quantity: numberField(t('form.errors.required'), (s) =>
      s.gt(0, t('form.errors.quantityPositive')),
    ),
    unitCode: z.string().trim().min(1, t('form.errors.unitCode')),
    netUnitPrice: numberField(t('form.errors.required'), (s) =>
      s.min(0, t('form.errors.netPriceMin')),
    ),
    discountPercent: z.preprocess(
      (v) => v === '' || v === undefined ? 0 : Number(v),
      z.number().min(0, t('form.errors.discountRange')).lt(100, t('form.errors.discountRange')),
    ),
    taxCategory: z.number().int(),
    vatRatePercent: numberField(t('form.errors.required'), (s) =>
      s.min(0, t('form.errors.vatRange')).max(100, t('form.errors.vatRange')),
    ),
  })
}

export function makeDocumentSchema(t: Translate) {
  return z.object({
    documentType: z.number().int(),
    partnerId: z.string().trim().optional().or(z.literal('')),
    documentDate: z.string().trim().min(1, t('form.errors.required')),
    currency: z.enum(['EUR', 'USD', 'GBP', 'CHF', 'CAD', 'AUD', 'NOK', 'SEK', 'DKK', 'PLN', 'CZK']),
    exchangeRate: z.preprocess(
      (v) => (v === '' || v === null || v === undefined ? undefined : Number(v)),
      z.number().positive(t('form.errors.exchangeRatePositive')).optional(),
    ),
    exchangeRateDate: z.string().trim().optional().or(z.literal('')),
    notes: z.string().trim().max(4000).optional().or(z.literal('')),
    buyerReference: z.string().trim().max(200).optional().or(z.literal('')),
    // A document must carry at least one line (mirrors the server NotEmpty rule).
    lines: z.array(makeLineSchema(t)).min(1, t('form.errors.linesRequired')),
  }).superRefine((value, ctx) => {
    if (value.currency !== 'EUR') {
      if (value.exchangeRate == null) {
        ctx.addIssue({
          code: 'custom',
          path: ['exchangeRate'],
          message: t('form.errors.required'),
        })
      }
      if (!value.exchangeRateDate) {
        ctx.addIssue({
          code: 'custom',
          path: ['exchangeRateDate'],
          message: t('form.errors.required'),
        })
      }
    }
  })
}

// Default instances (identity translator) purely for static type inference.
export const documentSchema = makeDocumentSchema((k) => k)
export type DocumentFormValues = z.infer<typeof documentSchema>
export type LineFormValues = z.infer<ReturnType<typeof makeLineSchema>>

/** A blank free-text line (standard-rated, Stück, 19%). */
export function emptyLine(): LineFormValues {
  return {
    catalogItemId: null,
    name: '',
    description: '',
    quantity: 1,
    unitCode: 'C62',
    netUnitPrice: undefined as unknown as number,
    taxCategory: TaxCategory.S,
    vatRatePercent: 19,
    discountPercent: 0,
  }
}

/** Empty defaults for the create form (a Rechnung with one blank line, today's date). */
export function emptyDocumentForm(): DocumentFormValues {
  return {
    documentType: DocumentType.Rechnung,
    partnerId: '',
    documentDate: new Date().toISOString().slice(0, 10),
    currency: 'EUR',
    exchangeRate: undefined,
    exchangeRateDate: '',
    notes: '',
    buyerReference: '',
    lines: [emptyLine()],
  }
}

/** Map validated form values to the API create/update DTO (drops empty strings → null). */
export function toCreateRequest(v: DocumentFormValues): CreateSalesDocumentRequest {
  const nn = (s?: string | null) => (s && s.trim() ? s.trim() : null)
  return {
    documentType: v.documentType as DocumentType,
    partnerId: nn(v.partnerId),
    documentDate: v.documentDate,
    serviceDate: null,
    notes: nn(v.notes),
    buyerReference: nn(v.buyerReference),
    lines: v.lines.map(
      (l): SalesLineRequest => ({
        catalogItemId: l.catalogItemId ?? null,
        name: l.name.trim(),
        description: nn(l.description),
        quantity: l.quantity,
        unitCode: l.unitCode,
        netUnitPrice: l.netUnitPrice,
        discountPercent: l.discountPercent ?? 0,
        taxCategory: l.taxCategory as TaxCategory,
        vatRatePercent: l.vatRatePercent,
      }),
    ),
    currency: v.currency,
    exchangeRate: v.currency === 'EUR' ? null : v.exchangeRate,
    exchangeRateDate: v.currency === 'EUR' ? null : nn(v.exchangeRateDate),
  }
}

// ---- Preview totals (client-side estimate) ---------------------------------

/**
 * Round half-AWAY-FROM-ZERO to `dp` decimals — the kaufmännische rounding the server's
 * RoundingPolicy uses (MidpointRounding.AwayFromZero). JS `Math.round` rounds half toward
 * +∞, so operate on the magnitude and re-apply the sign.
 */
function roundAway(value: number, dp: number): number {
  const f = 10 ** dp
  const scaled = Math.abs(value) * f
  // Correct for binary float error so a true half (e.g. 0.285 × 100 = 28.4999…96)
  // rounds up, matching the server's decimal MidpointRounding.AwayFromZero. The epsilon
  // scales with magnitude so it stays effective for larger monetary values.
  const corrected = scaled + Math.max(1e-9, scaled * 1e-12)
  return (Math.sign(value) * Math.round(corrected)) / f
}

/** A line as it enters the preview (only the fields that drive the totals). */
export interface PreviewLine {
  discountPercent?: number
  quantity: number
  netUnitPrice: number
  taxCategory: number
  vatRatePercent: number
}

export interface PreviewTotals {
  net: number
  tax: number
  gross: number
}

/**
 * Live client-side total preview for the editor. Mirrors the server's per-category
 * round-then-sum (VatCalculationService): each line net = round(qty × price, 4); only
 * category S is taxed (all of AE/K/E/Z/G/O force to rate 0 / tax 0); lines are bucketed
 * by (category, effective rate), each bucket's tax is rounded to 2 dp away-from-zero, and
 * the document tax is the SUM of those rounded rows — never round(grand total) (EN 16931
 * BR-CO-14).
 *
 * NOTE: this is a PREVIEW only. The SERVER recomputes the authoritative totals + BG-23
 * breakdown at finalize; the numbers shown here are an estimate for the draft editor.
 */
export function computePreviewTotals(lines: PreviewLine[]): PreviewTotals {
  const buckets = new Map<string, number>()
  const hasDiscount = lines.some((l) => Number(l.discountPercent ?? 0) > 0)
  let net = 0

  for (const l of lines) {
    const qty = Number(l.quantity)
    const price = Number(l.netUnitPrice)
    if (!Number.isFinite(qty) || !Number.isFinite(price)) continue

    const discount = Number(l.discountPercent ?? 0)
    if (!Number.isFinite(discount) || discount < 0 || discount >= 100) continue
    const storedNet = roundAway(qty * price * (1 - discount / 100), 4)
    const lineNet = hasDiscount ? roundAway(storedNet, 2) : storedNet
    net += lineNet

    // Only standard-rated (S) lines carry tax; everything else is effectively rate 0.
    const effectiveRate = l.taxCategory === TaxCategory.S ? Number(l.vatRatePercent) || 0 : 0
    const key = `${l.taxCategory}:${effectiveRate}`
    buckets.set(key, (buckets.get(key) ?? 0) + lineNet)
  }

  let tax = 0
  for (const [key, base] of buckets) {
    const effectiveRate = Number(key.split(':')[1])
    if (effectiveRate > 0) {
      tax += roundAway((base * effectiveRate) / 100, 2)
    }
  }

  const netRounded = roundAway(net, 2)
  const taxRounded = roundAway(tax, 2)
  return { net: netRounded, tax: taxRounded, gross: roundAway(netRounded + taxRounded, 2) }
}
