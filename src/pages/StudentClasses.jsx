import React, { useEffect, useState, useContext } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import { getEnrolledClasses, joinClass } from '@/lib/api'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Users, BookOpen } from 'lucide-react'
import toast from 'react-hot-toast'

export default function StudentClasses() {
  const navigate = useNavigate()
  const auth = useContext(AuthContext)
  const [loading, setLoading] = useState(true)
  const [classes, setClasses] = useState([])
  const [loadError, setLoadError] = useState('')
  const [joinCode, setJoinCode] = useState('')
  const [joining, setJoining] = useState(false)

  async function loadClasses() {
    try {
      setLoading(true)
      setLoadError('')
      const data = await getEnrolledClasses()
      setClasses(Array.isArray(data) ? data : [])
    } catch (err) {
      console.error('Failed to load enrolled classes', err)
      setLoadError(err?.message || 'Failed to load your classes')
      toast.error('Failed to load your classes')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadClasses()
  }, [])

  async function handleJoinClass(e) {
    e.preventDefault()
    const code = joinCode.trim()
    if (!code) {
      toast.error('Enter a class code')
      return
    }

    try {
      setJoining(true)
      await joinClass(code)
      setJoinCode('')
      toast.success('Joined class')
      await loadClasses()
    } catch (err) {
      toast.error(err?.message || 'Could not join class')
    } finally {
      setJoining(false)
    }
  }

  if (!auth?.loggedIn) {
    return (
      <div className="p-6 md:p-8">
        <h2 className="text-xl md:text-2xl font-bold">Please sign in</h2>
        <p className="mt-2 text-sm text-slate-600">You need to be signed in to view your classes.</p>
      </div>
    )
  }

  return (
    <div className="p-4 md:p-6 max-w-7xl mx-auto">
      <header className="mb-6">
        <h1 className="text-3xl font-semibold text-[#2C2218]">My Classes</h1>
        <p className="text-sm text-[#5C4C3C] mt-1">
          View the classes you are enrolled in and access their case studies.
        </p>
      </header>

      <Card className="mb-6 p-4 md:p-5">
        <form onSubmit={handleJoinClass} className="flex flex-col gap-3 md:flex-row md:items-end">
          <div className="flex-1">
            <label htmlFor="classJoinCode" className="text-sm font-medium text-[#2C2218]">
              Join a class
            </label>
            <p className="mb-2 text-xs text-[#5C4C3C]">
              Enter the class code your instructor gave you.
            </p>
            <Input
              id="classJoinCode"
              value={joinCode}
              onChange={e => setJoinCode(e.target.value)}
              placeholder="Example: CP4K8QZ"
            />
          </div>
          <Button type="submit" variant="warm" disabled={joining}>
            {joining ? 'Joining...' : 'Join class'}
          </Button>
        </form>
      </Card>

      {loading ? (
        <div className="bg-white border border-slate-200 rounded-lg p-6 md:p-8 text-center">
          <p className="text-slate-500">Loading your classes…</p>
        </div>
      ) : loadError ? (
        <div className="bg-white border border-red-200 rounded-lg p-6 md:p-8 text-center">
          <p className="font-medium text-red-700">Could not load your classes.</p>
          <p className="mt-1 text-sm text-slate-600">
            Check your connection and try again. If this keeps happening, sign out and sign back in.
          </p>
          <Button type="button" variant="outline" className="mt-4" onClick={loadClasses}>
            Retry
          </Button>
        </div>
      ) : classes.length === 0 ? (
        <div className="bg-white border border-slate-200 rounded-lg p-6 md:p-8 text-center">
          <p className="text-slate-500">You are not enrolled in any classes yet.</p>
        </div>
      ) : (
        <div className="space-y-5">
          {classes.map(cls => {
            const caseCount = Array.isArray(cls.cases) ? cls.cases.length : 0
            return (
              <Card
                key={cls.classId || cls.id}
                className="overflow-hidden border-[#eadfd4] bg-white shadow-sm"
              >
                <div className="border-b border-[#eee4da] bg-[#fffaf5] px-5 py-4 md:px-6">
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-wider text-[#9b7658]">Enrolled class</p>
                      <h3 className="mt-1 text-xl font-semibold text-[#2C2218]">{cls.name}</h3>
                    </div>
                    <span className="shrink-0 rounded-full bg-[#f1e5d8] px-3 py-1 text-xs font-medium text-[#6c503b]">
                      {caseCount} {caseCount === 1 ? 'case' : 'cases'}
                    </span>
                  </div>
                    {cls.description && (
                      <p className="mt-2 text-sm text-[#5C4C3C]">{cls.description}</p>
                    )}
                </div>
                <div className="px-5 py-4 md:px-6">
                  <div className="mb-3 flex items-center gap-2 text-sm text-[#5C4C3C]">
                    <Users size={16} className="text-[#8B7462]" />
                    <span>Assigned reading</span>
                  </div>
                  <div className="space-y-2">
                    {caseCount === 0 ? (
                      <p className="text-sm text-[#5C4C3C]">No cases assigned yet.</p>
                    ) : (
                      (cls.cases || []).map(c => (
                        <div key={c.uploadId} className="flex items-center justify-between gap-4 rounded-lg border border-[#eee4da] bg-white px-4 py-3">
                          <div className="flex min-w-0 items-center gap-3">
                            <BookOpen size={18} className="shrink-0 text-[#9b7658]" />
                            <p className="truncate text-sm font-medium text-[#2C2218]">{c.fileName || 'Untitled case'}</p>
                          </div>
                          <Button
                            size="sm"
                            variant="outline"
                            className="h-auto py-1.5 px-3 text-xs"
                            onClick={() => navigate(`/workspace/${encodeURIComponent(c.uploadId)}`, { state: { from: '/classes' } })}
                          >
                            Open
                          </Button>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </Card>
            )
          })}
        </div>
      )}
    </div>
  )
}
