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
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {classes.map(cls => {
            const caseCount = Array.isArray(cls.cases) ? cls.cases.length : 0
            return (
              <Card
                key={cls.classId || cls.id}
                className="p-6 transition duration-200 ease-out hover:-translate-y-1 hover:shadow-xl cursor-pointer"
              >
                <div className="space-y-3">
                  <div>
                    <h3 className="text-lg font-semibold text-[#2C2218]">{cls.name}</h3>
                    {cls.description && (
                      <p className="text-sm text-[#5C4C3C] mt-1">{cls.description}</p>
                    )}
                  </div>
                  <div className="flex items-center gap-4 text-sm text-[#5C4C3C]">
                    <div className="flex items-center gap-1">
                      <Users size={16} className="text-[#8B7462]" />
                      <span>Enrolled</span>
                    </div>
                    <div className="flex items-center gap-1">
                      <BookOpen size={16} className="text-[#8B7462]" />
                      <span>{caseCount} {caseCount === 1 ? 'case' : 'cases'}</span>
                    </div>
                  </div>

                  <div className="border-t pt-3 space-y-2">
                    {caseCount === 0 ? (
                      <p className="text-sm text-[#5C4C3C]">No cases assigned yet.</p>
                    ) : (
                      (cls.cases || []).map(c => (
                        <div key={c.uploadId} className="flex items-start justify-between">
                          <div>
                            <p className="text-sm font-medium text-[#2C2218]">{c.fileName || c.uploadId}</p>
                            <p className="text-xs text-[#8B7462]">{c.uploadId}</p>
                          </div>
                          <Button
                            size="sm"
                            variant="outline"
                            className="h-auto py-1.5 px-3 text-xs"
                            onClick={() => navigate(`/workspace/${encodeURIComponent(c.uploadId)}`)}
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
