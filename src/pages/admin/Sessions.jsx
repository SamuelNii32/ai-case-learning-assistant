import React, { useEffect, useState, useContext } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { API_BASE } from '@/config'
import { getAuthToken } from '@/lib/api'
import { AuthContext } from '@/contexts/AuthContext'

// Group sessions by user (email/name)
function groupSessionsByUser(sessions) {
  const map = new Map()

  for (const s of sessions || []) {
    const key = s.userEmail || s.userFullName || 'unknown'
    if (!map.has(key)) {
      map.set(key, {
        key,
        userFullName: s.userFullName || 'Unknown learner',
        userEmail: s.userEmail || '',
        sessions: [],
      })
    }
    map.get(key).sessions.push(s)
  }

  // Sort each student's sessions by createdAt (newest first)
  for (const group of map.values()) {
    group.sessions.sort((a, b) => {
      const aTime = a.createdAt ? new Date(a.createdAt).getTime() : 0
      const bTime = b.createdAt ? new Date(b.createdAt).getTime() : 0
      return bTime - aTime
    })
  }

  // Convert to array and sort students by their most recent session
  const groups = Array.from(map.values())
  groups.sort((a, b) => {
    const aLatest = a.sessions[0]?.createdAt
    const bLatest = b.sessions[0]?.createdAt
    const aTime = aLatest ? new Date(aLatest).getTime() : 0
    const bTime = bLatest ? new Date(bLatest).getTime() : 0
    return bTime - aTime
  })

  return groups
}

function AccessDenied() {
  return (
    <div className="p-8">
      <h2 className="text-2xl font-bold">Access denied — supervisor only</h2>
      <p className="mt-2 text-sm text-muted-foreground">
        You do not have permission to view this page.
      </p>
    </div>
  )
}

export default function AdminSessions() {
  const auth = useContext(AuthContext)
  const _navigate = useNavigate() // kept for future use

  console.log('AUTH DEBUG:', auth)

  const isInstructor =
    auth?.user?.role === 'instructor' ||
    auth?.user?.isSuperUser === true ||
    auth?.user?.isSuperUser === 'true'

  const [sessions, setSessions] = useState([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  // per-student collapsed state: { [emailOrKey]: true/false }
  const [collapsedByEmail, setCollapsedByEmail] = useState({})

  useEffect(() => {
    if (!auth?.loggedIn || !isInstructor) return
    let cancelled = false
    ;(async () => {
      setLoading(true)
      setError(null)
      try {
        const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
        const url = base ? `${base}/admin/sessions` : `/admin/sessions`
        const token = getAuthToken()
        const res = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
        if (!res.ok) {
          const txt = await res.text().catch(() => '')
          throw new Error(`Failed to fetch sessions: ${res.status} ${txt}`)
        }
        const js = await res.json()
        if (!cancelled) setSessions(js)
      } catch (err) {
        if (!cancelled) setError(err?.message || String(err))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [auth?.loggedIn, auth?.user?.role])

  if (!auth?.loggedIn || !isInstructor) {
    return <AccessDenied />
  }

  function exportCsv() {
    if (!sessions || sessions.length === 0) return
    const header = [
      'sessionId',
      'userFullName',
      'userEmail',
      'uploadId',
      'caseName',
      'originalFileName',
      'createdAt',
      'lastMessageAt',
      'messageCount',
    ]
    const rows = sessions.map(s => [
      s.sessionId,
      s.userFullName || '',
      s.userEmail || '',
      s.uploadId || '',
      s.caseName || '',
      s.originalFileName || '',
      s.createdAt || '',
      s.lastMessageAt || '',
      s.messageCount ?? 0,
    ])
    const csv = [header, ...rows]
      .map(r => r.map(v => `"${String(v).replace(/"/g, '""')}"`).join(','))
      .join('\n')
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'admin-sessions.csv'
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  }

  // 🔹 Build grouped data for the UI
  const groups = groupSessionsByUser(sessions)

  return (
    <div className="p-8">
      <h2 className="text-2xl font-bold">Access denied — supervisor only</h2>
        <div>
          <h1 className="text-2xl font-bold">Supervisor — Sessions</h1>
          <p className="text-sm text-slate-600 mt-1">
            View and export session activity across learners.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={exportCsv}
            className="inline-flex items-center gap-2 px-3 py-2 bg-slate-100 rounded text-sm"
          >
            Export CSV
          </button>
        </div>
      </div>

      {loading ? (
        <div>Loading sessions…</div>
      ) : error ? (
        <div className="text-red-600">Failed to load sessions: {error}</div>
      ) : groups.length === 0 ? (
        <div>No sessions found.</div>
      ) : (
        <div className="space-y-6">
          {groups.map(group => {
            const key = group.userEmail || group.key
            const isCollapsed = collapsedByEmail[key]

            return (
              <div key={key} className="space-y-2">
                {/* Student header row */}
                <button
                  type="button"
                  className="w-full flex items-center justify-between text-left"
                  onClick={() =>
                    setCollapsedByEmail(prev => ({
                      ...prev,
                      [key]: !prev[key],
                    }))
                  }
                >
                  <div>
                    <div className="text-sm font-semibold text-slate-900">{group.userFullName}</div>
                    {group.userEmail && (
                      <div className="text-xs text-slate-500">{group.userEmail}</div>
                    )}
                  </div>
                  <div className="flex items-center gap-2 text-xs text-slate-500">
                    <span>{group.sessions.length} session(s)</span>
                    <span className="text-slate-400">{isCollapsed ? '▸' : '▾'}</span>
                  </div>
                </button>

                {/* That student's sessions */}
                {!isCollapsed && (
                  <div className="space-y-3 mt-1">
                    {group.sessions.map(s => (
                      <Link
                        key={s.sessionId}
                        to={`/admin/sessions/${encodeURIComponent(s.sessionId)}`}
                        className="block bg-white border border-slate-100 rounded-lg shadow-sm hover:shadow-md p-4 transition-shadow"
                      >
                        <div className="grid gap-4 items-start md:grid-cols-[minmax(0,2.2fr)_minmax(0,1.3fr)_minmax(0,0.7fr)]">
                          <div className="min-w-0">
                            {/* We already show name/email in the header above, so just show case info here */}
                            <div className="mt-1">
                              {/* First line: prefer caseName, otherwise show originalFileName once */}
                              <div className="text-sm font-medium text-slate-800 truncate">
                                {s.caseName ? s.caseName : s.originalFileName || '—'}
                              </div>

                              {/* Second line: only if BOTH exist and are different */}
                              {s.caseName &&
                                s.originalFileName &&
                                s.caseName !== s.originalFileName && (
                                  <div className="text-xs text-slate-500">{s.originalFileName}</div>
                                )}
                            </div>
                          </div>

                          <div className="flex flex-col items-start gap-2">
                            <div className="text-xs text-slate-500">Created</div>
                            <div className="text-sm text-slate-700">
                              {s.createdAt ? new Date(s.createdAt).toLocaleString() : '—'}
                            </div>
                            <div className="mt-2 text-xs text-slate-500">Last message</div>
                            <div className="text-sm text-slate-700">
                              {s.lastMessageAt ? new Date(s.lastMessageAt).toLocaleString() : '—'}
                            </div>
                          </div>

                          <div className="flex flex-col items-start md:items-end">
                            <div className="text-xs text-slate-500">Messages</div>
                            <div className="mt-1 inline-flex items-center justify-center px-2 py-1 bg-slate-100 rounded text-sm font-medium">
                              {s.messageCount ?? 0}
                            </div>
                          </div>
                        </div>
                      </Link>
                    ))}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
