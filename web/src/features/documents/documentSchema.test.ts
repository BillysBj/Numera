import { describe, it, expect } from 'vitest'
import { TaxCategory } from '@/lib/api/documents'
import {
  computePreviewTotals,
  emptyDocumentForm,
  emptyLine,
  toCreateRequest,
  makeDocumentSchema,
  makeLineSchema,
  type PreviewLine,
} from './documentSchema'

// Identity translator — the tests assert on the returned key, not a localised string.
const t = (k: string) => k

const documentSchema = makeDocumentSchema(t)
const lineSchema = makeLineSchema(t)

describe('documentSchema (line validation)', () => {
  const validLine = {
    name: 'Beratung',
    quantity: 2,
    unitCode: 'HUR',
    netUnitPrice: 100,
    taxCategory: TaxCategory.S,
    vatRatePercent: 19,
  }

  it('accepts a well-formed line', () => {
    expect(lineSchema.safeParse(validLine).success).toBe(true)
  })

  it('rejects a blank name', () => {
    const r = lineSchema.safeParse({ ...validLine, name: '' })
    expect(r.success).toBe(false)
  })

  it('rejects a non-positive quantity', () => {
    expect(lineSchema.safeParse({ ...validLine, quantity: 0 }).success).toBe(false)
    expect(lineSchema.safeParse({ ...validLine, quantity: -1 }).success).toBe(false)
  })

  it('rejects a negative net unit price', () => {
    expect(lineSchema.safeParse({ ...validLine, netUnitPrice: -5 }).success).toBe(false)
  })

  it('accepts a zero net unit price', () => {
    expect(lineSchema.safeParse({ ...validLine, netUnitPrice: 0 }).success).toBe(true)
  })

  it('rejects a blank unit code', () => {
    expect(lineSchema.safeParse({ ...validLine, unitCode: '' }).success).toBe(false)
  })

  it('rejects a VAT rate outside 0..100', () => {
    expect(lineSchema.safeParse({ ...validLine, vatRatePercent: -1 }).success).toBe(false)
    expect(lineSchema.safeParse({ ...validLine, vatRatePercent: 101 }).success).toBe(false)
  })
})

describe('documentSchema (document validation)', () => {
  it('accepts the empty form defaults once a line has a price', () => {
    const form = emptyDocumentForm()
    form.lines[0].name = 'Position 1'
    form.lines[0].netUnitPrice = 50
    expect(documentSchema.safeParse(form).success).toBe(true)
  })

  it('rejects a document with no lines', () => {
    const form = { ...emptyDocumentForm(), lines: [] }
    const r = documentSchema.safeParse(form)
    expect(r.success).toBe(false)
  })

  it('rejects a document with a blank documentDate', () => {
    const form = emptyDocumentForm()
    form.lines[0].name = 'Position 1'
    form.lines[0].netUnitPrice = 50
    form.documentDate = ''
    expect(documentSchema.safeParse(form).success).toBe(false)
  })

  it('emptyLine defaults to a standard-rated 19% Stück line', () => {
    const l = emptyLine()
    expect(l.taxCategory).toBe(TaxCategory.S)
    expect(l.vatRatePercent).toBe(19)
    expect(l.unitCode).toBe('C62')
  })
})

