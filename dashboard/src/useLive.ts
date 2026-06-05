import { useEffect, useRef, useState } from 'react'
import { getTraces } from './api'
import type { TraceSummary } from './types'

// Loads the trace list once, then keeps it fresh from the SSE feed.
// Returns the list plus the id of whatever arrived last, so the UI can flash it.
export function useLive() {
  const [traces, setTraces] = useState<TraceSummary[]>([])
  const [latestId, setLatestId] = useState<string | null>(null)
  const seen = useRef(new Set<string>())

  useEffect(() => {
    let live = true

    getTraces().then(initial => {
      if (!live) return
      initial.forEach(t => seen.current.add(t.id))
      setTraces(initial)
    })

    const es = new EventSource('/events')
    es.onmessage = e => {
      const t: TraceSummary = JSON.parse(e.data)
      if (seen.current.has(t.id)) return
      seen.current.add(t.id)
      setTraces(prev => [t, ...prev])
      setLatestId(t.id)
    }

    return () => {
      live = false
      es.close()
    }
  }, [])

  return { traces, latestId }
}
