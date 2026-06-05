export function money(n: number | null): string {
  if (n == null) return 'n/a'
  if (n === 0) return '$0'
  if (n < 0.01) return '$' + n.toFixed(6)
  return '$' + n.toFixed(4)
}

export function dur(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(2)} s`
}

export function tokens(prompt: number | null, completion: number | null): string {
  if (prompt == null && completion == null) return 'n/a'
  return `${prompt ?? 0} / ${completion ?? 0}`
}

export function ago(unixMs: number): string {
  const s = Math.max(0, (Date.now() - unixMs) / 1000)
  if (s < 5) return 'just now'
  if (s < 60) return `${Math.floor(s)}s ago`
  if (s < 3600) return `${Math.floor(s / 60)}m ago`
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`
  return new Date(unixMs).toLocaleDateString()
}
