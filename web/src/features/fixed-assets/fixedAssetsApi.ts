// Fixed-asset (Anlagevermögen) + AfA BFF client. Same posture as the other api
// modules: HttpOnly session cookie, same-origin, enums cross the wire as NUMBERS.

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'

// Keep aligned with Numera.Modules.Ledger.AfaMethode / AssetDisposal.
export const AfaMethode = { Linear: 0, GwgSofort: 1 } as const
export const AssetDisposal = { None: 0, Verkauf: 1, Verschrottung: 2, Entnahme: 3 } as const

export interface FixedAsset {
  id: string
  tenantId: string
  bezeichnung: string
  lieferant: string | null
  belegRef: string | null
  rechnungsdatum: string | null
  anschaffungsDatum: string
  inbetriebnahmeDatum: string
  anschaffungskostenNetto: number
  anschaffungsnebenkostenNetto: number
  anlagekontoNumber: string
  abschreibungskontoNumber: string
  nutzungsdauerJahre: number
  methode: number
  abgangsDatum: string | null
  abgangsArt: number
  anschaffungswert: number
}

export interface FixedAssetRequest {
  bezeichnung: string
  anschaffungsDatum: string
  inbetriebnahmeDatum: string
  anschaffungskostenNetto: number
  anlagekontoNumber: string
  nutzungsdauerJahre: number
  anschaffungsnebenkostenNetto?: number
  abschreibungskontoNumber?: string | null
  methode?: number
  lieferant?: string | null
  belegRef?: string | null
  rechnungsdatum?: string | null
  abgangsDatum?: string | null
  abgangsArt?: number
}

export interface AfaRunSummary {
  bookedCount: number
  skippedCount: number
  total: number
}

export interface AnlagenspiegelLine {
  bezeichnung: string
  buchwertJahresanfang: number
  zugaenge: number
  abgaenge: number
  afaJahr: number
  buchwertJahresende: number
}

export interface AnlagenspiegelReport {
  jahr: number
  anlagen: AnlagenspiegelLine[]
  summe: AnlagenspiegelLine
}

export function useFixedAssets() {
  return useQuery({
    queryKey: ['fixed-assets'],
    queryFn: () => apiRequest<FixedAsset[]>('/fixed-assets/'),
  })
}

export function useAnlagenspiegel(jahr: number) {
  return useQuery({
    queryKey: ['fixed-assets', 'anlagenspiegel', jahr],
    queryFn: () =>
      apiRequest<AnlagenspiegelReport>(
        `/fixed-assets/anlagenspiegel?${new URLSearchParams({ jahr: String(jahr) })}`,
      ),
  })
}

export function useSaveFixedAsset() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string | null; body: FixedAssetRequest }) =>
      apiRequest<FixedAsset>(id ? `/fixed-assets/${id}` : '/fixed-assets/', {
        method: id ? 'PUT' : 'POST',
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['fixed-assets'] })
    },
  })
}

export function useAfaRun() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (jahr: number) =>
      apiRequest<AfaRunSummary>(`/fixed-assets/${jahr}/afa-run`, { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['fixed-assets'] })
    },
  })
}
