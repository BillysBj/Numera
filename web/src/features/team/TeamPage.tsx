import { useMemo, useState } from 'react'
import { useForm, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { UpgradeHint } from '@/features/shared/UpgradeHint'
import { hasCapability, useEntitlements } from '@/lib/entitlements'
import {
  MembershipRole,
  TeamApiError,
  useChangeRole,
  useInvite,
  useRemoveMember,
  useTeam,
  type MembershipRoleValue,
} from './team'

interface InviteValues {
  email: string
  role: MembershipRoleValue
}

const roles = Object.values(MembershipRole) as MembershipRoleValue[]

export default function TeamPage() {
  const { t } = useTranslation('team')
  const entitlements = useEntitlements()
  const multiUser = hasCapability(entitlements.data?.capabilities, 'MultiUser')
  const team = useTeam(multiUser)
  const invite = useInvite()
  const changeRole = useChangeRole()
  const remove = useRemoveMember()
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [invited, setInvited] = useState<{ email: string; password: string } | null>(null)

  const schema = useMemo(() => z.object({
    email: z.email(t('errors.invalidEmail')),
    role: z.number().refine(
      (value) => roles.includes(value as MembershipRoleValue),
      t('errors.invalidRole'),
    ),
  }), [t])
  const form = useForm<InviteValues>({
    resolver: zodResolver(schema) as unknown as Resolver<InviteValues>,
    defaultValues: { email: '', role: MembershipRole.Employee },
  })

  const ownerCount = team.data?.filter(
    (member) => member.role === MembershipRole.Owner,
  ).length ?? 0

  const messageFor = (caught: unknown) => {
    if (caught instanceof TeamApiError) {
      if (caught.code === 'last_owner' || caught.status === 409 || caught.status === 422) {
        return t('errors.lastOwner')
      }
      if (caught.code === 'upgrade_required' || caught.status === 403) {
        return t('errors.upgradeRequired')
      }
    }
    return t('errors.server')
  }

  const submit = form.handleSubmit(async (values) => {
    setError(null)
    setStatus(null)
    try {
      const result = await invite.mutateAsync(values)
      form.reset()
      setStatus(t('invite.success'))
      setInvited(
        result.temporaryPassword
          ? { email: values.email, password: result.temporaryPassword }
          : null,
      )
    } catch (caught) {
      setError(messageFor(caught))
    }
  })

  const updateRole = async (userId: string, role: MembershipRoleValue) => {
    setError(null)
    setStatus(null)
    try {
      await changeRole.mutateAsync({ userId, role })
      setStatus(t('members.roleChanged'))
    } catch (caught) {
      setError(messageFor(caught))
    }
  }

  const removeMember = async (userId: string) => {
    setError(null)
    setStatus(null)
    try {
      await remove.mutateAsync(userId)
      setStatus(t('members.removed'))
    } catch (caught) {
      setError(messageFor(caught))
    }
  }

  if (entitlements.isLoading) {
    return <main className="app-main"><p>{t('loading')}</p></main>
  }

  return (
    <main className="app-main app-main--wide">
      <h1 className="text-2xl font-semibold">{t('title')}</h1>
      <p className="mt-1 text-sm text-muted-foreground">{t('description')}</p>

      {!multiUser ? (
        <UpgradeHint className="mt-5" />
      ) : (
        <>
          <Card className="mt-5">
            <CardHeader><CardTitle>{t('invite.title')}</CardTitle></CardHeader>
            <CardContent>
              <form onSubmit={submit} className="grid gap-4 md:grid-cols-[1fr_220px_auto] md:items-end">
                <label className="flex flex-col gap-1.5">
                  <Label htmlFor="team-email">{t('invite.email')}</Label>
                  <Input id="team-email" type="email" {...form.register('email')} />
                  {form.formState.errors.email && (
                    <span className="text-sm text-destructive">
                      {form.formState.errors.email.message}
                    </span>
                  )}
                </label>
                <label className="flex flex-col gap-1.5">
                  <Label htmlFor="team-role">{t('invite.role')}</Label>
                  <Select
                    id="team-role"
                    {...form.register('role', { setValueAs: Number })}
                  >
                    {roles.map((role) => (
                      <option key={role} value={role}>{t(`roles.${role}`)}</option>
                    ))}
                  </Select>
                </label>
                <Button type="submit" disabled={invite.isPending}>
                  {invite.isPending ? t('invite.submitting') : t('invite.submit')}
                </Button>
              </form>
            </CardContent>
          </Card>

          {error && <p role="alert" className="mt-4 text-sm text-destructive">{error}</p>}
          {status && <p role="status" className="mt-4 text-sm text-emerald-600">{status}</p>}

          {invited && (
            <div className="mt-4 rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-amber-900 dark:text-amber-200">
              <p className="font-medium">Zugang für {invited.email} erstellt.</p>
              <p className="mt-1">
                Temporäres Passwort (wird nur einmal angezeigt — bitte sicher weitergeben):
              </p>
              <code className="mt-1.5 inline-block select-all rounded bg-background px-2 py-1 font-mono text-base">
                {invited.password}
              </code>
              <p className="mt-2 text-xs">
                Die Person meldet sich mit E-Mail + diesem Passwort an und muss beim ersten Login ein
                eigenes Passwort vergeben.
              </p>
            </div>
          )}

          <Card className="mt-5">
            <CardHeader><CardTitle>{t('members.title')}</CardTitle></CardHeader>
            <CardContent>
              {team.isLoading && <p>{t('loading')}</p>}
              {team.isError && <p className="text-destructive">{t('errors.load')}</p>}
              {team.data && (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{t('members.user')}</TableHead>
                      <TableHead>{t('members.role')}</TableHead>
                      <TableHead>{t('members.changeRole')}</TableHead>
                      <TableHead className="text-right">{t('members.actions')}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {team.data.map((member) => {
                      const lastOwner =
                        member.role === MembershipRole.Owner && ownerCount === 1
                      return (
                        <TableRow key={member.userId}>
                          <TableCell className="text-sm">{member.email ?? member.userId}</TableCell>
                          <TableCell><Badge variant="secondary">{t(`roles.${member.role}`)}</Badge></TableCell>
                          <TableCell>
                            <Select
                              aria-label={t('members.changeRole')}
                              value={member.role}
                              disabled={changeRole.isPending}
                              onChange={(event) => void updateRole(
                                member.userId,
                                Number(event.target.value) as MembershipRoleValue,
                              )}
                            >
                              {roles.map((role) => (
                                <option
                                  key={role}
                                  value={role}
                                  disabled={lastOwner && role !== MembershipRole.Owner}
                                >
                                  {t(`roles.${role}`)}
                                </option>
                              ))}
                            </Select>
                          </TableCell>
                          <TableCell className="text-right">
                            <Button
                              type="button"
                              variant="destructive"
                              disabled={lastOwner || remove.isPending}
                              onClick={() => void removeMember(member.userId)}
                            >
                              {t('members.remove')}
                            </Button>
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </main>
  )
}
