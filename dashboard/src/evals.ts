import type { EvalRun } from './types'

// The set the Evals tab opens on: the newest run's, else the first set on disk.
export function defaultSet(runs: EvalRun[], sets: string[]): string | null {
  let newest: EvalRun | null = null
  for (const r of runs) if (!newest || r.createdAt > newest.createdAt) newest = r
  return newest?.set ?? sets[0] ?? null
}
