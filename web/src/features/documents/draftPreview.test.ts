import { describe, expect, it } from 'vitest'
import { TaxCategory } from '@/lib/api/documents'
import { calculateDraftPreview } from './draftPreview'

const line = (lineNetAmount: number, vatRatePercent = 19) => ({
  lineNetAmount, vatRatePercent, taxCategory: TaxCategory.S,
})

describe('calculateDraftPreview', () => {
  it('uses discounted line nets without applying the discount twice, grouped by rate', () => {
    // 2 × 100 less 10% = 180, plus 100 at 7% and an exempt line.
    const result = calculateDraftPreview([
      line(180), line(100, 7), { ...line(50, 0), taxCategory: TaxCategory.E },
    ])
    expect(result.totalNet).toBe(330)
    expect(result.totalTax).toBe(41.2)
    expect(result.totalGross).toBe(371.2)
    expect(result.taxBreakdown.map((row) => row.taxAmount)).toEqual([34.2, 7, 0])
  })

  it('rounds tax per rate bucket rather than per line', () => {
    const result = calculateDraftPreview([line(0.03), line(0.03)])
    expect(result.totalTax).toBe(0.01)
    expect(result.totalGross).toBe(0.07)
    expect(result.taxBreakdown).toHaveLength(1)
  })

  it('rounds positive and negative half cents away from zero', () => {
    expect(calculateDraftPreview([line(0.5, 19)]).totalTax).toBe(0.1)
    expect(calculateDraftPreview([line(-0.5, 19)]).totalTax).toBe(-0.1)
  })

  it('returns zero totals for an empty draft and leaves line data untouched', () => {
    expect(calculateDraftPreview([])).toEqual({
      taxBreakdown: [], totalNet: 0, totalTax: 0, totalGross: 0,
    })
    const lines = Object.freeze([Object.freeze(line(100))])
    expect(calculateDraftPreview(lines).totalGross).toBe(119)
    expect(lines[0].lineNetAmount).toBe(100)
  })
})
