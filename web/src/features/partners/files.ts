import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

export const MAX_CUSTOMER_FILE_BYTES = 20 * 1024 * 1024

export const ALLOWED_CUSTOMER_FILE_TYPES = [
  'application/pdf',
  'image/png',
  'image/jpeg',
  'text/plain',
  'application/msword',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.ms-excel',
] as const

export interface CustomerFile {
  id: string
  fileName: string
  contentType: string
  byteSize: number
  uploadedByUserId: string | null
  uploadedAt: string
}

interface UploadedCustomerFile {
  id: string
  fileName: string
  byteSize: number
}

const fileKey = (partnerId: string) => ['partner-files', partnerId] as const

async function listFiles(partnerId: string): Promise<CustomerFile[]> {
  const response = await fetch(`/api/partners/${partnerId}/files`, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) throw new Error(`File request failed: ${response.status}`)
  return response.json() as Promise<CustomerFile[]>
}

async function uploadFile(
  partnerId: string,
  file: File,
): Promise<UploadedCustomerFile> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch(`/api/partners/${partnerId}/files`, {
    method: 'POST',
    credentials: 'include',
    headers: { Accept: 'application/json' },
    body: form,
  })
  if (!response.ok) throw new Error(`File upload failed: ${response.status}`)
  return response.json() as Promise<UploadedCustomerFile>
}

export function customerFileDownloadUrl(partnerId: string, fileId: string) {
  return `/api/partners/${partnerId}/files/${fileId}`
}

export function useFiles(partnerId: string) {
  return useQuery({
    queryKey: fileKey(partnerId),
    queryFn: () => listFiles(partnerId),
    enabled: Boolean(partnerId),
  })
}

export function useUploadFile(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => uploadFile(partnerId, file),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: fileKey(partnerId) }),
  })
}
