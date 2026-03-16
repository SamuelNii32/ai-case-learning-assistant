import React, { useEffect, useState, useContext } from 'react'
import { useParams, Link } from 'react-router-dom'
import { API_BASE } from '@/config'
import { getAuthToken } from '@/lib/api'
import { AuthContext } from '@/contexts/AuthContext'
import { Card } from '@/components/ui/card'

export default function AdminSessionDetail() {
  const { sessionId } = useParams()
  const auth = useContext(AuthContext)
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!auth?.loggedIn || auth?.user?.role !== 'instructor') return
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
  }, [sessionId, auth?.loggedIn, auth?.user?.role])

  if (!auth?.loggedIn || auth?.user?.role !== 'instructor') {
    return (
      <div className="min-h-screen bg-[#faf6f0] py-10">
        <div className="max-w-5xl mx-auto px-4">
          <div className="p-6 md:p-8 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm">
            <h2 className="text-2xl font-bold text-[#2c2218]">Access denied — supervisor only</h2>
            <p className="mt-2 text-sm text-[#7a5c3c]">
              You do not have permission to view this page.
            </p>
          </div>
        </div>
      </div>
    )
  }

  const initials = name =>
    (name || '')
      .split(' ')
      .map(s => s[0])
      .join('')
      .slice(0, 2)
      .toUpperCase()

  return (
    <div className="min-h-screen bg-[#faf6f0] py-10">
      <div className="max-w-5xl mx-auto px-4 space-y-6">
        <Card className="p-6 md:p-7 bg-white border border-[#f4e7d8] rounded-[12px] shadow-[0_25px_45px_rgba(32,20,8,0.08)]">
            <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
              <div>
                <h1 className="text-2xl font-bold text-[#2c2218]">Session details</h1>
              </div>
            <Link
              to="/admin/sessions"
              className="text-sm font-semibold text-[#C96A08] hover:text-[#a65b07]"
            >
              ← Back to sessions
            </Link>
          </div>
        </Card>

        {loading ? (
          <Card className="p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm text-center">
            <p className="text-[#7a5c3e]">Loading session…</p>
          </Card>
        ) : error ? (
          <Card className="p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm text-center">
            <p className="text-[#c76008]">Failed to load session: {error}</p>
          </Card>
        ) : !data ? (
          <Card className="p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm text-center">
            <p className="text-[#7a5c3e]">No session data.</p>
          </Card>
        ) : (
          <div className="space-y-6">
            <Card className="p-5 md:p-6 bg-white border border-[#f4e7d8] rounded-[12px] shadow-sm">
              <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
                <div>
                  <div className="text-lg font-semibold text-[#2c2218]">
                    {data.userFullName || data.userEmail}
                  </div>
                  <div className="text-sm text-[#7a5c3c]">{data.userEmail}</div>
                  <div className="text-sm mt-2 text-[#2c2218]">
                    File:{' '}
                    <span className="font-medium block truncate max-w-full">
                      {data.caseName || data.originalFileName || '—'}
                    </span>
                  </div>
                  <div className="text-sm text-[#7a5c3c]">
                    Created: {data.createdAt ? new Date(data.createdAt).toLocaleString() : '—'}
                  </div>
                </div>
                <div className="md:text-right mt-4 md:mt-0">
                  <div className="text-sm text-[#7a5c3c]">Messages</div>
                  <div className="text-3xl font-bold text-[#2c2218]">
                    {Array.isArray(data.messages) ? data.messages.length : 0}
                  </div>
                  <div className="mt-2 text-sm text-[#7a5c3c]">
                    Last:{' '}
                    {data.messages && data.messages.length
                      ? new Date(data.messages[data.messages.length - 1].createdAt).toLocaleString()
                      : '—'}
                  </div>
                </div>
              </div>
            </Card>

            <div className="space-y-4">
              {Array.isArray(data.messages) && data.messages.length > 0 ? (
                data.messages.map(m => (
                  <article
                    key={m.id}
                    className={`p-5 rounded-[12px] border border-[#f3e0ce] shadow-sm transition-shadow hover:shadow-[0_12px_30px_rgba(32,20,8,0.12)] ${
                      m.role === 'assistant' ? 'bg-[#fdf4eb]' : 'bg-white'
                    }`}
                  >
                    <header className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between mb-2">
                      <div className="flex items-center gap-3">
                        <div className="w-10 h-10 rounded-full bg-[#C96A08] text-white grid place-items-center text-sm font-semibold">
                          {initials(m.authorName || (m.role === 'user' ? data.userFullName || data.userEmail : m.role))}
                        </div>
                        <div>
                          <div className="text-sm font-medium text-[#2c2218]">
                            {m.authorName ||
                              (m.role === 'user'
                                ? data.userFullName || data.userEmail
                                : m.role === 'assistant'
                                  ? 'Assistant'
                                  : m.role)}
                          </div>
                          <div className="text-xs text-[#7a5c3c]">
                            {m.role?.toUpperCase()} • {new Date(m.createdAt).toLocaleString()}
                          </div>
                        </div>
                      </div>
                      {m.citations ? (
                        <div className="text-xs text-[#7a5c3c]">
                          {`${m.citations.length || m.citations} citations`}
                        </div>
                      ) : null}
                    </header>
                    <div className="whitespace-pre-wrap text-sm text-[#2c2218]">
                      {m.content}
                    </div>
                    {(m.pagesUsed || (m.citations && m.citations.length)) && (
                      <footer className="mt-3 flex flex-wrap gap-2 text-xs text-[#7a5c3c]">
                        {m.pagesUsed && (
                          <span className="px-2 py-1 bg-[#fdf4eb] border border-[#f3e0ce] rounded">
                            Pages: {m.pagesUsed}
                          </span>
                        )}
                        {m.citations && m.citations.length > 0 && (
                          <span className="px-2 py-1 bg-[#fdf4eb] border border-[#f3e0ce] rounded">
                            Citations: {m.citations.length}
                          </span>
                        )}
                      </footer>
                    )}
                  </article>
                ))
              ) : (
                <Card className="p-5 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm">
                  <p className="text-sm text-[#7a5c3c]">No messages in this session.</p>
                </Card>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
