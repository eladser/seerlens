import { useEffect, useState } from 'react'
import { deleteSet, getSet, saveSet } from '../api'
import type { GoldenCase } from '../types'

type Draft = { input: string; keywords: string; criteria: string }

const blank: Draft = { input: '', keywords: '', criteria: '' }

// Create or edit a golden set without touching JSON by hand. `name` null means a
// brand new set.
export function SetEditor({ name, onSaved, onClose }: {
  name: string | null
  onSaved: (name: string) => void
  onClose: () => void
}) {
  const [setName, setSetName] = useState(name ?? '')
  const [cases, setCases] = useState<Draft[]>([blank])
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!name) return
    getSet(name).then(s => {
      setCases(s.cases.map(c => ({
        input: c.input,
        keywords: (c.keywords ?? []).join(', '),
        criteria: c.criteria ?? '',
      })))
    }).catch(() => {})
  }, [name])

  function edit(i: number, patch: Partial<Draft>) {
    setCases(cs => cs.map((c, j) => (j === i ? { ...c, ...patch } : c)))
  }

  async function save() {
    const clean = setName.trim()
    if (!clean) { setErr('Give the set a name.'); return }
    const out: GoldenCase[] = cases
      .filter(c => c.input.trim())
      .map((c, i) => ({
        id: `case-${i + 1}`,
        input: c.input.trim(),
        keywords: c.keywords.split(',').map(k => k.trim()).filter(Boolean),
        criteria: c.criteria.trim() || null,
      }))
    if (out.length === 0) { setErr('Add at least one case with a question.'); return }

    setBusy(true)
    setErr(null)
    try {
      await saveSet(clean, out)
      onSaved(clean)
    } catch {
      setErr('Save failed.')
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!name) return onClose()
    if (!confirm(`Delete the "${name}" set?`)) return
    await deleteSet(name).catch(() => {})
    onSaved('')
  }

  return (
    <div className="set-editor">
      <div className="run-bar">
        <input
          className="models-input"
          value={setName}
          onChange={e => setSetName(e.target.value)}
          placeholder="set name, e.g. support"
          disabled={!!name}
          spellCheck={false}
        />
        <button className="run-btn" onClick={save} disabled={busy}>{busy ? 'saving…' : 'Save set'}</button>
        <button className="ghost-btn" onClick={onClose}>Cancel</button>
        {name && <button className="ghost-btn danger" onClick={remove}>Delete</button>}
        {err && <span className="muted bad">{err}</span>}
      </div>

      {cases.map((c, i) => (
        <div key={i} className="case-edit">
          <div className="case-edit-head">
            <span className="muted">case {i + 1}</span>
            {cases.length > 1 && (
              <button className="x" onClick={() => setCases(cs => cs.filter((_, j) => j !== i))}>remove</button>
            )}
          </div>
          <textarea
            className="prompt-input"
            value={c.input}
            onChange={e => edit(i, { input: e.target.value })}
            placeholder="A real question your app handles"
            rows={2}
          />
          <input
            className="models-input wide"
            value={c.keywords}
            onChange={e => edit(i, { keywords: e.target.value })}
            placeholder="keywords a good answer must contain (comma separated)"
            spellCheck={false}
          />
          <input
            className="models-input wide"
            value={c.criteria}
            onChange={e => edit(i, { criteria: e.target.value })}
            placeholder="plain-English rubric for the LLM judge (optional)"
          />
        </div>
      ))}

      <button className="ghost-btn" onClick={() => setCases(cs => [...cs, blank])}>+ add case</button>
    </div>
  )
}
