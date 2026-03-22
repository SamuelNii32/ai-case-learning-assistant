import React, { useEffect, useState, useContext } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { API_BASE } from '@/config'
import { getAuthToken } from '@/lib/api'
import { AuthContext } from '@/contexts/AuthContext'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'

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
    <div className="p-6 md:p-8 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm">
      <h2 className="text-2xl font-bold text-[#2c2218]">Access denied — supervisor only</h2>
      <p className="mt-2 text-sm text-[#7a5c3c]">You do not have permission to view this page.</p>
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
        if (!base) throw new Error('API_BASE is empty in this build')
        const url = `${base}/admin/sessions`
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
  }, [auth?.loggedIn, isInstructor])

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
    <div className="min-h-screen bg-[#faf6f0] py-10">
      <div className="max-w-7xl mx-auto px-4 space-y-6">
        <div className="bg-white border border-[#f4e7d8] shadow-[0_25px_45px_rgba(32,20,8,0.08)] rounded-[12px] p-6 md:p-8 space-y-6">
          <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
            <div>
              <h1 className="text-2xl md:text-3xl font-bold text-[#2c2218]">
                Supervisor — Sessions
              </h1>
              <p className="text-sm text-[#5C4C3C] mt-1">
                View and export session activity across learners.
              </p>
            </div>
            <Button variant="warm" onClick={exportCsv} className="inline-flex items-center gap-2">
              Export CSV
            </Button>
          </div>

          <div className="border-b border-[#E8DDD0] bg-white px-1">
            <div className="flex flex-wrap gap-6 text-sm font-semibold text-[#5C4C3C]">
              <button type="button" className="pb-3 text-[#C96A08] border-b-2 border-[#C96A08]">
                Sessions
              </button>
              <button type="button" className="pb-3 text-[#7a5c3e]">
                History
              </button>
            </div>
          </div>

          {loading ? (
            <div className="bg-white border border-[#f3e0ce] rounded-2xl p-6 text-center shadow-sm">
              <p className="text-[#7a5c3e]">Loading sessions…</p>
            </div>
          ) : error ? (
            <div className="bg-white border border-[#f3e0ce] rounded-2xl p-6 text-center shadow-sm">
              <p className="text-[#c76008]">Failed to load sessions: {error}</p>
            </div>
          ) : groups.length === 0 ? (
            <div className="bg-white border border-[#f3e0ce] rounded-2xl p-6 text-center shadow-sm">
              <p className="text-[#7a5c3e]">No sessions found.</p>
            </div>
          ) : (
            <div className="space-y-5">
              {groups.map(group => {
                const key = group.userEmail || group.key
                const isCollapsed = collapsedByEmail[key]

                return (
                  <Card
                    key={key}
                    className="p-5 md:p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm transition-shadow hover:shadow-md"
                  >
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <div className="text-lg font-semibold text-[#2c2218]">
                          {group.userFullName}
                        </div>
                        {group.userEmail && (
                          <div className="text-sm text-[#7a5c3c]">{group.userEmail}</div>
                        )}
                      </div>
                      <button
                        type="button"
                        onClick={() =>
                          setCollapsedByEmail(prev => ({
                            ...prev,
                            [key]: !prev[key],
                          }))
                        }
                        className="text-sm font-semibold text-[#5C4C3C]"
                      >
                        {group.sessions.length} session{group.sessions.length !== 1 ? 's' : ''}
                        <span className="ml-2 text-[#c76008]">{isCollapsed ? '▸' : '▾'}</span>
                      </button>
                    </div>

                    {!isCollapsed && (
                      <div className="mt-5 space-y-4">
                        {group.sessions.map(s => (
                          <Link
                            key={s.sessionId}
                            to={`/admin/sessions/${encodeURIComponent(s.sessionId)}`}
                            className="block border border-[#f4e7d8] rounded-[12px] p-4 bg-white shadow-sm transition-shadow hover:shadow-[0_12px_30px_rgba(32,20,8,0.12)]"
                          >
                            <div className="grid gap-4 items-start md:grid-cols-[minmax(0,2.2fr)_minmax(0,1.3fr)_minmax(0,0.7fr)]">
                              <div className="min-w-0">
                                <div className="text-sm font-medium text-[#2c2218] truncate">
                                  {s.caseName ? s.caseName : s.originalFileName || '—'}
                                </div>
                                {s.caseName &&
                                  s.originalFileName &&
                                  s.caseName !== s.originalFileName && (
                                    <div className="text-xs text-[#7a5c3c]">
                                      {s.originalFileName}
                                    </div>
                                  )}
                              </div>

                              <div className="flex flex-col items-start gap-2">
                                <div className="text-xs text-[#7a5c3c]">Created</div>
                                <div className="text-sm text-[#2c2218]">
                                  {s.createdAt ? new Date(s.createdAt).toLocaleString() : '—'}
                                </div>
                                <div className="mt-2 text-xs text-[#7a5c3c]">Last message</div>
                                <div className="text-sm text-[#2c2218]">
                                  {s.lastMessageAt
                                    ? new Date(s.lastMessageAt).toLocaleString()
                                    : '—'}
                                </div>
                              </div>

                              <div className="flex flex-col items-start md:items-end">
                                <div className="text-xs text-[#7a5c3c]">Messages</div>
                                <div className="mt-1 inline-flex items-center justify-center px-2 py-1 bg-[#fdf4eb] border border-[#f3e0ce] rounded text-sm font-medium text-[#2c2218]">
                                  {s.messageCount ?? 0}
                                </div>
                              </div>
                            </div>
                          </Link>
                        ))}
                      </div>
                    )}
                  </Card>
                )
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
