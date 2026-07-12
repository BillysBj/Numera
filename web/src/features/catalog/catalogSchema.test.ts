import { describe, expect, it } from 'vitest'
import {
  emptyCatalogForm,
  makeCatalogSchema,
  toCatalogWriteRequest,
  type CatalogFormValues,
} from './catalogSchema'
import { CatalogItemKind, TaxCategory } from '@/lib/api/catalog'

// The zod schema mirrors the server FluentValidation rules (CatalogValidators.cs);
// these lock the client-side gates so a UI change can't silently drift from them.
const schema = makeCatalogSchema((k) => k)

function base(): CatalogFormValues {
  return {
    ...emptyCatalogForm(),
    itemNumber: 'ART-001',
    name: 'Beratungsstunde',
    netPrice: 95,
  }
}

describe('catalogSchema', () => {
  it('accepts a minimal valid item', () => {
    expect(schema.safeParse(base()).success).toBe(true)
  })

  it('requires an article number and a name', () => {
    expect(schema.safeParse({ ...base(), itemNumber: '' }).success).toBe(false)
    expect(schema.safeParse({ ...base(), name: '' }).success).toBe(false)
  })

  it('rejects an article number longer than 64 chars', () => {
    expect(schema.safeParse({ ...base(), itemNumber: 'X'.repeat(65) }).success).toBe(
      false,
    )
  })

  it('rejects an uncurated unit code but accepts a curated one', () => {
    expect(schema.safeParse({ ...base(), unitCode: 'XXX' as never }).success).toBe(
      false,
    )
    expect(schema.safeParse({ ...base(), unitCode: 'HUR' }).success).toBe(true)
  })

  it('rejects a negative net price', () => {
    expect(schema.safeParse({ ...base(), netPrice: -1 }).success).toBe(false)
  })

  it('requires a net price', () => {
    expect(
      schema.safeParse({ ...base(), netPrice: undefined as unknown as number })
        .success,
    ).toBe(false)
  })

  it('enforces the VAT rate 0..100 range', () => {
    expect(schema.safeParse({ ...base(), vatRatePercent: 150 }).success).toBe(false)
    expect(schema.safeParse({ ...base(), vatRatePercent: 19 }).success).toBe(true)
  })

  it('requires a 3-letter currency', () => {
    expect(schema.safeParse({ ...base(), currency: 'EU' }).success).toBe(false)
  })
})

describe('toCatalogWriteRequest', () => {
  it('maps empty description to null and upper-cases currency', () => {
    const v = { ...base(), description: '', currency: 'eur' }
    const body = toCatalogWriteRequest(v)
    expect(body.description).toBeNull()
    expect(body.currency).toBe('EUR')
    expect(body.itemNumber).toBe('ART-001')
  })

  it('passes through the curated unit code, kind and tax category', () => {
    const v = {
      ...base(),
      kind: CatalogItemKind.Service,
      unitCode: 'HUR' as const,
      taxCategory: TaxCategory.S,
      vatRatePercent: undefined,
      costPrice: undefined,
    }
    const body = toCatalogWriteRequest(v)
    expect(body.kind).toBe(CatalogItemKind.Service)
    expect(body.unitCode).toBe('HUR')
    expect(body.taxCategory).toBe(TaxCategory.S)
    expect(body.vatRatePercent).toBeNull()
    expect(body.costPrice).toBeNull()
  })
})
