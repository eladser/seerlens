import { useEffect, useState } from 'react'
import { getConfig, setBudget } from '../api'
import type { Config } from '../types'

// One place to see how the collector is wired up and set the spend budget. The
// provider key stays in the environment on purpose, so this page shows status,
// it doesn't take secrets.
export function SettingsView() {
  const [cfg, setCfg] = useState<Config | null>(null)
  const [draft, setDraft] = useState('')
  const [saved, setSaved] = useState(false)

  const load = () => getConfig().then(c => {
    setCfg(c)
    setDraft(c.budget.monthlyUsd != null ? String(c.budget.monthlyUsd) : '')
  }).catch(() => {})

  useEffect(() => { load() }, [])

  async function save() {
    const n = parseFloat(draft)
    await setBudget({ monthlyUsd: Number.isFinite(n) && n > 0 ? n : null, alertPerCallUsd: null }).catch(() => {})
    setSaved(true)
    setTimeout(() => setSaved(false), 1500)
    load()
  }

  if (!cfg) return <div className="empty">Loading…</div>

  return (
    <div className="settings">
      <section className="settings-block">
        <h3>Provider</h3>
        <Row label="status" value={cfg.providerConfigured ? 'configured' : 'not set'} ok={cfg.providerConfigured} />
        <Row label="model" value={cfg.model} />
        {cfg.endpoint && <Row label="endpoint" value={cfg.endpoint} />}
        {!cfg.providerConfigured && (
          <p className="muted hint">
            Set <code>SEERLENS_AI_BASE_URL</code>, <code>SEERLENS_AI_KEY</code> and <code>SEERLENS_AI_MODEL</code> in
            the environment or a <code>.env.local</code> to run evals from here. The key stays out of the UI by design.
          </p>
        )}
      </section>

      <section className="settings-block">
        <h3>Budget</h3>
        <div className="budget-set">
          <label className="muted">monthly cap (USD)</label>
          <input value={draft} onChange={e => setDraft(e.target.value)} placeholder="e.g. 50" inputMode="decimal" />
          <button className="run-btn" onClick={save}>{saved ? 'saved' : 'Save'}</button>
          <span className="muted run-note">blank clears it</span>
        </div>
        <p className="muted hint">The Cost tab shows month-to-date against this and warns when you get close.</p>
      </section>

      <section className="settings-block">
        <h3>Golden sets</h3>
        <Row label="location" value={cfg.evalsDir} />
        <Row label="sets" value={String(cfg.setCount)} />
      </section>

      <section className="settings-block">
        <h3>Pricing</h3>
        <Row label="override file" value={cfg.pricingOverride ? 'loaded' : 'built-in prices'} />
        <p className="muted hint">
          Point <code>SEERLENS_PRICING_FILE</code> at a JSON of <code>{'{ "model": { "in": 1.0, "out": 2.0 } }'}</code> to
          keep dollar costs current when prices change.
        </p>
      </section>
    </div>
  )
}

function Row({ label, value, ok }: { label: string; value: string; ok?: boolean }) {
  return (
    <div className="settings-row">
      <span className="muted">{label}</span>
      <span className={ok === undefined ? 'mono' : ok ? 'mono good' : 'mono bad'}>{value}</span>
    </div>
  )
}
