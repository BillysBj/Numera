import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import { ApiError } from '@/lib/api'
import { SupplierPaymentMethod } from './belegeApi'
import RecordSupplierPaymentDialog from './RecordSupplierPaymentDialog'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

const mutateAsync = vi.fn()

vi.mock('./belegeApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./belegeApi')>()
  return {
    ...actual,
    useRecordSupplierPayment: () => ({ mutateAsync, isPending: false }),
  }
})

const receiptId = '11111111-1111-1111-1111-111111111111'

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient
let onOpenChange: ReturnType<typeof vi.fn>

function field(label: string): HTMLInputElement | HTMLSelectElement {
  const wrapper = [...container.querySelectorAll('label')].find((element) =>
    element.textContent?.includes(label),
  )
  const control = wrapper?.querySelector('input, select')
  if (!control) throw new Error(`Field not found: ${label}`)
  return control as HTMLInputElement | HTMLSelectElement
}

function change(control: HTMLInputElement | HTMLSelectElement, value: string) {
  const prototype =
    control instanceof HTMLSelectElement
      ? HTMLSelectElement.prototype
      : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(prototype, 'value')?.set?.call(control, value)
  control.dispatchEvent(new Event('change', { bubbles: true }))
  control.dispatchEvent(new Event('input', { bubbles: true }))
}

async function submit() {
  const form = container.querySelector('form')
  if (!form) throw new Error('Form not found')
  await act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(async () => {
  mutateAsync.mockReset()
  await i18n.changeLanguage('de')
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  onOpenChange = vi.fn()
  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <RecordSupplierPaymentDialog
          open
          onOpenChange={onOpenChange}
          receiptId={receiptId}
          openAmount={120.5}
          currency="EUR"
          supplierName="Muster Lieferant GmbH"
        />
      </QueryClientProvider>,
    )
  })
})

afterEach(async () => {
  await act(async () => root.unmount())
  container.remove()
  queryClient.clear()
})

describe('RecordSupplierPaymentDialog', () => {
  it('defaults the amount to the open amount', () => {
    expect((field('Betrag') as HTMLInputElement).value).toBe('120.5')
  })

  it('blocks an amount less than or equal to zero before any request', async () => {
    change(field('Betrag'), '0')
    await submit()
    expect(container.textContent).toContain('Der Betrag muss größer als 0 sein.')
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('blocks an amount greater than the open amount', async () => {
    change(field('Betrag'), '121')
    await submit()
    expect(container.textContent).toContain(
      'Der Betrag darf den offenen Betrag nicht überschreiten.',
    )
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('records a valid partial payment and closes', async () => {
    mutateAsync.mockResolvedValue({ id: 'payment-id' })
    change(field('Betrag'), '50.25')
    change(field('Referenz'), 'Kontoauszug 42')
    await submit()

    expect(mutateAsync).toHaveBeenCalledWith({
      amount: 50.25,
      valueDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
      method: SupplierPaymentMethod.BankTransfer,
      reference: 'Kontoauszug 42',
    })
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('surfaces a 422 validation response on the field and as a notice', async () => {
    mutateAsync.mockRejectedValue(
      new ApiError(422, 'Request failed: 422', {
        errors: { amount: ['Der Server hat den Betrag abgelehnt.'] },
      }),
    )
    await submit()
    expect(container.textContent).toContain('Der Server hat den Betrag abgelehnt.')
    expect(container.textContent).toContain('Bitte prüfe die Zahlungsangaben.')
  })
})
