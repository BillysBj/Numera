import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import { ApiError } from '@/lib/api'
import RecordVatPaymentDialog from './RecordVatPaymentDialog'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

const mutateAsync = vi.fn()
vi.mock('./vatPaymentsApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./vatPaymentsApi')>(),
  useRecordVatPayment: () => ({ mutateAsync, isPending: false }),
}))

let container: HTMLDivElement
let root: Root
let onOpenChange: ReturnType<typeof vi.fn>

function field(label: string): HTMLInputElement | HTMLSelectElement {
  const control = [...container.querySelectorAll('label')]
    .find((element) => element.textContent?.includes(label))?.querySelector('input, select')
  if (!control) throw new Error(`Field not found: ${label}`)
  return control as HTMLInputElement | HTMLSelectElement
}

function change(control: HTMLInputElement | HTMLSelectElement, value: string) {
  const prototype = control instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(prototype, 'value')?.set?.call(control, value)
  control.dispatchEvent(new Event('change', { bubbles: true }))
  control.dispatchEvent(new Event('input', { bubbles: true }))
}

async function submit() {
  await act(async () => {
    container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(async () => {
  mutateAsync.mockReset()
  await i18n.changeLanguage('de')
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  onOpenChange = vi.fn()
  await act(async () => root.render(<RecordVatPaymentDialog open onOpenChange={onOpenChange} />))
})

afterEach(async () => {
  await act(async () => root.unmount())
  container.remove()
})

describe('RecordVatPaymentDialog', () => {
  it('defaults to a payment with today as value date', () => {
    expect(field('Art').value).toBe('0')
    expect(field('Wertstellungsdatum').value).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it.each(['', '0', '-1'])('rejects a non-positive or empty amount (%s)', async (amount) => {
    change(field('Betrag'), amount)
    await submit()
    expect(container.textContent).toContain('Der Betrag muss größer als 0 sein.')
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('requires a value date', async () => {
    change(field('Betrag'), '19')
    change(field('Wertstellungsdatum'), '')
    await submit()
    expect(container.textContent).toContain('Bitte gib ein Wertstellungsdatum an.')
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it.each([0, 1])('records kind %s with a positive amount and the selected date', async (kind) => {
    mutateAsync.mockResolvedValue({ id: 'payment-id' })
    change(field('Betrag'), '50.25')
    change(field('Art'), String(kind))
    change(field('Wertstellungsdatum'), '2026-02-10')
    change(field('Referenz'), '  USt 02/2026  ')
    await submit()
    expect(mutateAsync).toHaveBeenCalledWith({
      amount: 50.25, kind, valueDate: '2026-02-10', reference: 'USt 02/2026',
    })
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('surfaces server validation without closing', async () => {
    mutateAsync.mockRejectedValue(new ApiError(422, 'Request failed', {
      errors: { amount: ['Der Server hat den Betrag abgelehnt.'] },
    }))
    change(field('Betrag'), '19')
    await submit()
    expect(container.textContent).toContain('Der Server hat den Betrag abgelehnt.')
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('renders English labels', async () => {
    await act(async () => { await i18n.changeLanguage('en') })
    expect(field('Amount').value).toBe('')
    expect(container.textContent).toContain('Refund / offset from the tax office')
  })
})
