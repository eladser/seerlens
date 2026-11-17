import { describe, expect, it } from 'vitest'
import { defaultSet } from './evals'
import type { EvalRun } from './types'

const run = (set: string, createdAt: number): EvalRun => ({
  id: `${set}-${createdAt}`, set, target: 'gpt-4o', scorer: 'keyword', createdAt, score: 1, caseCount: 1,
})

describe('defaultSet', () => {
  it('takes the set of the most recent run', () => {
    const runs = [run('general', 100), run('support', 300), run('agent', 200)]
    expect(defaultSet(runs, ['agent', 'general', 'structured', 'support'])).toBe('support')
  })

  it('ignores where the newest run sits in the list', () => {
    // the api returns runs oldest first
    expect(defaultSet([run('general', 100), run('agent', 200)], ['general'])).toBe('agent')
    expect(defaultSet([run('agent', 200), run('general', 100)], ['general'])).toBe('agent')
  })

  it('falls back to the first set when there are no runs', () => {
    expect(defaultSet([], ['agent', 'general'])).toBe('agent')
  })

  it('is null with no runs and no sets', () => {
    expect(defaultSet([], [])).toBeNull()
  })
})
