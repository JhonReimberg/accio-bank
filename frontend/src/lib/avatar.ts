import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import { keys, useAccount } from './queries'

// Under ['account'], so anything that refreshes the account also refreshes the photo.
const photoKey = [...keys.account, 'photo']

function toDataUrl(blob: Blob) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(blob)
  })
}

/** The profile photo as a data URL, or null when the user has none (initials are shown instead). */
export function useAvatar(): string | null {
  const hasPhoto = useAccount().data?.hasPhoto ?? false
  const photo = useQuery({
    queryKey: photoKey,
    enabled: hasPhoto,
    staleTime: Infinity,
    queryFn: async () => toDataUrl(await api<Blob>('/account/photo', { blob: true })),
  })
  return hasPhoto ? (photo.data ?? null) : null
}

export function usePhotoActions() {
  const client = useQueryClient()
  const refresh = () => client.invalidateQueries({ queryKey: keys.account })
  const upload = useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData()
      form.append('file', await squarePhoto(file), 'foto.jpg')
      return api('/account/photo', { method: 'PUT', form })
    },
    onSuccess: refresh,
  })
  const remove = useMutation({ mutationFn: () => api('/account/photo', { method: 'DELETE' }), onSuccess: refresh })
  return { upload, remove }
}

/** Center-crops the picked image to a square JPEG, so uploads stay small (the API caps them at 2 MB). */
async function squarePhoto(file: File, size = 512): Promise<Blob> {
  if (!file.type.startsWith('image/')) throw new Error('Escolha uma imagem (JPG, PNG ou WebP).')
  const bitmap = await createImageBitmap(file)
  const side = Math.min(bitmap.width, bitmap.height)
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = size
  canvas.getContext('2d')!.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, size, size)
  bitmap.close()
  return new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('Não foi possível processar a imagem.'))), 'image/jpeg', 0.85))
}
