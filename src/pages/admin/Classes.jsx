import React, { useState, useContext, useEffect } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import { getClasses, createClass } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Card } from '@/components/ui/card'
import toast from 'react-hot-toast'
import { Plus, X, Users, BookOpen } from 'lucide-react'

export default function Classes() {
  const navigate = useNavigate()
  const auth = useContext(AuthContext)
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [className, setClassName] = useState('')
  const [classDescription, setClassDescription] = useState('')
  const [creating, setCreating] = useState(false)
  const [classes, setClasses] = useState([])
  const [loading, setLoading] = useState(true)
  const location = useLocation()
  const uploadIdFromQuery = React.useMemo(() => {
    try {
      const sp = new URLSearchParams(location.search)
      return sp.get('uploadId') || ''
    } catch {
      return ''
    }
  }, [location.search])

  useEffect(() => {
    loadClasses()
  }, [])

  async function loadClasses() {
    try {
      setLoading(true)
      const data = await getClasses()
      setClasses(Array.isArray(data) ? data : [])
    } catch (err) {
      console.error('Failed to load classes:', err)
      toast.error('Failed to load classes')
    } finally {
      setLoading(false)
    }
  }

  async function handleCreateClass(e) {
    e.preventDefault()
    const name = className.trim()
    if (!name) {
      toast.error('Class name is required')
      return
    }

    setCreating(true)
    try {
      await createClass(name, classDescription.trim())
      toast.success('Class created successfully')
      setShowCreateModal(false)
      setClassName('')
      setClassDescription('')
      loadClasses()
    } catch (err) {
      toast.error(err?.message || 'Failed to create class')
    } finally {
      setCreating(false)
    }
  }

  if (!auth?.loggedIn || auth?.user?.role !== 'instructor') {
    return (
      <div className="p-6 md:p-8">
        <h2 className="text-xl md:text-2xl font-bold">Access denied</h2>
        <p className="mt-2 text-sm text-slate-600">Instructor access required.</p>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-[#faf6f0] py-10">
      <div className="max-w-7xl mx-auto px-4">
        <div className="bg-white border border-[#f4e7d8] shadow-[0_25px_45px_rgba(32,20,8,0.08)] rounded-[12px] p-6 md:p-8 space-y-6">
          <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
            <div>
              <h1 className="text-2xl md:text-3xl font-bold text-[#2c2218]">Classes</h1>
              <p className="text-sm text-[#5C4C3C] mt-1">Manage your classes, students, and assignments.</p>
            </div>
            <Button
              onClick={() => setShowCreateModal(true)}
              className="w-full sm:w-auto inline-flex items-center gap-2"
              variant="warm"
            >
              <Plus size={18} />
              Create Class
            </Button>
          </div>

          <div className="border-b border-[#E8DDD0] bg-white px-1">
            <div className="flex flex-wrap gap-6 text-sm font-semibold text-[#5C4C3C]">
              <button
                type="button"
                className="pb-3 text-[#C96A08] border-b-2 border-[#C96A08]"
              >
                My Classes
              </button>
              <button type="button" className="pb-3 text-[#7a5c3e]">
                Upload Cases
              </button>
            </div>
          </div>

          {loading ? (
            <div className="bg-white border border-[#f3e0ce] rounded-2xl p-6 md:p-8 text-center shadow-sm">
              <p className="text-[#7a5c3e]">Loading classes...</p>
            </div>
          ) : classes.length === 0 ? (
            <div className="bg-white border border-[#f3e0ce] rounded-2xl p-6 md:p-8 text-center shadow-sm">
              <p className="text-[#7a5c3e]">No classes yet. Create your first class to get started.</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
              {classes.map(cls => (
                <Card
                  key={cls.id}
                  className="p-5 md:p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-[0_10px_25px_rgba(32,20,8,0.08)] transition-shadow hover:shadow-[0_12px_30px_rgba(32,20,8,0.12)]"
                >
                  <div className="flex flex-col h-full gap-4 text-[#2c2218]">
                    <div>
                      <h3 className="text-lg font-semibold">{cls.name}</h3>
                      {cls.description && (
                        <p className="text-sm text-[#5C4C3C] mt-1">{cls.description}</p>
                      )}
                    </div>
                    <div className="flex flex-wrap items-center gap-6 text-sm text-[#5C4C3C]">
                      <div className="flex items-center gap-2">
                        <Users size={16} className="text-[#C96A08]" />
                        <span className="font-semibold text-[#2c2218]">{cls.studentCount || 0}</span>
                        <span>students</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <BookOpen size={16} className="text-[#C96A08]" />
                        <span className="font-semibold text-[#2c2218]">{cls.caseCount || 0}</span>
                        <span>cases</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-[#C96A08] text-lg leading-none">•</span>
                        <span className="font-semibold text-[#2c2218]">
                          {cls.activeSessions ?? cls.activeSessionCount ?? 0}
                        </span>
                        <span>active sessions</span>
                      </div>
                    </div>
                    <div className="mt-auto">
                      <Button
                        variant="warm"
                        className="w-full"
                        size="sm"
                        onClick={() => {
                          const q = uploadIdFromQuery
                            ? `?uploadId=${encodeURIComponent(uploadIdFromQuery)}`
                            : ''
                          navigate(`/admin/classes/${encodeURIComponent(cls.id)}${q}`)
                        }}
                      >
                        Manage Class
                      </Button>
                    </div>
                  </div>
                </Card>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Create Class Modal */}
      {showCreateModal && (
        <div
          className="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50"
          onClick={e => {
            if (e.target === e.currentTarget) setShowCreateModal(false)
          }}
        >
          <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
            <div className="flex items-center justify-between p-4 md:p-6 border-b border-[#f3e0ce]">
              <h2 className="text-lg md:text-xl font-semibold text-[#2c2218]">Create New Class</h2>
              <button
                onClick={() => setShowCreateModal(false)}
                className="text-[#7a5c3e] hover:text-[#2c2218] transition-colors"
              >
                <X size={20} />
              </button>
            </div>

            <form onSubmit={handleCreateClass} className="p-4 md:p-6 space-y-4">
              <div>
                <label htmlFor="className" className="block text-sm font-medium mb-1.5 text-[#2c2218]">
                  Class Name <span className="text-red-500">*</span>
                </label>
                <Input
                  id="className"
                  type="text"
                  placeholder="e.g., Business Strategy 101"
                  value={className}
                  onChange={e => setClassName(e.target.value)}
                  required
                  autoFocus
                  className="w-full"
                />
              </div>

              <div>
                <label htmlFor="classDescription" className="block text-sm font-medium mb-1.5 text-[#2c2218]">
                  Description (optional)
                </label>
                <Textarea
                  id="classDescription"
                  placeholder="Brief description of the class..."
                  value={classDescription}
                  onChange={e => setClassDescription(e.target.value)}
                  rows={3}
                  className="w-full resize-none"
                />
              </div>

              <div className="flex flex-col-reverse sm:flex-row gap-3 pt-2">
                <Button
                  type="button"
                  onClick={() => setShowCreateModal(false)}
                  disabled={creating}
                  variant="outline"
                  className="w-full sm:w-auto"
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={creating} className="w-full sm:w-auto" variant="warm">
                  {creating ? 'Creating...' : 'Create Class'}
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
