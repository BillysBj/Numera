import { describe, expect, it } from 'vitest'
import {
  emptyCompanyProfileForm,
  makeCompanyProfileSchema,
  toUpdateCompanyProfileRequest,
  type CompanyProfileFormValues,
} from './companyProfileSchema'

// The zod schema mirrors the server FluentValidation rules (CompanyProfileValidators.cs);
// these lock the §14 client-side gates so a UI change can't silently drift from them.
const schema = makeCompanyProfileSchema((k) => k)

function base(): CompanyProfileFormValues {
  return {
    ...emptyCompanyProfileForm(),
    legalName: 'Muster GmbH',
    address: {
      street: 'Hauptstraße 1',
      line2: undefined,
      postalCode: '10115',
      city: 'Berlin',
      countryCode: 'DE',
      poBox: undefined,
    },
    vatId: 'DE123456789',
    taxNumber: undefined,
  }
}

describe('companyProfileSchema', () => {
  it.each(['invoiceFooterText', 'deliveryNoteFooterText'] as const)(
    'limits %s to 2000 characters and allows empty text',
    (field) => {
      for (const value of [undefined, '', 'x'.repeat(2000)]) {
        expect(schema.safeParse({ ...base(), [field]: value }).success).toBe(true)
      }
      const result = schema.safeParse({ ...base(), [field]: 'x'.repeat(2001) })
      expect(result.success).toBe(false)
      if (!result.success) {
        expect(result.error.issues.some((issue) => issue.path[0] === field)).toBe(true)
      }
    },
  )

  it('accepts a valid profile with only a USt-IdNr', () => {
    expect(schema.safeParse(base()).success).toBe(true)
  })

  it('accepts a valid profile with only a Steuernummer', () => {
    const v = { ...base(), vatId: undefined, taxNumber: '29/815/08151' }
    expect(schema.safeParse(v).success).toBe(true)
  })

  it('rejects both tax ids set (exactly-one rule)', () => {
    const v = { ...base(), vatId: 'DE123456789', taxNumber: '29/815/08151' }
    const r = schema.safeParse(v)
    expect(r.success).toBe(false)
    if (!r.success) {
      expect(r.error.issues.some((i) => i.path.join('.') === 'vatId')).toBe(true)
    }
  })

  it('rejects neither tax id set (exactly-one rule)', () => {
    const v = { ...base(), vatId: undefined, taxNumber: undefined }
    expect(schema.safeParse(v).success).toBe(false)
  })

  it('requires a legal name', () => {
    expect(schema.safeParse({ ...base(), legalName: '' }).success).toBe(false)
  })

  it('requires the billing address street / postalCode / city', () => {
    expect(
      schema.safeParse({ ...base(), address: { ...base().address, street: '' } })
        .success,
    ).toBe(false)
    expect(
      schema.safeParse({
        ...base(),
        address: { ...base().address, postalCode: '' },
      }).success,
    ).toBe(false)
    expect(
      schema.safeParse({ ...base(), address: { ...base().address, city: '' } })
        .success,
    ).toBe(false)
  })

  it('rejects out-of-range payment terms', () => {
    expect(
      schema.safeParse({ ...base(), defaultPaymentTermsNetDays: 400 }).success,
    ).toBe(false)
    expect(
      schema.safeParse({ ...base(), defaultPaymentTermsNetDays: 14 }).success,
    ).toBe(true)
  })
})

describe('toUpdateCompanyProfileRequest', () => {
  it('preserves closing-text line breaks and maps empty texts to null', () => {
    const body = toUpdateCompanyProfileRequest(schema.parse({
      ...base(),
      invoiceFooterText: 'Vielen Dank!\n\nIhr Team',
      deliveryNoteFooterText: 'Ware erhalten.\r\nVielen Dank!',
    }))
    expect(body.invoiceFooterText).toBe('Vielen Dank!\n\nIhr Team')
    expect(body.deliveryNoteFooterText).toBe('Ware erhalten.\r\nVielen Dank!')
    const empty = toUpdateCompanyProfileRequest({
      ...base(), invoiceFooterText: '', deliveryNoteFooterText: ' \n ',
    })
    expect(empty.invoiceFooterText).toBeNull()
    expect(empty.deliveryNoteFooterText).toBeNull()
  })

  it('trims fields and maps blank optionals to null', () => {
    const body = toUpdateCompanyProfileRequest(base())
    expect(body.legalName).toBe('Muster GmbH')
    expect(body.vatId).toBe('DE123456789')
    expect(body.taxNumber).toBeNull()
    expect(body.address.countryCode).toBe('DE')
    expect(body.iban).toBeNull()
  })
})
