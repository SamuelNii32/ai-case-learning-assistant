import React, { useEffect, useState, useContext } from 'react'
import { useParams, useNavigate, useSearchParams } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import {
  getClassDetails,
  addStudentToClass,
  assignCaseToClass,
  getMyUploads,
  deleteStudentFromClass,
  unassignCaseFromClass,
  getClassStudents,
  getClassCases,
  getClassTutorProgress,
  getJoinCode,
  regenerateJoinCode,
} from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Card } from '@/components/ui/card'
import { ArrowLeft, Users, BookOpen, Copy, RotateCw } from 'lucide-react'
import toast from 'react-hot-toast'

export default function ClassDetail() {
  const { classId } = useParams()
  const navigate = useNavigate()
  const auth = useContext(AuthContext)
  const [searchParams] = useSearchParams()

  const [loading, setLoading] = useState(true)
  const [details, setDetails] = useState(null)
  const [studentEmail, setStudentEmail] = useState('')
  const [uploadId, setUploadId] = useState('')
  const [addingStudent, setAddingStudent] = useState(false)
  const [assigningCase, setAssigningCase] = useState(false)
  const [myUploads, setMyUploads] = useState([])
  const [loadingUploads, setLoadingUploads] = useState(false)
  const [removingStudentId, setRemovingStudentId] = useState(null)
  const [unassigningUploadId, setUnassigningUploadId] = useState(null)
  // Local snapshots for lists so counts always reflect server
  const [students, setStudents] = useState([])
  const [cases, setCases] = useState([])
  // Reading Coach / Tutor Progress
  const [tutorProgress, setTutorProgress] = useState([])
  const [loadingTutorProgress, setLoadingTutorProgress] = useState(false)
  const [tutorProgressError, setTutorProgressError] = useState(null)
  // Join Code
  const [joinCode, setJoinCode] = useState('')
  const [loadingJoinCode, setLoadingJoinCode] = useState(false)
  const [regeneratingJoinCode, setRegeneratingJoinCode] = useState(false)

  async function loadMyUploads() {
    try {
      setLoadingUploads(true)
      const uploads = await getMyUploads()
      setMyUploads(uploads || [])
    } catch (err) {
      console.error('Failed to load uploads', err)
    } finally {
      setLoadingUploads(false)
    }
  }

  async function loadTutorProgress() {
    try {
      setLoadingTutorProgress(true)
      setTutorProgressError(null)
      const data = await getClassTutorProgress(classId)
      setTutorProgress(Array.isArray(data) ? data : [])
    } catch (err) {
      console.error('Failed to load tutor progress', err)
      setTutorProgressError(err?.message || 'Failed to load Reading Coach progress')
      setTutorProgress([])
    } finally {
      setLoadingTutorProgress(false)
    }
  }

  async function loadJoinCode() {
    try {
      setLoadingJoinCode(true)
      const response = await getJoinCode(classId)
      // Backend may return { joinCode: "..." } or just "..."
      const code = response?.joinCode || response?.code || response
      setJoinCode(code || '')
    } catch (err) {
      console.error('Failed to load join code', err)
      setJoinCode('')
    } finally {
      setLoadingJoinCode(false)
    }
  }

  async function handleRegenerateJoinCode() {
    if (!window.confirm('Regenerate the class join code? Students will need the new code to join.')) {
      return
    }
    try {
      setRegeneratingJoinCode(true)
      const response = await regenerateJoinCode(classId)
      const newCode = response?.joinCode || response?.code || response
      setJoinCode(newCode || '')
      toast.success('Join code regenerated')
    } catch (err) {
      toast.error(err?.message || 'Failed to regenerate join code')
    } finally {
      setRegeneratingJoinCode(false)
    }
  }

  function handleCopyJoinCode() {
    if (!joinCode) {
      toast.error('No join code available')
      return
    }
    navigator.clipboard.writeText(joinCode)
    toast.success('Join code copied to clipboard')
  }

  async function loadDetails() {
    try {
      setLoading(true)
      const data = await getClassDetails(classId)
      setDetails(data)
      // Also fetch canonical lists to eliminate any backend shape drift
      const [s, c] = await Promise.all([
        getClassStudents(classId).catch(() => []),
        getClassCases(classId).catch(() => []),
      ])
      setStudents(Array.isArray(s) ? s : [])
      setCases(Array.isArray(c) ? c : [])
    } catch (err) {
      console.error('Failed to load class details', err)
      toast.error('Failed to load class details')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadDetails()
    loadMyUploads()
    loadTutorProgress()
    loadJoinCode()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [classId])

  function renderText(value, fallback = '') {
    if (value == null) return fallback
    if (typeof value === 'string') return value
    if (typeof value === 'number') return String(value)
    // handle common object shapes
    if (typeof value === 'object') {
      if (Array.isArray(value)) return value.map(item => renderText(item)).join(', ')
      return value.title ?? value.name ?? value.label ?? value.text ?? JSON.stringify(value)
    }
    return String(value)
  }

  function getProgressKey(item, index) {
    const studentId = renderText(item?.studentId || item?.userId || item?.student?.id || '')
    const uploadId = renderText(item?.uploadId || item?.caseId || item?.case?.id || '')
    const status = renderText(item?.status || item?.state || '')
    return `${studentId || 'student'}:${uploadId || 'case'}:${status || 'status'}:${index}`
  }

  // Prefill upload selection from query param when present
  useEffect(() => {
    try {
      const u = searchParams.get('uploadId')
      if (u) setUploadId(u)
    } catch {
      /* ignore */
    }
  }, [searchParams])

  async function handleAddStudent(e) {
    e.preventDefault()
    const email = studentEmail.trim()
    if (!email) {
      toast.error('Student email is required')
      return
    }

    setAddingStudent(true)
    try {
      const res = await addStudentToClass(classId, email)
      if (res?.alreadyInClass) {
        toast.success('Student is already in this class')
      } else {
        toast.success('Student added')
      }
      setStudentEmail('')
      await loadDetails()
    } catch (err) {
      toast.error(err?.message || 'Failed to add student')
    } finally {
      setAddingStudent(false)
    }
  }

  async function handleAssignCase(e) {
    e.preventDefault()
    const trimmed = uploadId.trim()
    if (!trimmed) {
      toast.error('Upload ID is required')
      return
    }

    setAssigningCase(true)
    try {
      const res = await assignCaseToClass(classId, trimmed)
      if (res?.alreadyAssigned) {
        toast.success('Case already assigned')
      } else {
        toast.success('Case assigned')
      }
      setUploadId('')
      await loadDetails()
    } catch (err) {
      toast.error(err?.message || 'Failed to assign case')
    } finally {
      setAssigningCase(false)
    }
  }

  async function handleRemoveStudent(studentId) {
    if (!studentId) return
    if (!window.confirm('Remove this student from the class?')) return
    setRemovingStudentId(studentId)
    try {
      await deleteStudentFromClass(classId, studentId)
      toast.success('Student removed')
      await loadDetails()
    } catch (err) {
      toast.error(err?.message || 'Failed to remove student')
    } finally {
      setRemovingStudentId(null)
    }
  }

  async function handleUnassignCase(uploadIdToUnassign) {
    if (!uploadIdToUnassign) return
    if (!window.confirm('Unassign this case from the class?')) return
    setUnassigningUploadId(uploadIdToUnassign)
    try {
      await unassignCaseFromClass(classId, uploadIdToUnassign)
      toast.success('Case unassigned')
      await loadDetails()
    } catch (err) {
      toast.error(err?.message || 'Failed to unassign case')
    } finally {
      setUnassigningUploadId(null)
    }
  }

  // History view has been moved to a dedicated page accessible from the sidebar

  if (!auth?.loggedIn || auth?.user?.role !== 'instructor') {
    return (
      <div className="p-6 md:p-8">
        <h2 className="text-xl md:text-2xl font-bold">Access denied</h2>
        <p className="mt-2 text-sm text-slate-600">Instructor access required.</p>
      </div>
    )
  }

  return (
    <div className="p-4 md:p-6 max-w-6xl mx-auto space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => navigate('/admin/classes')}
            className="inline-flex items-center gap-2"
          >
            <ArrowLeft className="h-4 w-4" />
            Back
          </Button>
          <div>
            <h1 className="text-2xl md:text-3xl font-bold">
              {renderText(details?.name, 'Class')}
            </h1>
            {details?.description && (
              <p className="text-sm text-slate-600 mt-1">{renderText(details.description)}</p>
            )}
          </div>
        </div>
        <div className="flex items-center gap-4 text-sm text-slate-600">
          <div className="flex items-center gap-2">
            <Users className="h-4 w-4" />
            <span>{(students?.length ?? details?.students?.length ?? 0)} students</span>
          </div>
          <div className="flex items-center gap-2">
            <BookOpen className="h-4 w-4" />
            <span>{(cases?.length ?? details?.cases?.length ?? 0)} cases</span>
          </div>
        </div>
      </div>

      {loading ? (
        <Card className="p-6">Loading class...</Card>
      ) : !details ? (
        <Card className="p-6">Class not found.</Card>
      ) : (
        <div className="space-y-6">
          {/* Class Join Code Section */}
          <Card className="p-6 space-y-4 border-2 border-[#C96A08]/20 bg-gradient-to-br from-[#fdf4eb] to-[#f9f1e8]">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-[#2c2218]">Class Join Code</h2>
                <p className="text-sm text-[#7a5c3c] mt-1">Share this code with students to let them join the class</p>
              </div>
            </div>

            {loadingJoinCode ? (
              <div className="p-4 bg-white border border-[#e4d6c7] rounded-md text-center">
                <p className="text-sm text-[#7a5c3c]">Loading code…</p>
              </div>
            ) : joinCode ? (
              <div className="space-y-3">
                <div className="flex items-center gap-3 p-4 bg-white border-2 border-[#C96A08] rounded-md">
                  <div className="flex-1">
                    <p className="text-xs text-[#7a5c3c] font-medium uppercase tracking-widest">Join Code</p>
                    <p className="text-3xl font-bold text-[#2c2218] font-mono tracking-wider">{joinCode}</p>
                  </div>
                  <Button
                    variant="warm"
                    size="sm"
                    onClick={handleCopyJoinCode}
                    className="whitespace-nowrap flex items-center gap-2"
                  >
                    <Copy className="h-4 w-4" />
                    Copy
                  </Button>
                </div>
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handleRegenerateJoinCode}
                    disabled={regeneratingJoinCode}
                    className="flex items-center gap-2"
                  >
                    <RotateCw className={`h-4 w-4 ${regeneratingJoinCode ? 'animate-spin' : ''}`} />
                    {regeneratingJoinCode ? 'Regenerating…' : 'Regenerate Code'}
                  </Button>
                </div>
              </div>
            ) : (
              <div className="p-4 bg-white border border-[#e4d6c7] rounded-md text-center">
                <p className="text-sm text-[#7a5c3c]">No join code available</p>
              </div>
            )}
          </Card>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card className="p-6 space-y-4">
            <div>
              <h2 className="text-lg font-semibold">Students</h2>
              <p className="text-sm text-slate-600">Enroll students by email.</p>
            </div>
            <form onSubmit={handleAddStudent} className="space-y-3">
              <Input
                type="email"
                placeholder="student@example.com"
                value={studentEmail}
                onChange={e => setStudentEmail(e.target.value)}
              />
              <Button type="submit" disabled={addingStudent} className="w-full sm:w-auto" variant="warm">
                {addingStudent ? 'Adding...' : 'Add student'}
              </Button>
            </form>
            <div className="border-t pt-4 space-y-3">
              {(students?.length ? students : details.students || []).length ? (
                (students?.length ? students : details.students).map(stu => (
                  <div key={stu.id} className="flex items-center justify-between">
                    <div>
                      <p className="font-medium text-sm">{stu.fullName || stu.email}</p>
                      <p className="text-xs text-slate-600">{stu.email}</p>
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleRemoveStudent(stu.id)}
                      disabled={removingStudentId === stu.id}
                    >
                      {removingStudentId === stu.id ? 'Removing…' : 'Remove'}
                    </Button>
                  </div>
                ))
              ) : (
                <p className="text-sm text-slate-600">No students yet.</p>
              )}
            </div>
          </Card>

          <Card className="p-6 space-y-4">
            <div>
              <h2 className="text-lg font-semibold">Cases</h2>
              <p className="text-sm text-slate-600">Assign a case from your uploads.</p>
            </div>
            <form onSubmit={handleAssignCase} className="space-y-3">
              <div className="space-y-2">
                <label htmlFor="case-select" className="text-sm font-medium">
                  Select case
                </label>
                <select
                  id="case-select"
                  value={uploadId}
                  onChange={e => setUploadId(e.target.value)}
                  className="w-full px-3 py-2 border border-[#e4d6c7] rounded-md text-sm focus:outline-none focus:border-[#C96A08] focus:ring-2 focus:ring-[#C96A08]/30"
                  disabled={loadingUploads}
                >
                  <option value="">
                    {loadingUploads ? 'Loading...' : 'Choose a case'}
                  </option>
                  {myUploads.map(upload => (
                    <option key={upload.uploadId} value={upload.uploadId}>
                      {upload.originalFileName || upload.name || upload.uploadId}
                    </option>
                  ))}
                </select>
              </div>
              <Button type="submit" disabled={assigningCase || !uploadId} className="w-full sm:w-auto" variant="warm">
                {assigningCase ? 'Assigning...' : 'Assign case'}
              </Button>
            </form>
            <div className="border-t pt-4 space-y-3">
              {(cases?.length ? cases : details.cases || []).length ? (
                (cases?.length ? cases : details.cases).map(c => (
                  <div key={c.uploadId} className="flex items-center justify-between">
                    <div>
                      <p className="font-medium text-sm">{c.fileName || c.uploadId}</p>
                      <p className="text-xs text-slate-600">{c.uploadId}</p>
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleUnassignCase(c.uploadId)}
                      disabled={unassigningUploadId === c.uploadId}
                    >
                      {unassigningUploadId === c.uploadId ? 'Unassigning…' : 'Unassign'}
                    </Button>
                  </div>
                ))
              ) : (
                <p className="text-sm text-slate-600">No cases assigned yet.</p>
              )}
            </div>
          </Card>

          </div>

          {/* Reading Coach Progress Section */}
          <Card className="p-6 space-y-4">
            <div>
              <h2 className="text-lg font-semibold">Reading Coach Progress</h2>
              <p className="text-sm text-slate-600">Learner progress on assigned cases.</p>
            </div>

            {loadingTutorProgress ? (
              <div className="p-4 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md text-center">
                <p className="text-sm text-[#7a5c3e]">Loading progress…</p>
              </div>
            ) : tutorProgressError ? (
              <div className="p-4 bg-[#fde5e5] border border-[#f2c6c6] rounded-md">
                <p className="text-sm text-[#8c1c1c] font-medium">Error</p>
                <p className="text-xs text-[#8c1c1c] mt-1">{tutorProgressError}</p>
              </div>
            ) : tutorProgress.length === 0 ? (
              <div className="p-4 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md text-center">
                <p className="text-sm text-[#7a5c3e]">No Reading Coach activity yet.</p>
              </div>
            ) : (
              <div className="space-y-3">
                {tutorProgress.map((item, idx) => {
                  // Normalize field names defensively
                  const studentId = item.studentId || item.userId
                  const studentName = renderText(item.studentName || item.fullName || item.email || item.student?.name || 'Unknown')
                  const studentEmail = renderText(item.email || item.student?.email || '')
                  const caseName = renderText(item.caseName || item.fileName || item.originalFileName || item.uploadId || item.case?.name || 'Unknown Case')
                  const status = renderText(item.status || item.state || item.progressStatus || 'Unknown')
                  const currentNode = renderText(item.currentNode || item.currentStep || item.latestStep || item.node || '—')
                  const completedSteps = renderText(item.completedStepCount ?? item.stepsCompleted ?? item.completedSteps ?? item.answerCount ?? 0)
                  const lastActivity = item.updatedAt || item.lastActivityAt || item.createdAt
                  const uploadId = renderText(item.uploadId || item.caseId || item.case?.id || '')

                  return (
                    <div
                      key={getProgressKey(item, idx)}
                      className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 p-4 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md hover:shadow-md transition-shadow"
                    >
                      <div className="flex-1 min-w-0">
                        <div className="text-sm font-medium text-[#2c2218]">{studentName}</div>
                        {studentEmail && (
                          <div className="text-xs text-[#7a5c3c]">{studentEmail}</div>
                        )}
                        <div className="text-xs text-[#7a5c3c] mt-1">
                          <span className="font-medium">Case:</span> {caseName}
                        </div>
                        <div className="text-xs text-[#7a5c3c] mt-1 space-x-3">
                          <span>
                            <span className="font-medium">Status:</span> {status}
                          </span>
                          <span>
                            <span className="font-medium">Steps:</span> {completedSteps}
                          </span>
                          {currentNode !== '—' && (
                            <span>
                              <span className="font-medium">Node:</span> {currentNode}
                            </span>
                          )}
                        </div>
                        {lastActivity && (
                          <div className="text-xs text-[#7a5c3c] mt-1">
                            <span className="font-medium">Last:</span>{' '}
                            {new Date(lastActivity).toLocaleString()}
                          </div>
                        )}
                      </div>

                      {studentId && uploadId && (
                        <Button
                          variant="warm"
                          size="sm"
                          onClick={() =>
                            navigate(
                              `/admin/classes/${encodeURIComponent(classId)}/tutor-progress/${encodeURIComponent(
                                studentId
                              )}/${encodeURIComponent(uploadId)}`
                            )
                          }
                          className="w-full sm:w-auto whitespace-nowrap"
                        >
                          View Details
                        </Button>
                      )}
                    </div>
                  )
                })}
              </div>
            )}
          </Card>
        </div>
      )}
    </div>
  )
}
