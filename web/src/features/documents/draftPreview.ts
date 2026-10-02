import type { SalesLine, SalesTaxBreakdownRow } from '@/lib/api/documents'

function roundAmount(value: number): number {
  return Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * 100) / 100
}

/** Display-only estimate. lineNetAmount already includes the line discount. */
export function calculateDraftPreview(
  lines: readonly Pick<SalesLine, 'lineNetAmount' | 'taxCategory' | 'vatRatePercent'>[],
) {
  const buckets = new Map<string, SalesTaxBreakdownRow>()
  for (const line of lines) {
    const key = `${line.taxCategory}-${line.vatRatePercent}`
    const row = buckets.get(key) ?? {
      id: key,
      taxCategory: line.taxCategory,
      vatRatePercent: line.vatRatePercent,
      taxableBase: 0,
      taxAmount: 0,
    }
    row.taxableBase += line.lineNetAmount
    buckets.set(key, row)
  }
  const taxBreakdown = [...buckets.values()].map((row) => ({
    ...row,
    taxAmount: roundAmount(row.taxableBase * row.vatRatePercent / 100),
  }))
  const totalNet = lines.reduce((sum, line) => sum + line.lineNetAmount, 0)
  const totalTax = roundAmount(taxBreakdown.reduce((sum, row) => sum + row.taxAmount, 0))
  return { taxBreakdown, totalNet, totalTax, totalGross: roundAmount(totalNet + totalTax) }
}