describe('computePreviewTotals (per-category round-then-sum)', () => {
  it('taxes a single 19% line', () => {
    const lines: PreviewLine[] = [
      { quantity: 1, netUnitPrice: 100, taxCategory: TaxCategory.S, vatRatePercent: 19 },
    ]
    expect(computePreviewTotals(lines)).toEqual({ net: 100, tax: 19, gross: 119 })
  })

  it('sums per-category tax on a mixed 19/7 document', () => {
    // 100 @ 19% → 19.00, 50 @ 7% → 3.50 → total tax 22.50, gross 172.50.
    const lines: PreviewLine[] = [
      { quantity: 1, netUnitPrice: 100, taxCategory: TaxCategory.S, vatRatePercent: 19 },
      { quantity: 1, netUnitPrice: 50, taxCategory: TaxCategory.S, vatRatePercent: 7 },
    ]
    expect(computePreviewTotals(lines)).toEqual({ net: 150, tax: 22.5, gross: 172.5 })
  })

  it('rounds each category before summing (0.40 vs naive 0.39)', () => {
    // Two 19% lines, each net 1.05 → tax 0.1995 each. Per-line-rounded 0.20 + 0.20 = 0.40.
    // But same category+rate buckets together: base 2.10 → 0.399 → 0.40. Never 0.39.
    const lines: PreviewLine[] = [
      { quantity: 1, netUnitPrice: 1.05, taxCategory: TaxCategory.S, vatRatePercent: 19 },
      { quantity: 1, netUnitPrice: 1.05, taxCategory: TaxCategory.S, vatRatePercent: 19 },
    ]
    const r = computePreviewTotals(lines)
    expect(r.net).toBe(2.1)
    expect(r.tax).toBe(0.4)
    expect(r.gross).toBe(2.5)
  })

  it('rounds a real 19% midpoint away from zero (1.50 → 0.285 → 0.29)', () => {
    const lines: PreviewLine[] = [
      { quantity: 1, netUnitPrice: 1.5, taxCategory: TaxCategory.S, vatRatePercent: 19 },
    ]
    const r = computePreviewTotals(lines)
    expect(r.tax).toBe(0.29)
  })

  it('does not tax non-standard categories (reverse-charge / exempt / export)', () => {
    const lines: PreviewLine[] = [
      { quantity: 1, netUnitPrice: 100, taxCategory: TaxCategory.AE, vatRatePercent: 19 },
      { quantity: 1, netUnitPrice: 100, taxCategory: TaxCategory.E, vatRatePercent: 0 },
      { quantity: 1, netUnitPrice: 100, taxCategory: TaxCategory.G, vatRatePercent: 0 },
    ]
    expect(computePreviewTotals(lines)).toEqual({ net: 300, tax: 0, gross: 300 })
  })

  it('ignores lines with a non-numeric price in the preview', () => {
    const lines: PreviewLine[] = [
      { quantity: 1, netUnitPrice: 100, taxCategory: TaxCategory.S, vatRatePercent: 19 },
      { quantity: 1, netUnitPrice: NaN, taxCategory: TaxCategory.S, vatRatePercent: 19 },
    ]
    expect(computePreviewTotals(lines)).toEqual({ net: 100, tax: 19, gross: 119 })
  })
})


describe('line discounts', () => {
  it('defaults to zero and includes the percentage in the request', () => {
    const form = emptyDocumentForm()
    form.lines[0].name = 'Service'
    form.lines[0].netUnitPrice = 100
    expect(toCreateRequest(form).lines[0].discountPercent).toBe(0)
    form.lines[0].discountPercent = 12.5
    expect(toCreateRequest(form).lines[0].discountPercent).toBe(12.5)
    const { discountPercent: _, ...legacyLine } = form.lines[0]
    expect(lineSchema.parse(legacyLine).discountPercent).toBe(0)
  })

  it.each([-0.01, 100, 101])('rejects %s percent', (discountPercent) => {
    expect(lineSchema.safeParse({ ...emptyLine(), name: 'Service', netUnitPrice: 100, discountPercent }).success).toBe(false)
  })

  it('reduces net and VAT separately in mixed-rate buckets', () => {
    expect(computePreviewTotals([
      { quantity: 3, netUnitPrice: 100, discountPercent: 12.5, taxCategory: TaxCategory.S, vatRatePercent: 19 },
      { quantity: 10, netUnitPrice: 10, discountPercent: 10, taxCategory: TaxCategory.S, vatRatePercent: 7 },
    ])).toEqual({ net: 352.5, tax: 56.18, gross: 408.68 })
  })

  it('sums cent-rounded discounted lines to match the invoice VAT base', () => {
    expect(computePreviewTotals(Array.from({ length: 3 }, () => ({
      quantity: 1, netUnitPrice: 19.99, discountPercent: 12.5, taxCategory: TaxCategory.S, vatRatePercent: 19,
    })))).toEqual({ net: 52.47, tax: 9.97, gross: 62.44 })
  })
})
