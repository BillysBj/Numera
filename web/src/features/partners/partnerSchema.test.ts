import { describe, expect, it } from 'vitest'
import {
  emptyPartnerForm,
  makePartnerSchema,
  toWriteRequest,
  type PartnerFormValues,
} from './partnerSchema'

// The zod schema mirrors the server FluentValidation rules (PartnerValidators.cs);
// these lock the client-side gates so a UI change can't silently drift from them.
const schema = makePartnerSchema((k) => k)

function base(): PartnerFormValues {
  return {
    ...emptyPartnerForm(),
    name: 'Acme GmbH',
    billingAddress: {
      street: 'Hauptstr. 1',
      line2: '',
      postalCode: '10115',
      city: 'Berlin',
      countryCode: 'DE',
      poBox: '',
    },
  }
}

describe('partnerSchema', () => {
  it('accepts a minimal valid customer', () => {
    expect(schema.safeParse(base()).success).toBe(true)
  })

  it('requires at least one role (customer or supplier)', () => {
    const r = schema.safeParse({ ...base(), isCustomer: false, isSupplier: false })
    expect(r.success).toBe(false)
  })

  it('requires a 2-letter country code', () => {
    const v = base()
    v.billingAddress.countryCode = 'DEU'
    expect(schema.safeParse(v).success).toBe(false)
  })

  it('rejects an implausible VAT-ID but accepts a plausible one', () => {
    expect(schema.safeParse({ ...base(), vatId: '!!' }).success).toBe(false)
    expect(schema.safeParse({ ...base(), vatId: 'DE123456789' }).success).toBe(true)
  })

  it('enforces the Skonto 0..100 range', () => {
    expect(schema.safeParse({ ...base(), skontoPercent: 150 }).success).toBe(false)
    expect(schema.safeParse({ ...base(), skontoPercent: 3 }).success).toBe(true)
  })

  it('requires a 3-letter currency', () => {
    expect(schema.safeParse({ ...base(), defaultCurrency: 'EU' }).success).toBe(false)
  })
})

describe('toWriteRequest', () => {
  it('maps empty strings to null and upper-cases country/currency', () => {
    const v = base()
    v.legalForm = ''
    v.billingAddress.countryCode = 'de'
    v.defaultCurrency = 'eur'
    const body = toWriteRequest(v)
    expect(body.legalForm).toBeNull()
    expect(body.billingAddress.countryCode).toBe('DE')
    expect(body.defaultCurrency).toBe('EUR')
    expect(body.shippingAddress).toBeNull()
  })

  it('includes the shipping address only when toggled on', () => {
    const v = base()
    v.hasShippingAddress = true
    v.shippingAddress = {
      street: 'Lager 2',
      line2: '',
      postalCode: '20095',
      city: 'Hamburg',
      countryCode: 'DE',
      poBox: '',
    }
    expect(toWriteRequest(v).shippingAddress?.city).toBe('Hamburg')
  })
})
