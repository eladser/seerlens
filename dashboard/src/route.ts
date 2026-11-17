// Only /diff lives in the URL; every other view is plain state.
export type DiffRoute = { a: string; b: string }

export function diffUrl(a: string, b: string): string {
  return `/diff?a=${encodeURIComponent(a)}&b=${encodeURIComponent(b)}`
}

export function parseRoute(pathname: string, search: string): DiffRoute | null {
  if (pathname.replace(/\/+$/, '') !== '/diff') return null
  const q = new URLSearchParams(search)
  return { a: q.get('a') ?? '', b: q.get('b') ?? '' }
}

export function pushDiff(a: string, b: string, replace = false) {
  const state = { diff: true }
  if (replace) history.replaceState(state, '', diffUrl(a, b))
  else history.pushState(state, '', diffUrl(a, b))
}

export const evalRef = (runId: string, idx: number) => `eval:${runId}:${idx}`
