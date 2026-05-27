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
  const [joinCode, setJoinCode] = useState('')
  const [joiningClass, setJoiningClass] = useState(false)

  function renderText(value, fallback = '') {
    if (value == null) return fallback
    if (typeof value === 'string') return value
    if (typeof value === 'number') return String(value)
    if (typeof value === 'object') return value.title ?? value.name ?? JSON.stringify(value)
    return String(value)
  }

  async function loadClasses() {
    try {
      setLoading(true)
      const data = await getEnrolledClasses()
      setClasses(Array.isArray(data) ? data : [])
    } catch (err) {
      console.error('Failed to load enrolled classes', err)
      toast.error('Failed to load your classes')
    } finally {
      setLoading(false)
    }
  }

  async function handleJoinClass(e) {
    e.preventDefault()
    const code = joinCode.trim()
    if (!code) {
      toast.error('Please enter a class join code')
      return
    }
    setJoiningClass(true)
    try {
      await joinClass(code)
      toast.success('Successfully joined the class!')
      setJoinCode('')
      await loadClasses()
    } catch (err) {
      toast.error(err?.message || 'Failed to join class')
    } finally {
      setJoiningClass(false)
    }
  }

  useEffect(() => {
    loadClasses()
  }, [])

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

      {loading ? (
        <div className="bg-white border border-slate-200 rounded-lg p-6 md:p-8 text-center">
          <p className="text-slate-500">Loading your classes…</p>
        </div>
      ) : (
        <div className="space-y-6">
          {/* Join a Class Card */}
          <Card className="p-6 space-y-4 border-2 border-[#C96A08]/20 bg-gradient-to-br from-[#fdf4eb] to-[#f9f1e8]">
            <div>
              <h2 className="text-lg font-semibold text-[#2c2218]">Join a Class</h2>
              <p className="text-sm text-[#7a5c3c] mt-1">Enter a class code to join a new class</p>
            </div>
            <form onSubmit={handleJoinClass} className="flex gap-3">
              <Input
                type="text"
                placeholder="Enter class join code"
                value={joinCode}
                onChange={e => setJoinCode(e.target.value)}
                disabled={joiningClass}
                className="flex-1"
              />
              <Button
                type="submit"
                variant="warm"
                disabled={joiningClass || !joinCode.trim()}
              >
                {joiningClass ? 'Joining…' : 'Join'}
              </Button>
            </form>
          </Card>

          {/* Classes Grid */}
          {classes.length === 0 ? (
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
                    <h3 className="text-lg font-semibold text-[#2C2218]">{renderText(cls.name)}</h3>
                    {cls.description && (
                      <p className="text-sm text-[#5C4C3C] mt-1">{renderText(cls.description)}</p>
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
      )}
    </div>
  )
}
