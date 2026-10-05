import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { getMe } from '@/lib/api'
import {
  AfaMethode,
  useAfaRun,
  useAnlagenspiegel,
  useFixedAssets,
  useSaveFixedAsset,
  type FixedAsset,
  type FixedAssetRequest,
} from './fixedAssetsApi'

const moneyFormat = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })

interface FormState {
  id: string | null
  bezeichnung: string
  anschaffungsDatum: string
  inbetriebnahmeDatum: string
  anschaffungskostenNetto: string
  anschaffungsnebenkostenNetto: string
  anlagekontoNumber: string
  abschreibungskontoNumber: string
  nutzungsdauerJahre: string
  methode: number
}

function emptyForm(): FormState {
  const today = new Date().toISOString().slice(0, 10)
  return {
    id: null,
    bezeichnung: '',
    anschaffungsDatum: today,
    inbetriebnahmeDatum: today,
    anschaffungskostenNetto: '',
    anschaffungsnebenkostenNetto: '',
    anlagekontoNumber: '',
    abschreibungskontoNumber: '',
    nutzungsdauerJahre: '',
    methode: AfaMethode.Linear,
  }
}

export default function FixedAssetsPage() {
  const { t } = useTranslation('reports')
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  const isOwner = me.data?.role === 'Owner'
  const currentYear = new Date().getFullYear()
  const [jahr, setJahr] = useState(currentYear)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [runMessage, setRunMessage] = useState<string | null>(null)

  const assets = useFixedAssets()
  const spiegel = useAnlagenspiegel(jahr)
  const save = useSaveFixedAsset()
  const afaRun = useAfaRun()

  function editAsset(asset: FixedAsset) {
    setFormError(null)
    setForm({
      id: asset.id,
      bezeichnung: asset.bezeichnung,
      anschaffungsDatum: asset.anschaffungsDatum,
      inbetriebnahmeDatum: asset.inbetriebnahmeDatum,
      anschaffungskostenNetto: String(asset.anschaffungskostenNetto),
      anschaffungsnebenkostenNetto: String(asset.anschaffungsnebenkostenNetto),
      anlagekontoNumber: asset.anlagekontoNumber,
      abschreibungskontoNumber: asset.abschreibungskontoNumber,
      nutzungsdauerJahre: String(asset.nutzungsdauerJahre),
      methode: asset.methode,
    })
  }

  async function submitForm() {
    setFormError(null)
    const body: FixedAssetRequest = {
      bezeichnung: form.bezeichnung.trim(),
      anschaffungsDatum: form.anschaffungsDatum,
      inbetriebnahmeDatum: form.inbetriebnahmeDatum,
      anschaffungskostenNetto: Number(form.anschaffungskostenNetto || 0),
      anschaffungsnebenkostenNetto: Number(form.anschaffungsnebenkostenNetto || 0),
      anlagekontoNumber: form.anlagekontoNumber.trim(),
      abschreibungskontoNumber: form.abschreibungskontoNumber.trim() || null,
      nutzungsdauerJahre: Number(form.nutzungsdauerJahre || 0),
      methode: form.methode,
    }
    try {
      await save.mutateAsync({ id: form.id, body })
      setForm(emptyForm())
    } catch {
      setFormError(t('anlagen.saveError'))
    }
  }

  async function runAfa() {
    setRunMessage(null)
    try {
      const summary = await afaRun.mutateAsync(jahr)
      setRunMessage(
        t('anlagen.runResult', {
          booked: summary.bookedCount,
          skipped: summary.skippedCount,
          total: moneyFormat.format(summary.total),
        }),
      )
    } catch {
      setRunMessage(t('anlagen.runError'))
    }
  }

  return (
    <main className="app-main" style={{ maxWidth: '1040px' }}>
      <div className="mb-6">
        <h1 className="text-2xl font-semibold">{t('anlagen.title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('anlagen.subtitle')}</p>
      </div>

      <section className="mb-5 flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4">
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('filters.year')}
          <Input
            className="w-28"
            type="number"
            min={2000}
            max={2100}
            value={jahr}
            onChange={(event) => setJahr(Number(event.target.value))}
          />
        </label>
        {isOwner && (
          <Button disabled={afaRun.isPending} onClick={() => void runAfa()}>
            {afaRun.isPending ? t('anlagen.running') : t('anlagen.runButton')}
          </Button>
        )}
      </section>

      {runMessage && (
        <p className="mb-4 text-sm text-muted-foreground" role="status">{runMessage}</p>
      )}

      {/* Anlagenspiegel */}
      <section className="mb-6 overflow-hidden rounded-xl border border-border bg-card">
        <h2 className="border-b border-border px-4 py-3 text-base font-semibold">
          {t('anlagen.spiegelTitle')}
        </h2>
        {spiegel.isLoading && <p className="px-4 py-3 text-sm text-muted-foreground">{t('loading')}</p>}
        {spiegel.data && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-4 py-3">{t('anlagen.columns.name')}</th>
                  <th className="px-4 py-3 text-right">{t('anlagen.columns.openingValue')}</th>
                  <th className="px-4 py-3 text-right">{t('anlagen.columns.additions')}</th>
                  <th className="px-4 py-3 text-right">{t('anlagen.columns.disposals')}</th>
                  <th className="px-4 py-3 text-right">{t('anlagen.columns.afa')}</th>
                  <th className="px-4 py-3 text-right">{t('anlagen.columns.closingValue')}</th>
                </tr>
              </thead>
              <tbody>
                {spiegel.data.anlagen.map((line, index) => (
                  <tr key={`${line.bezeichnung}-${index}`} className="border-t border-border">
                    <td className="px-4 py-3">{line.bezeichnung}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(line.buchwertJahresanfang)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(line.zugaenge)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(line.abgaenge)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(line.afaJahr)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(line.buchwertJahresende)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="border-t-2 border-border bg-muted/40 font-semibold">
                <tr>
                  <td className="px-4 py-3">{t('anlagen.columns.total')}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(spiegel.data.summe.buchwertJahresanfang)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(spiegel.data.summe.zugaenge)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(spiegel.data.summe.abgaenge)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(spiegel.data.summe.afaJahr)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(spiegel.data.summe.buchwertJahresende)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </section>

      {/* Asset list */}
      <section className="mb-6 overflow-hidden rounded-xl border border-border bg-card">
        <h2 className="border-b border-border px-4 py-3 text-base font-semibold">{t('anlagen.listTitle')}</h2>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[640px] text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3">{t('anlagen.columns.name')}</th>
                <th className="px-4 py-3">{t('anlagen.columns.inService')}</th>
                <th className="px-4 py-3 text-right">{t('anlagen.columns.cost')}</th>
                <th className="px-4 py-3 text-right">{t('anlagen.columns.usefulLife')}</th>
                {isOwner && <th className="px-4 py-3" />}
              </tr>
            </thead>
            <tbody>
              {assets.data?.length === 0 && (
                <tr><td colSpan={isOwner ? 5 : 4} className="px-4 py-4 text-muted-foreground">{t('anlagen.empty')}</td></tr>
              )}
              {assets.data?.map((asset) => (
                <tr key={asset.id} className="border-t border-border">
                  <td className="px-4 py-3">{asset.bezeichnung}</td>
                  <td className="px-4 py-3 tabular-nums">{asset.inbetriebnahmeDatum}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(asset.anschaffungswert)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{asset.nutzungsdauerJahre}</td>
                  {isOwner && (
                    <td className="px-4 py-3 text-right">
                      <button type="button" className="text-sm text-primary hover:underline" onClick={() => editAsset(asset)}>
                        {t('anlagen.edit')}
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* Create / edit form */}
      {isOwner && (
        <section className="rounded-xl border border-border bg-card p-4">
          <h2 className="mb-4 text-base font-semibold">
            {form.id ? t('anlagen.editTitle') : t('anlagen.newTitle')}
          </h2>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={t('anlagen.fields.name')}>
              <Input value={form.bezeichnung} onChange={(e) => setForm({ ...form, bezeichnung: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.account')} hint={t('anlagen.fields.accountHint')}>
              <Input value={form.anlagekontoNumber} onChange={(e) => setForm({ ...form, anlagekontoNumber: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.acquisitionDate')}>
              <Input type="date" value={form.anschaffungsDatum} onChange={(e) => setForm({ ...form, anschaffungsDatum: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.inServiceDate')}>
              <Input type="date" value={form.inbetriebnahmeDatum} onChange={(e) => setForm({ ...form, inbetriebnahmeDatum: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.cost')}>
              <Input type="number" step="0.01" min={0} value={form.anschaffungskostenNetto} onChange={(e) => setForm({ ...form, anschaffungskostenNetto: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.extraCost')}>
              <Input type="number" step="0.01" min={0} value={form.anschaffungsnebenkostenNetto} onChange={(e) => setForm({ ...form, anschaffungsnebenkostenNetto: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.usefulLife')}>
              <Input type="number" min={1} value={form.nutzungsdauerJahre} onChange={(e) => setForm({ ...form, nutzungsdauerJahre: e.target.value })} />
            </Field>
            <Field label={t('anlagen.fields.method')}>
              <select
                className="h-9 rounded-md border border-input bg-background px-3 text-sm"
                value={form.methode}
                onChange={(e) => setForm({ ...form, methode: Number(e.target.value) })}
              >
                <option value={AfaMethode.Linear}>{t('anlagen.methods.linear')}</option>
                <option value={AfaMethode.GwgSofort}>{t('anlagen.methods.gwg')}</option>
              </select>
            </Field>
            <Field label={t('anlagen.fields.expenseAccount')} hint={t('anlagen.fields.expenseAccountHint')}>
              <Input value={form.abschreibungskontoNumber} onChange={(e) => setForm({ ...form, abschreibungskontoNumber: e.target.value })} />
            </Field>
          </div>
          {formError && <p role="alert" className="mt-3 text-sm text-destructive">{formError}</p>}
          <div className="mt-4 flex gap-3">
            <Button disabled={save.isPending} onClick={() => void submitForm()}>
              {save.isPending ? t('anlagen.saving') : form.id ? t('anlagen.update') : t('anlagen.create')}
            </Button>
            {form.id && (
              <Button variant="outline" onClick={() => setForm(emptyForm())}>{t('anlagen.cancel')}</Button>
            )}
          </div>
        </section>
      )}
    </main>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1.5 text-sm font-medium">
      {label}
      {children}
      {hint && <span className="text-xs font-normal text-muted-foreground">{hint}</span>}
    </label>
  )
}
