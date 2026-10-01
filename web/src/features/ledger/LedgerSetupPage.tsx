import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Besteuerungsart,
  ChartVariant,
  Gewinnermittlungsart,
  getLedgerSettings,
  setupLedger,
  ApiError,
} from '@/lib/api/ledger'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Select } from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'

const CHART_LABELS: Record<number, string> = {
  [ChartVariant.Skr04]: 'SKR04 (Standardkontenrahmen 04)',
  [ChartVariant.Skr03]: 'SKR03 (Standardkontenrahmen 03)',
}
const TAX_LABELS: Record<number, string> = {
  [Besteuerungsart.Soll]: 'Soll-Versteuerung (nach vereinbarten Entgelten)',
  [Besteuerungsart.Ist]: 'Ist-Versteuerung (nach vereinnahmten Entgelten)',
}
const PROFIT_LABELS: Record<number, string> = {
  [Gewinnermittlungsart.Euer]: 'Einnahmen-Überschuss-Rechnung (EÜR)',
  [Gewinnermittlungsart.Bilanzierung]: 'Bilanzierung',
}

export default function LedgerSetupPage() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['ledger-settings'], queryFn: getLedgerSettings })

  const [chartVariant, setChartVariant] = useState<number>(ChartVariant.Skr04)
  const [besteuerungsart, setBesteuerungsart] = useState<number>(Besteuerungsart.Soll)
  const [gewinnermittlungsart, setGewinnermittlungsart] = useState<number>(Gewinnermittlungsart.Euer)
  const [startMonth, setStartMonth] = useState<number>(1)
  const [banner, setBanner] = useState<{ kind: 'error' | 'success'; text: string } | null>(null)

  const setup = useMutation({
    mutationFn: () =>
      setupLedger({ chartVariant, besteuerungsart, gewinnermittlungsart, fiscalYearStartMonth: startMonth }),
    onSuccess: (res) => {
      setBanner({
        kind: 'success',
        text: `Kontenrahmen eingerichtet — ${res.accountCount} Konten angelegt. USt-VA, EÜR und der Buchungsvorschlag stehen jetzt bereit.`,
      })
      queryClient.invalidateQueries({ queryKey: ['ledger-settings'] })
    },
    onError: (err) =>
      setBanner({
        kind: 'error',
        text:
          err instanceof ApiError && err.status === 409
            ? 'Der Kontenrahmen ist bereits eingerichtet.'
            : 'Einrichtung fehlgeschlagen. Bitte später erneut versuchen.',
      }),
  })

  if (settings.isLoading) {
    return (
      <main className="app-main" style={{ maxWidth: '760px' }}>
        <p className="text-muted-foreground">Wird geladen …</p>
      </main>
    )
  }

  const existing = settings.data

  return (
    <main className="app-main" style={{ maxWidth: '760px' }}>
      <h1 className="text-2xl font-semibold">Kontenrahmen & Buchhaltung</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Einmalige Einrichtung des Kontenrahmens und der steuerlichen Grundeinstellungen.
        Erforderlich für USt-Voranmeldung, EÜR und den Buchungsvorschlag.
      </p>

      {banner && (
        <p
          className={
            'mt-4 rounded-md border px-4 py-3 text-sm ' +
            (banner.kind === 'success'
              ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400'
              : 'border-destructive/30 bg-destructive/10 text-destructive')
          }
          role={banner.kind === 'error' ? 'alert' : 'status'}
        >
          {banner.text}
        </p>
      )}

      {existing ? (
        <Card className="mt-5">
          <CardHeader>
            <CardTitle>Eingerichtet</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 text-sm">
            <Row label="Kontenrahmen" value={CHART_LABELS[existing.chartVariant] ?? String(existing.chartVariant)} />
            <Row label="Besteuerung" value={TAX_LABELS[existing.besteuerungsart] ?? String(existing.besteuerungsart)} />
            <Row label="Gewinnermittlung" value={PROFIT_LABELS[existing.gewinnermittlungsart] ?? String(existing.gewinnermittlungsart)} />
            <Row label="Wirtschaftsjahr-Beginn" value={`Monat ${existing.fiscalYearStartMonth}`} />
            <p className="mt-2 text-xs text-muted-foreground">
              Die Einrichtung ist einmalig und aus Gründen der Buchführungsintegrität nicht über
              die Oberfläche änderbar. Für Änderungen wenden Sie sich an den Support/Steuerberater.
            </p>
          </CardContent>
        </Card>
      ) : (
        <>
          <div className="mt-4 rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-800 dark:text-amber-300">
            <strong>Wichtig:</strong> Die Wahl von Kontenrahmen und Besteuerung ist eine
            steuerliche Entscheidung, ist <strong>einmalig</strong> und kann anschließend nicht
            mehr geändert werden. Bitte im Zweifel vorher mit dem Steuerberater abstimmen.
          </div>

          <Card className="mt-5">
            <CardHeader>
              <CardTitle>Einrichtung</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4">
              <Field label="Kontenrahmen">
                <Select value={chartVariant} onChange={(e) => setChartVariant(Number(e.target.value))}>
                  <option value={ChartVariant.Skr04}>{CHART_LABELS[ChartVariant.Skr04]}</option>
                  <option value={ChartVariant.Skr03}>{CHART_LABELS[ChartVariant.Skr03]}</option>
                </Select>
              </Field>
              <Field label="Besteuerung">
                <Select value={besteuerungsart} onChange={(e) => setBesteuerungsart(Number(e.target.value))}>
                  <option value={Besteuerungsart.Soll}>{TAX_LABELS[Besteuerungsart.Soll]}</option>
                  <option value={Besteuerungsart.Ist}>{TAX_LABELS[Besteuerungsart.Ist]}</option>
                </Select>
              </Field>
              <Field label="Gewinnermittlung">
                <Select value={gewinnermittlungsart} onChange={(e) => setGewinnermittlungsart(Number(e.target.value))}>
                  <option value={Gewinnermittlungsart.Euer}>{PROFIT_LABELS[Gewinnermittlungsart.Euer]}</option>
                  <option value={Gewinnermittlungsart.Bilanzierung}>{PROFIT_LABELS[Gewinnermittlungsart.Bilanzierung]}</option>
                </Select>
              </Field>
              <Field label="Wirtschaftsjahr beginnt im Monat">
                <Select value={startMonth} onChange={(e) => setStartMonth(Number(e.target.value))}>
                  {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                    <option key={m} value={m}>{m}</option>
                  ))}
                </Select>
              </Field>

              <div className="pt-1">
                <Button
                  type="button"
                  disabled={setup.isPending}
                  onClick={() => {
                    setBanner(null)
                    if (
                      window.confirm(
                        'Kontenrahmen jetzt endgültig einrichten? Diese Wahl kann nicht mehr geändert werden.',
                      )
                    ) {
                      setup.mutate()
                    }
                  }}
                >
                  {setup.isPending ? 'Wird eingerichtet …' : 'Kontenrahmen einrichten'}
                </Button>
              </div>
            </CardContent>
          </Card>
        </>
      )}
    </main>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label>{label}</Label>
      {children}
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-border pb-2 last:border-0">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  )
}
