import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api'
import { downloadDunningNoticePdf, listDunningNotices } from './dunning'
import { downloadDatev } from './ledger'

afterEach(() => vi.unstubAllGlobals())

describe('authenticated downloads', () => {
  it('downloads the exact dunning PDF blob with session credentials', async () => {
    const blob = new Blob(['%PDF-test'], { type: 'application/pdf' })
    const fetch = vi.fn().mockResolvedValue({ ok: true, blob: async () => blob })
    vi.stubGlobal('fetch', fetch)
    expect(await downloadDunningNoticePdf('notice-id')).toBe(blob)
    expect(fetch).toHaveBeenCalledWith('/api/dunning/notices/notice-id/pdf',
      expect.objectContaining({ credentials: 'include' }))
  })

  it('preserves CSV bytes and sends the inclusive date range', async () => {
    const blob = new Blob([new Uint8Array([0x42, 0xfc, 0x72, 0x6f])], { type: 'text/csv' })
    const fetch = vi.fn().mockResolvedValue({ ok: true, blob: async () => blob })
    vi.stubGlobal('fetch', fetch)
    expect(await downloadDatev('2026-01-01', '2026-09-30')).toBe(blob)
    expect(fetch).toHaveBeenCalledWith('/api/ledger/datev?from=2026-01-01&to=2026-09-30',
      expect.objectContaining({ credentials: 'include' }))
  })

  it('preserves the setup problem detail for the German UI', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 409,
      json: async () => ({ detail: 'Kontenrahmen ist nicht eingerichtet' }) }))
    await expect(downloadDatev('2026-01-01', '2026-09-30')).rejects.toMatchObject({
      status: 409, body: { detail: 'Kontenrahmen ist nicht eingerichtet' },
    } satisfies Partial<ApiError>)
  })

  it('requests the selected notice page and keeps enum values numeric', async () => {
    const response = { items: [{ id: 'notice-id', level: 2, status: 1 }], total: 30 }
    const fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, text: async () => JSON.stringify(response) })
    vi.stubGlobal('fetch', fetch)
    expect(await listDunningNotices(2, 25)).toEqual(response)
    expect(fetch).toHaveBeenCalledWith('/api/dunning/notices?page=2&pageSize=25',
      expect.objectContaining({ credentials: 'include' }))
  })
})
