import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

/** Mirrors Numera.Modules.Crm.PartnerTaskStatus. */
export const PartnerTaskStatus = {
  Open: 0,
  Done: 1,
} as const

export type PartnerTaskStatusValue =
  (typeof PartnerTaskStatus)[keyof typeof PartnerTaskStatus]

export interface PartnerTask {
  id: string
  title: string
  description: string | null
  dueDate: string | null
  status: PartnerTaskStatusValue
  assignedUserId: string | null
  isOverdue: boolean
}

export interface PartnerTaskWrite {
  title: string
  description: string | null
  dueDate: string | null
  assignedUserId: string | null
}

export interface PartnerTaskUpdate extends PartnerTaskWrite {
  status: PartnerTaskStatusValue
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })
  if (!response.ok) throw new Error(`Task request failed: ${response.status}`)
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

const taskKey = (partnerId: string) => ['partner-tasks', partnerId] as const

export function useTasks(
  partnerId: string,
  filters: { openOnly?: boolean; overdueOnly?: boolean },
) {
  const params = new URLSearchParams()
  if (filters.openOnly) params.set('openOnly', 'true')
  if (filters.overdueOnly) params.set('overdueOnly', 'true')
  const query = params.toString()
  return useQuery({
    queryKey: [...taskKey(partnerId), filters.openOnly, filters.overdueOnly],
    queryFn: () => request<PartnerTask[]>(
      `/api/partners/${partnerId}/tasks${query ? `?${query}` : ''}`,
    ),
    enabled: Boolean(partnerId),
  })
}

export function useCreateTask(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: PartnerTaskWrite) =>
      request<PartnerTask>(`/api/partners/${partnerId}/tasks`, {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: taskKey(partnerId) }),
  })
}

export function useUpdateTask(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...input }: PartnerTaskUpdate & { id: string }) =>
      request<void>(`/api/partners/${partnerId}/tasks/${id}`, {
        method: 'PUT',
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: taskKey(partnerId) }),
  })
}

export function useDeleteTask(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) =>
      request<void>(`/api/partners/${partnerId}/tasks/${id}`, {
        method: 'DELETE',
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: taskKey(partnerId) }),
  })
}
