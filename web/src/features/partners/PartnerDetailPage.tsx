import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  archivePartner,
  createContact,
  createNote,
  deleteContact,
  deleteNote,
  getPartner,
  listActivities,
  listContacts,
  listNotes,
  unarchivePartner,
  updateContact,
  updateNote,
  PartnerActivityType,
  type ContactDto,
  type ContactWriteRequest,
} from '@/lib/api/partners'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button, buttonVariants } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Dialog, DialogFooter } from '@/components/ui/dialog'
import { cn } from '@/lib/utils'

const ACTIVITY_KEY: Record<number, string> = {
  [PartnerActivityType.PartnerCreated]: 'PartnerCreated',
  [PartnerActivityType.PartnerUpdated]: 'PartnerUpdated',
  [PartnerActivityType.Archived]: 'Archived',
  [PartnerActivityType.Unarchived]: 'Unarchived',
  [PartnerActivityType.NoteAdded]: 'NoteAdded',
}

const emptyContact = (): ContactWriteRequest => ({
  salutation: '',
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  position: '',
  isPrimary: false,
})

export default function PartnerDetailPage() {
  const { t } = useTranslation('partners')
  const { id = '' } = useParams<{ id: string }>()
  const queryClient = useQueryClient()

  const partner = useQuery({
    queryKey: ['partner', id],
    queryFn: () => getPartner(id),
  })
  const contacts = useQuery({
    queryKey: ['contacts', id],
    queryFn: () => listContacts(id),
  })
  const notes = useQuery({
    queryKey: ['notes', id],
    queryFn: () => listNotes(id),
  })
  const activities = useQuery({
    queryKey: ['activities', id],
    queryFn: () => listActivities(id),
  })

  const invalidate = (...keys: string[]) =>
    keys.forEach((k) => queryClient.invalidateQueries({ queryKey: [k, id] }))

  // --- Archive / unarchive --------------------------------------------------
  const archiveMutation = useMutation({
    mutationFn: () =>
      partner.data?.archived ? unarchivePartner(id) : archivePartner(id),
    onSuccess: () => {
      invalidate('partner', 'activities')
      queryClient.invalidateQueries({ queryKey: ['partners'] })
    },
  })

  // --- Contacts -------------------------------------------------------------
  const [contactOpen, setContactOpen] = useState(false)
  const [editingContactId, setEditingContactId] = useState<string | null>(null)
  const [contactDraft, setContactDraft] = useState<ContactWriteRequest>(
    emptyContact(),
  )

  const saveContact = useMutation({
    mutationFn: async () => {
      if (editingContactId) {
        await updateContact(id, editingContactId, contactDraft)
      } else {
        await createContact(id, contactDraft)
      }
    },
    onSuccess: () => {
      invalidate('contacts')
      setContactOpen(false)
    },
  })
  const removeContact = useMutation({
    mutationFn: (contactId: string) => deleteContact(id, contactId),
    onSuccess: () => invalidate('contacts'),
  })

  const openNewContact = () => {
    setEditingContactId(null)
    setContactDraft(emptyContact())
    setContactOpen(true)
  }
  const openEditContact = (c: ContactDto) => {
    setEditingContactId(c.id)
    setContactDraft({
      salutation: c.salutation ?? '',
      firstName: c.firstName ?? '',
      lastName: c.lastName,
      email: c.email ?? '',
      phone: c.phone ?? '',
      position: c.position ?? '',
      isPrimary: c.isPrimary,
    })
    setContactOpen(true)
  }

  // --- Notes ----------------------------------------------------------------
  const [newNote, setNewNote] = useState('')
  const [editingNoteId, setEditingNoteId] = useState<string | null>(null)
  const [editingNoteBody, setEditingNoteBody] = useState('')

  const addNote = useMutation({
    mutationFn: () => createNote(id, newNote.trim()),
    onSuccess: () => {
      setNewNote('')
      invalidate('notes', 'activities')
    },
  })
  const saveNote = useMutation({
    mutationFn: () => updateNote(id, editingNoteId!, editingNoteBody.trim()),
    onSuccess: () => {
      setEditingNoteId(null)
      invalidate('notes')
    },
  })
  const removeNote = useMutation({
    mutationFn: (noteId: string) => deleteNote(id, noteId),
    onSuccess: () => invalidate('notes'),
  })

  if (partner.isLoading) {
    return (
      <main className="app-main">
        <p className="text-muted-foreground">{t('table.loading')}</p>
      </main>
    )
  }
  if (partner.isError || !partner.data) {
    return (
      <main className="app-main">
        <p className="text-destructive">{t('loadError')}</p>
      </main>
    )
  }
  const p = partner.data

  return (
    <main className="app-main" style={{ maxWidth: '900px' }}>
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <h1 className="mr-auto text-2xl font-semibold">{p.name}</h1>
        {p.isCustomer && <Badge variant="secondary">{t('roles.customer')}</Badge>}
        {p.isSupplier && <Badge variant="outline">{t('roles.supplier')}</Badge>}
        {p.archived && <Badge variant="destructive">{t('status.archived')}</Badge>}
        <Link
          to={`/partners/${id}/edit`}
          className={cn(buttonVariants({ variant: 'outline' }))}
        >
          {t('actions.edit')}
        </Link>
        <Button
          variant={p.archived ? 'secondary' : 'destructive'}
          disabled={archiveMutation.isPending}
          onClick={() => {
            const msg = p.archived
              ? t('detail.confirmUnarchive')
              : t('detail.confirmArchive')
            if (window.confirm(msg)) archiveMutation.mutate()
          }}
        >
          {p.archived ? t('actions.unarchive') : t('actions.archive')}
        </Button>
      </div>

      <div className="grid gap-5 md:grid-cols-2">
        {/* Master data */}
        <Card className="md:col-span-2">
          <CardHeader>
            <CardTitle>{t('form.sections.master')}</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
            <Detail label={t('form.fields.legalForm')} value={p.legalForm} />
            <Detail label={t('form.fields.vatId')} value={p.vatId} />
            <Detail
              label={t('form.sections.billingAddress')}
              value={`${p.billingAddress.street}, ${p.billingAddress.postalCode} ${p.billingAddress.city} (${p.billingAddress.countryCode})`}
            />
            {p.shippingAddress && (
              <Detail
                label={t('form.sections.shippingAddress')}
                value={`${p.shippingAddress.street}, ${p.shippingAddress.postalCode} ${p.shippingAddress.city} (${p.shippingAddress.countryCode})`}
              />
            )}
            <Detail label={t('form.fields.email')} value={p.email} />
            <Detail label={t('form.fields.phone')} value={p.phone} />
            <Detail
              label={t('form.fields.paymentTermsNetDays')}
              value={p.paymentTermsNetDays?.toString()}
            />
            <Detail
              label={t('form.fields.defaultCurrency')}
              value={p.defaultCurrency}
            />
            <Detail
              label={t('form.fields.customerNumber')}
              value={p.customerNumber}
            />
            <Detail
              label={t('form.fields.supplierNumber')}
              value={p.supplierNumber}
            />
          </CardContent>
        </Card>

        {/* Contacts */}
        <Card>
          <CardHeader className="flex-row items-center justify-between">
            <CardTitle>{t('detail.contacts')}</CardTitle>
            <Button variant="outline" size="sm" onClick={openNewContact}>
              {t('form.contact.add')}
            </Button>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            {contacts.data?.length ? (
              contacts.data.map((c) => (
                <div
                  key={c.id}
                  className="flex items-center justify-between rounded-md border border-border p-2 text-sm"
                >
                  <div>
                    <div className="font-medium">
                      {[c.firstName, c.lastName].filter(Boolean).join(' ')}
                      {c.isPrimary && (
                        <Badge variant="secondary" className="ml-2">
                          {t('form.contact.isPrimary')}
                        </Badge>
                      )}
                    </div>
                    <div className="text-muted-foreground">
                      {[c.position, c.email, c.phone].filter(Boolean).join(' · ')}
                    </div>
                  </div>
                  <div className="flex gap-1">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => openEditContact(c)}
                    >
                      {t('actions.edit')}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => {
                        if (window.confirm(t('detail.confirmDeleteContact')))
                          removeContact.mutate(c.id)
                      }}
                    >
                      {t('actions.delete')}
                    </Button>
                  </div>
                </div>
              ))
            ) : (
              <p className="text-sm text-muted-foreground">
                {t('detail.noContacts')}
              </p>
            )}
          </CardContent>
        </Card>

        {/* Notes */}
        <Card>
          <CardHeader>
            <CardTitle>{t('detail.notes')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <div className="flex flex-col gap-2">
              <Textarea
                placeholder={t('detail.notePlaceholder')}
                value={newNote}
                onChange={(e) => setNewNote(e.target.value)}
              />
              <Button
                size="sm"
                className="self-start"
                disabled={!newNote.trim() || addNote.isPending}
                onClick={() => addNote.mutate()}
              >
                {t('detail.addNote')}
              </Button>
            </div>
            {notes.data?.length ? (
              notes.data.map((n) => (
                <div
                  key={n.id}
                  className="rounded-md border border-border p-2 text-sm"
                >
                  {editingNoteId === n.id ? (
                    <div className="flex flex-col gap-2">
                      <Textarea
                        value={editingNoteBody}
                        onChange={(e) => setEditingNoteBody(e.target.value)}
                      />
                      <div className="flex gap-2">
                        <Button
                          size="sm"
                          disabled={!editingNoteBody.trim() || saveNote.isPending}
                          onClick={() => saveNote.mutate()}
                        >
                          {t('actions.save')}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setEditingNoteId(null)}
                        >
                          {t('actions.cancel')}
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <>
                      <p className="whitespace-pre-wrap">{n.body}</p>
                      <div className="mt-1 flex items-center justify-between text-xs text-muted-foreground">
                        <span>{new Date(n.createdAt).toLocaleString()}</span>
                        <span className="flex gap-1">
                          <button
                            className="hover:underline"
                            onClick={() => {
                              setEditingNoteId(n.id)
                              setEditingNoteBody(n.body)
                            }}
                          >
                            {t('actions.edit')}
                          </button>
                          <button
                            className="hover:underline"
                            onClick={() => {
                              if (window.confirm(t('detail.confirmDeleteNote')))
                                removeNote.mutate(n.id)
                            }}
                          >
                            {t('actions.delete')}
                          </button>
                        </span>
                      </div>
                    </>
                  )}
                </div>
              ))
            ) : (
              <p className="text-sm text-muted-foreground">
                {t('detail.noNotes')}
              </p>
            )}
          </CardContent>
        </Card>

        {/* Activity timeline */}
        <Card>
          <CardHeader>
            <CardTitle>{t('detail.timeline')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            {activities.data?.length ? (
              activities.data.map((a) => (
                <div key={a.id} className="flex flex-col text-sm">
                  <span className="font-medium">
                    {ACTIVITY_KEY[a.type]
                      ? t(`activity.${ACTIVITY_KEY[a.type]}`)
                      : a.summary}
                  </span>
                  <span className="text-xs text-muted-foreground">
                    {new Date(a.occurredAt).toLocaleString()} — {a.summary}
                  </span>
                </div>
              ))
            ) : (
              <p className="text-sm text-muted-foreground">
                {t('detail.noActivities')}
              </p>
            )}
          </CardContent>
        </Card>

        {/* Documents placeholder (Phase 3) */}
        <Card>
          <CardHeader>
            <CardTitle>{t('detail.documents')}</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground">
              {t('detail.documentsHint')}
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Contact add/edit dialog */}
      <Dialog
        open={contactOpen}
        onOpenChange={setContactOpen}
        title={
          editingContactId ? t('detail.editContact') : t('form.contact.add')
        }
      >
        <div className="grid grid-cols-2 gap-3">
          <ContactField
            label={t('form.contact.firstName')}
            value={contactDraft.firstName ?? ''}
            onChange={(v) => setContactDraft((d) => ({ ...d, firstName: v }))}
          />
          <ContactField
            label={t('form.contact.lastName')}
            value={contactDraft.lastName}
            onChange={(v) => setContactDraft((d) => ({ ...d, lastName: v }))}
          />
          <ContactField
            label={t('form.contact.email')}
            value={contactDraft.email ?? ''}
            onChange={(v) => setContactDraft((d) => ({ ...d, email: v }))}
          />
          <ContactField
            label={t('form.contact.phone')}
            value={contactDraft.phone ?? ''}
            onChange={(v) => setContactDraft((d) => ({ ...d, phone: v }))}
          />
          <ContactField
            label={t('form.contact.position')}
            value={contactDraft.position ?? ''}
            onChange={(v) => setContactDraft((d) => ({ ...d, position: v }))}
          />
          <label className="col-span-2 flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={contactDraft.isPrimary}
              onChange={(e) =>
                setContactDraft((d) => ({ ...d, isPrimary: e.target.checked }))
              }
            />
            {t('form.contact.isPrimary')}
          </label>
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => setContactOpen(false)}>
            {t('actions.cancel')}
          </Button>
          <Button
            disabled={!contactDraft.lastName.trim() || saveContact.isPending}
            onClick={() => saveContact.mutate()}
          >
            {t('actions.save')}
          </Button>
        </DialogFooter>
      </Dialog>
    </main>
  )
}

function Detail({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span>{value?.trim() ? value : '—'}</span>
    </div>
  )
}

function ContactField({
  label,
  value,
  onChange,
}: {
  label: string
  value: string
  onChange: (v: string) => void
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label>{label}</Label>
      <Input value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}
