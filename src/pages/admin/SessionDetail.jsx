import React, { useEffect, useState, useContext } from 'react'
import { useParams, Link } from 'react-router-dom'
import { API_BASE } from '@/config'
import { getAuthToken } from '@/lib/api'
import { AuthContext } from '@/contexts/AuthContext'

export default function AdminSessionDetail() {
  const { sessionId } = useParams()
  const auth = useContext(AuthContext)
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!auth?.loggedIn || !auth?.user?.isSuperUser) return
    let cancelled = false
    ;(async () => {
      setLoading(true)
      setError(null)
      try {
        const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
        const url = base
          ? `${base}/admin/sessions/${encodeURIComponent(sessionId)}`
          : `/admin/sessions/${encodeURIComponent(sessionId)}`
        const token = getAuthToken()
        const res = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
        if (!res.ok) {
          const txt = await res.text().catch(() => '')
          throw new Error(`Failed to fetch session: ${res.status} ${txt}`)
        }
        const js = await res.json()
        if (!cancelled) setData(js)
      } catch (err) {
        if (!cancelled) setError(err?.message || String(err))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [sessionId, auth?.loggedIn, auth?.user?.isSuperUser])

  if (!auth?.loggedIn || !auth?.user?.isSuperUser) {
    return (
      <div className="p-8">
        <h2 className="text-2xl font-bold">Access denied — supervisor only</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          You do not have permission to view this page.
        </p>
      </div>
    )
  }

  return (
    <div className="p-6">
  <div className="flex flex-col md:flex-row items-start justify-between mb-4 gap-4">
        <div>
          <h1 className="text-2xl font-bold">Session details</h1>
          <div className="text-sm text-muted-foreground">Session ID: {sessionId}</div>
        </div>
        <div className="flex items-center gap-3">
          <Link to="/admin/sessions" className="text-sm text-slate-600 hover:underline">
            Back to sessions
          </Link>
        </div>
      </div>

      {loading ? (
        <div>Loading session…</div>
      ) : error ? (
        <div className="text-red-600">Failed to load session: {error}</div>
      ) : !data ? (
        <div>No session data.</div>
      ) : (
        <div className="space-y-6">
          {/* Top summary card */}
          <div className="p-4 bg-white rounded shadow-sm flex flex-col md:flex-row items-start md:items-center justify-between">
            <div>
              <div className="text-lg font-semibold">{data.userFullName || data.userEmail}</div>
              <div className="text-sm text-muted-foreground">{data.userEmail}</div>
              <div className="text-sm mt-2">
                File:{' '}
                <span className="font-medium block truncate max-w-full">{data.caseName || data.originalFileName || '—'}</span>
              </div>
              <div className="text-sm text-muted-foreground">
                Created: {new Date(data.createdAt).toLocaleString()}
              </div>
            </div>
            <div className="md:text-right mt-4 md:mt-0">
              <div className="text-sm">Messages</div>
              <div className="text-2xl font-bold">
                {Array.isArray(data.messages) ? data.messages.length : 0}
              </div>
              <div className="mt-2 text-sm text-muted-foreground">
                Last:{' '}
                {data.messages && data.messages.length
                  ? new Date(data.messages[data.messages.length - 1].createdAt).toLocaleString()
                  : '—'}
              </div>
            </div>
          </div>

          {/* Actions removed: Download TXT/JSON and Copy transcript buttons removed per request */}

          {/* Messages */}
          <div className="space-y-4">
            {Array.isArray(data.messages) && data.messages.length > 0 ? (
              data.messages.map(m => (
                <article
                  key={m.id}
                  className={`p-4 rounded-lg shadow-sm ${m.role === 'assistant' ? 'bg-yellow-50 border border-yellow-100' : 'bg-white border border-slate-100'}`}
                >
                  <header className="flex items-center justify-between mb-2">
                    <div className="flex items-center gap-3">
                      <div className="w-9 h-9 rounded-full bg-[#125691] text-white grid place-items-center font-semibold">
                        {(m.authorName || m.userFullName || m.role || '')
                          .split(' ')
                          .map(s => s[0])
                          .join('')
                          .slice(0, 2)
                          .toUpperCase() || (m.role || '?').slice(0, 2).toUpperCase()}
                      </div>
                      <div>
                        <div className="text-sm font-medium">
                          {m.authorName ||
                            (m.role === 'user'
                              ? data.userFullName || data.userEmail
                              : m.role === 'assistant'
                                ? 'Assistant'
                                : m.role)}
                        </div>
                        <div className="text-xs text-muted-foreground">
                          {m.role.toUpperCase()} • {new Date(m.createdAt).toLocaleString()}
                        </div>
                      </div>
                    </div>
                    <div className="text-xs text-slate-600">
                      {m.citations ? `${m.citations.length || m.citations} citations` : ''}
                    </div>
                  </header>
                  <div className="whitespace-pre-wrap text-sm text-slate-800">{m.content}</div>
                  {(m.pagesUsed || (m.citations && m.citations.length)) && (
                    <footer className="mt-3 flex flex-wrap gap-2 text-xs">
                      {m.pagesUsed && (
                        <span className="px-2 py-1 bg-slate-100 rounded">Pages: {m.pagesUsed}</span>
                      )}
                      {m.citations && m.citations.length > 0 && (
                        <span className="px-2 py-1 bg-slate-100 rounded">
                          Citations: {m.citations.length}
                        </span>
                      )}
                    </footer>
                  )}
                </article>
              ))
            ) : (
              <div>No messages in this session.</div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
