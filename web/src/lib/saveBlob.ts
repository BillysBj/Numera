/** Trigger a browser download for an authenticated API response. */
export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename.replace(/[\\/:*?"<>|]/g, '-')
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  // Allow the browser to start consuming the object URL before releasing it.
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}
