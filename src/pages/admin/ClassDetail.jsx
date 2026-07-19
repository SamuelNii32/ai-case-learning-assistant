import React, { useEffect, useState, useContext, useMemo } from 'react'
import { useParams, useNavigate, useSearchParams } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import {
  getClassDetails,
  getPagedItems,
  addStudentToClass,
  assignCaseToClass,
  getMyUploads,
  deleteStudentFromClass,
  unassignCaseFromClass,
  getClassStudents,
  getClassCases,
  getClassTutorProgress,
  deleteClass,
  getClassReadingCoachSummary,
  getJoinCode,
  regenerateJoinCode,
} from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Card } from '@/components/ui/card'
import {
  ArrowLeft,
  Users,
  BookOpen,
  AlertTriangle,
  CheckCircle2,
  Clock3,
  Trash2,
  Copy,
  RotateCw,
  BarChart3,
} from 'lucide-react'
import toast from 'react-hot-toast'

const STATUS_OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'needs_help', label: 'Needs attention' },
  { value: 'in_progress', label: 'In progress' },
  { value: 'completed', label: 'Completed' },
  { value: 'not_started', label: 'Not started' },
]

function normalizeStatus(value) {
  const raw = String(value || 'not_started').toLowerCase()
  if (raw === 'needs_help') return 'Needs attention'
  if (raw === 'in_progress') return 'In progress'
  if (raw === 'not_started') return 'Not started'
  if (raw === 'completed') return 'Completed'
  return raw
    .split(/[_\s-]+/)
    .filter(Boolean)
    .map(part => part[0]?.toUpperCase() + part.slice(1))
    .join(' ')
}

function statusClass(value) {
  const raw = String(value || 'not_started').toLowerCase()
  if (raw === 'needs_help') return 'bg-red-50 text-red-700 border-red-200'
  if (raw === 'completed') return 'bg-green-50 text-green-700 border-green-200'
  if (raw === 'in_progress') return 'bg-blue-50 text-blue-700 border-blue-200'
  return 'bg-slate-50 text-slate-600 border-slate-200'
}

function formatDate(value) {
  if (!value) return 'No activity yet'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString()
}

function progressSortRank(row) {
  const status = String(row.status || '').toLowerCase()
  if (status === 'needs_help') return 0
  if (status === 'in_progress') return 1
  if (status === 'not_started') return 2
  if (status === 'completed') return 3
  return 4
}

export default function ClassDetail() {
  const { classId } = useParams()
  const navigate = useNavigate()
  const auth = useContext(AuthContext)
  const [searchParams] = useSearchParams()

  const [loading, setLoading] = useState(true)
  const [details, setDetails] = useState(null)
  const [studentEmail, setStudentEmail] = useState('')
  const [uploadId, setUploadId] = useState('')
  const [readingCoachQuestions, setReadingCoachQuestions] = useState('')
  const [addingStudent, setAddingStudent] = useState(false)
  const [assigningCase, setAssigningCase] = useState(false)
  const [myUploads, setMyUploads] = useState([])
  const [loadingUploads, setLoadingUploads] = useState(false)
  const [removingStudentId, setRemovingStudentId] = useState(null)
  const [unassigningUploadId, setUnassigningUploadId] = useState(null)
  const [deletingClass, setDeletingClass] = useState(false)
  // Local snapshots for lists so counts always reflect server
  const [students, setStudents] = useState([])
  const [cases, setCases] = useState([])
  const [tutorProgress, setTutorProgress] = useState([])
  const [progressLoading, setProgressLoading] = useState(false)
  const [progressError, setProgressError] = useState('')
  const [progressFilter, setProgressFilter] = useState('all')
  const [progressSearch, setProgressSearch] = useState('')
  const [readingCoachSummary, setReadingCoachSummary] = useState(null)
  const [loadingReadingCoachSummary, setLoadingReadingCoachSummary] = useState(false)
  const [readingCoachSummaryError, setReadingCoachSummaryError] = useState(null)
  const [joinCode, setJoinCode] = useState('')
  const [loadingJoinCode, setLoadingJoinCode] = useState(false)
  const [regeneratingJoinCode, setRegeneratingJoinCode] = useState(false)

  async function loadMyUploads() {
    try {
      setLoadingUploads(true)
      const uploads = await getMyUploads()
      setMyUploads(getPagedItems(uploads))
    } catch (err) {
      console.error('Failed to load uploads', err)
    } finally {
      setLoadingUploads(false)
    }
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
      setStudents(getPagedItems(s))
      const nextCases = getPagedItems(c)
      setCases(nextCases)
      const selectedCase = nextCases.find(item => String(item.uploadId) === String(uploadId))
      if (selectedCase?.readingCoachQuestions != null) {
        setReadingCoachQuestions(String(selectedCase.readingCoachQuestions || ''))
      }
    } catch (err) {
      console.error('Failed to load class details', err)
      toast.error('Failed to load class details')
    } finally {
      setLoading(false)
    }
  }

  async function loadTutorProgress() {
    try {
      setProgressLoading(true)
      setProgressError('')
      const rows = await getClassTutorProgress(classId)
      setTutorProgress(getPagedItems(rows))
    } catch (err) {
      console.error('Failed to load Reading Coach progress', err)
      setProgressError(err?.message || 'Failed to load Reading Coach progress')
      setTutorProgress([])
    } finally {
      setProgressLoading(false)
    }
  }

  async function loadReadingCoachSummary() {
    try {
      setLoadingReadingCoachSummary(true)
      setReadingCoachSummaryError(null)
      const data = await getClassReadingCoachSummary(classId)
      setReadingCoachSummary(data || null)
    } catch (err) {
      console.error('Failed to load Reading Coach summary', err)
      setReadingCoachSummaryError(err?.message || 'Failed to load Reading Coach summary')
      setReadingCoachSummary(null)
    } finally {
      setLoadingReadingCoachSummary(false)
    }
  }

  async function loadJoinCode() {
    try {
      setLoadingJoinCode(true)
      const response = await getJoinCode(classId)
      const code = response?.joinCode || response?.code || response
      setJoinCode(code || '')
    } catch (err) {
      console.error('Failed to load join code', err)
      setJoinCode('')
    } finally {
      setLoadingJoinCode(false)
    }
  }

  useEffect(() => {
    loadDetails()
    loadMyUploads()
    loadTutorProgress()
    loadReadingCoachSummary()
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

  function metricValue(source, keys) {
    for (const key of keys) {
      const value = source?.[key]
      if (value !== undefined && value !== null) return value
    }
    return 0
  }

  function formatMetric(value) {
    const numeric = Number(value)
    if (Number.isFinite(numeric)) return numeric.toLocaleString()
    return renderText(value, '0')
  }

  function getCaseQuestions(caseUploadId) {
    const match = (cases?.length ? cases : details?.cases || []).find(
      item => String(item.uploadId) === String(caseUploadId)
    )
    return match?.readingCoachQuestions ?? match?.customReadingCoachQuestions ?? ''
  }

  function getNormalizedStudents() {
    if (students.length) return students
    return getPagedItems(details?.students)
  }

  function getNormalizedCases() {
    if (cases.length) return cases
    return getPagedItems(details?.cases)
  }

  function handleUploadSelection(nextUploadId) {
    setUploadId(nextUploadId)
    const existingQuestions = getCaseQuestions(nextUploadId)
    setReadingCoachQuestions(existingQuestions ? String(existingQuestions) : '')
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
      await loadTutorProgress()
      await loadReadingCoachSummary()
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
      const res = await assignCaseToClass(classId, {
        uploadId: trimmed,
        readingCoachQuestions,
      })
      if (res?.alreadyAssigned) {
        toast.success('Case already assigned')
      } else {
        toast.success('Case assigned')
      }
      await loadDetails()
      await loadTutorProgress()
      await loadReadingCoachSummary()
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
      await loadTutorProgress()
      await loadReadingCoachSummary()
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
      await loadTutorProgress()
      await loadReadingCoachSummary()
    } catch (err) {
      toast.error(err?.message || 'Failed to unassign case')
    } finally {
      setUnassigningUploadId(null)
    }
  }

  async function handleDeleteClass() {
    if (!details?.name || !classId) return
    const confirmed = window.confirm(
      `Delete "${details.name}"? This removes the class, enrollments, and assignments. Student accounts, uploaded cases, and existing sessions will not be deleted.`
    )
    if (!confirmed) return

    setDeletingClass(true)
    try {
      await deleteClass(classId)
      toast.success('Class deleted')
      navigate('/admin/classes')
    } catch (err) {
      toast.error(err?.message || 'Failed to delete class')
      setDeletingClass(false)
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

  async function handleCopyJoinCode() {
    if (!joinCode) {
      toast.error('No join code available')
      return
    }

    try {
      await navigator.clipboard.writeText(joinCode)
      toast.success('Join code copied')
    } catch {
      toast.error('Could not copy join code')
    }
  }

  // History view has been moved to a dedicated page accessible from the sidebar

  const progressSummary = useMemo(() => {
    const total = tutorProgress.length
    const started = tutorProgress.filter(row => Number(row.answerAttempts || 0) > 0 || row.latestTutorSessionId).length
    const needsAttention = tutorProgress.filter(row => row.needsAttention || row.status === 'needs_help').length
    const completed = tutorProgress.filter(row => row.status === 'completed').length
    const notStarted = tutorProgress.filter(row => row.status === 'not_started' || (!row.latestTutorSessionId && Number(row.answerAttempts || 0) === 0)).length
    const onTrack = Math.max(0, started - needsAttention)
    return { total, started, onTrack, needsAttention, completed, notStarted }
  }, [tutorProgress])

  const filteredProgress = useMemo(() => {
    const query = progressSearch.trim().toLowerCase()
    return [...tutorProgress]
      .filter(row => {
        const status = String(row.status || 'not_started').toLowerCase()
        const matchesStatus = progressFilter === 'all' || status === progressFilter
        if (!matchesStatus) return false
        if (!query) return true
        const haystack = [
          row.studentName,
          row.studentEmail,
          row.fileName,
          row.currentStep?.title,
          row.lastHelpQuestion,
        ]
          .filter(Boolean)
          .join(' ')
          .toLowerCase()
        return haystack.includes(query)
      })
      .sort((a, b) => {
        const rank = progressSortRank(a) - progressSortRank(b)
        if (rank !== 0) return rank
        const aTime = a.lastActivity ? new Date(a.lastActivity).getTime() : 0
        const bTime = b.lastActivity ? new Date(b.lastActivity).getTime() : 0
        return bTime - aTime
      })
  }, [progressFilter, progressSearch, tutorProgress])

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
              {details?.name || 'Class'}
            </h1>
            {details?.description && (
              <p className="text-sm text-slate-600 mt-1">{details.description}</p>
            )}
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-600">
          <div className="flex items-center gap-4">
            <div className="flex items-center gap-2">
              <Users className="h-4 w-4" />
              <span>{(students?.length ?? details?.students?.length ?? 0)} students</span>
            </div>
            <div className="flex items-center gap-2">
              <BookOpen className="h-4 w-4" />
              <span>{(cases?.length ?? details?.cases?.length ?? 0)} cases</span>
            </div>
          </div>
          {details ? (
            <Button
              variant="outline"
              size="sm"
              onClick={handleDeleteClass}
              disabled={deletingClass}
              className="inline-flex items-center gap-2 border-red-200 text-red-700 hover:bg-red-50"
            >
              <Trash2 className="h-4 w-4" />
              {deletingClass ? 'Deleting...' : 'Delete class'}
            </Button>
          ) : null}
        </div>
      </div>

      {loading ? (
        <Card className="p-6">Loading class...</Card>
      ) : !details ? (
        <Card className="p-6">Class not found.</Card>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card className="p-6 space-y-4 border-2 border-[#C96A08]/20 bg-gradient-to-br from-[#fdf4eb] to-[#f9f1e8] lg:col-span-2">
            <div>
              <h2 className="text-lg font-semibold text-[#2c2218]">Class join code</h2>
              <p className="text-sm text-[#7a5c3c] mt-1">
                Share this code with students so they can join the class.
              </p>
            </div>

            {loadingJoinCode ? (
              <div className="rounded-md border border-[#e4d6c7] bg-white p-4 text-center text-sm text-[#7a5c3c]">
                Loading code...
              </div>
            ) : joinCode ? (
              <div className="space-y-3">
                <div className="flex flex-col gap-3 rounded-md border-2 border-[#C96A08] bg-white p-4 sm:flex-row sm:items-center">
                  <div className="flex-1">
                    <p className="text-xs font-medium uppercase tracking-widest text-[#7a5c3c]">
                      Join code
                    </p>
                    <p className="font-mono text-3xl font-bold tracking-wider text-[#2c2218]">
                      {joinCode}
                    </p>
                  </div>
                  <Button
                    variant="warm"
                    size="sm"
                    onClick={handleCopyJoinCode}
                    className="inline-flex items-center gap-2 whitespace-nowrap"
                  >
                    <Copy className="h-4 w-4" />
                    Copy
                  </Button>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleRegenerateJoinCode}
                  disabled={regeneratingJoinCode}
                  className="inline-flex items-center gap-2"
                >
                  <RotateCw className={`h-4 w-4 ${regeneratingJoinCode ? 'animate-spin' : ''}`} />
                  {regeneratingJoinCode ? 'Regenerating...' : 'Regenerate code'}
                </Button>
              </div>
            ) : (
              <div className="rounded-md border border-[#e4d6c7] bg-white p-4 text-center text-sm text-[#7a5c3c]">
                No join code available.
              </div>
            )}
          </Card>

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
              {getNormalizedStudents().length ? (
                getNormalizedStudents().map(stu => (
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
                  onChange={e => handleUploadSelection(e.target.value)}
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
              <div className="space-y-2">
                <label htmlFor="reading-coach-questions" className="text-sm font-medium">
                  Custom Reading Coach questions
                </label>
                <Textarea
                  id="reading-coach-questions"
                  value={readingCoachQuestions}
                  onChange={e => setReadingCoachQuestions(e.target.value)}
                  rows={5}
                  placeholder="Optional questions or prompts students should answer while working through this case."
                  className="min-h-[120px] border-[#e4d6c7] focus:outline-none focus:border-[#C96A08] focus:ring-2 focus:ring-[#C96A08]/30"
                />
              </div>
              <Button type="submit" disabled={assigningCase || !uploadId} className="w-full sm:w-auto" variant="warm">
                {assigningCase ? 'Assigning...' : 'Assign case'}
              </Button>
            </form>
            <div className="border-t pt-4 space-y-3">
              {getNormalizedCases().length ? (
                getNormalizedCases().map(c => (
                  <div key={c.uploadId} className="flex items-center justify-between">
                    <div>
                      <p className="font-medium text-sm">{c.fileName || c.uploadId}</p>
                      <p className="text-xs text-slate-600">{c.uploadId}</p>
                      {(c.readingCoachQuestions || c.customReadingCoachQuestions) && (
                        <p className="text-xs text-[#7a5c3c] mt-1 line-clamp-2">
                          {c.readingCoachQuestions || c.customReadingCoachQuestions}
                        </p>
                      )}
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

          <Card className="p-6 space-y-5 lg:col-span-2">
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div>
                <h2 className="text-lg font-semibold">Reading Coach Summary</h2>
                <p className="text-sm text-slate-600">
                  Class-level view of Reading Coach usage and learner progress.
                </p>
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  loadTutorProgress()
                  loadReadingCoachSummary()
                }}
                disabled={progressLoading || loadingReadingCoachSummary}
              >
                {progressLoading || loadingReadingCoachSummary ? 'Refreshing...' : 'Refresh'}
              </Button>
            </div>

            {loadingReadingCoachSummary ? (
              <div className="p-4 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md text-center">
                <p className="text-sm text-[#7a5c3e]">Loading summary...</p>
              </div>
            ) : readingCoachSummaryError ? (
              <div className="p-4 bg-[#fde5e5] border border-[#f2c6c6] rounded-md">
                <p className="text-sm text-[#8c1c1c] font-medium">Summary unavailable</p>
                <p className="text-xs text-[#8c1c1c] mt-1">{readingCoachSummaryError}</p>
              </div>
            ) : !readingCoachSummary ? (
              <div className="p-4 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md text-center">
                <p className="text-sm text-[#7a5c3e]">No Reading Coach summary available yet.</p>
              </div>
            ) : (
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                {[
                  ['Assigned students', ['assignedStudents', 'studentCount', 'studentsAssigned']],
                  ['Assigned cases', ['assignedCases', 'caseCount', 'casesAssigned']],
                  ['Started students', ['startedStudents', 'studentsStarted']],
                  ['Active 24h', ['activeStudentsLast24Hours', 'activeStudentsLast24h', 'activeLast24Hours']],
                  ['Help requests', ['helpRequests', 'helpRequestCount']],
                  ['Chat messages', ['chatMessages', 'chatMessageCount', 'messages']],
                  ['Tutor answers', ['tutorAnswers', 'tutorAnswerCount', 'answers']],
                ].map(([label, keys]) => (
                  <div key={label} className="rounded-md border border-[#f3e0ce] bg-[#fdf4eb] p-3">
                    <div className="flex items-center gap-2 text-xs font-medium text-[#7a5c3c]">
                      <BarChart3 className="h-3.5 w-3.5" />
                      {label}
                    </div>
                    <div className="mt-2 text-2xl font-semibold text-[#2c2218]">
                      {formatMetric(metricValue(readingCoachSummary, keys))}
                    </div>
                  </div>
                ))}
              </div>
            )}

            <div className="border-t pt-4">
              <h3 className="text-sm font-semibold text-[#2c2218]">Detailed progress</h3>
              <p className="text-xs text-slate-600 mt-1">Learner progress on assigned cases.</p>
            </div>

            {progressError ? (
              <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                {progressError}
              </div>
            ) : null}

            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
              <div className="rounded-md border border-slate-200 bg-white p-3">
                <div className="text-2xl font-semibold">{progressSummary.total}</div>
                <div className="text-xs text-slate-600">assigned</div>
              </div>
              <div className="rounded-md border border-slate-200 bg-white p-3">
                <div className="text-2xl font-semibold">{progressSummary.started}</div>
                <div className="text-xs text-slate-600">started</div>
              </div>
              <div className="rounded-md border border-green-200 bg-green-50 p-3">
                <div className="text-2xl font-semibold text-green-700">{progressSummary.onTrack}</div>
                <div className="text-xs text-green-700">on track</div>
              </div>
              <div className="rounded-md border border-red-200 bg-red-50 p-3">
                <div className="text-2xl font-semibold text-red-700">{progressSummary.needsAttention}</div>
                <div className="text-xs text-red-700">need attention</div>
              </div>
              <div className="rounded-md border border-blue-200 bg-blue-50 p-3">
                <div className="text-2xl font-semibold text-blue-700">{progressSummary.completed}</div>
                <div className="text-xs text-blue-700">completed</div>
              </div>
              <div className="rounded-md border border-slate-200 bg-slate-50 p-3">
                <div className="text-2xl font-semibold text-slate-700">{progressSummary.notStarted}</div>
                <div className="text-xs text-slate-600">not started</div>
              </div>
            </div>

            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
              <Input
                value={progressSearch}
                onChange={e => setProgressSearch(e.target.value)}
                placeholder="Search student, case, or step"
                className="md:max-w-sm"
              />
              <div className="flex flex-wrap gap-2">
                {STATUS_OPTIONS.map(option => (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => setProgressFilter(option.value)}
                    className={`rounded-md border px-3 py-1.5 text-sm transition ${
                      progressFilter === option.value
                        ? 'border-[#C96A08] bg-[#fff2e4] text-[#2C2218]'
                        : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50'
                    }`}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>

            {progressLoading ? (
              <div className="rounded-md border border-slate-200 p-4 text-sm text-slate-600">
                Loading Reading Coach progress...
              </div>
            ) : filteredProgress.length ? (
              <div className="overflow-x-auto rounded-md border border-slate-200">
                <table className="min-w-full divide-y divide-slate-200 text-sm">
                  <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th className="px-4 py-3 font-medium">Student</th>
                      <th className="px-4 py-3 font-medium">Case</th>
                      <th className="px-4 py-3 font-medium">Status</th>
                      <th className="px-4 py-3 font-medium">Progress</th>
                      <th className="px-4 py-3 font-medium">Last activity</th>
                      <th className="px-4 py-3 font-medium" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 bg-white">
                    {filteredProgress.map(row => {
                      const studentName = row.studentName || row.studentEmail || 'Learner'
                      const caseName = row.fileName || 'Assigned case'
                      const progressText = `${row.completedSteps || 0}/${row.totalSteps || 0} steps`
                      return (
                        <tr key={`${row.studentId}-${row.uploadId}`} className={row.needsAttention ? 'bg-red-50/40' : ''}>
                          <td className="px-4 py-3">
                            <div className="font-medium text-[#2C2218]">{studentName}</div>
                            {row.studentEmail && row.studentEmail !== studentName ? (
                              <div className="text-xs text-slate-500">{row.studentEmail}</div>
                            ) : null}
                          </td>
                          <td className="px-4 py-3">
                            <div className="max-w-xs truncate text-slate-700" title={caseName}>
                              {caseName}
                            </div>
                            {row.currentStep?.title ? (
                              <div className="text-xs text-slate-500">Current: {row.currentStep.title}</div>
                            ) : null}
                          </td>
                          <td className="px-4 py-3">
                            <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-medium ${statusClass(row.status)}`}>
                              {row.needsAttention ? <AlertTriangle className="h-3.5 w-3.5" /> : row.status === 'completed' ? <CheckCircle2 className="h-3.5 w-3.5" /> : <Clock3 className="h-3.5 w-3.5" />}
                              {normalizeStatus(row.status)}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-slate-700">
                            <div>{progressText}</div>
                            <div className="text-xs text-slate-500">
                              {row.answerAttempts || 0} answers · {row.weakAttempts || 0} weak · {row.helpRequests || 0} help
                            </div>
                          </td>
                          <td className="px-4 py-3 text-slate-600">{formatDate(row.lastActivity)}</td>
                          <td className="px-4 py-3 text-right">
                            <Button
                              variant={row.needsAttention ? 'warm' : 'outline'}
                              size="sm"
                              onClick={() =>
                                navigate(
                                  `/admin/classes/${encodeURIComponent(classId)}/tutor-progress/${encodeURIComponent(row.studentId)}/${encodeURIComponent(row.uploadId)}`
                                )
                              }
                            >
                              View details
                            </Button>
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="rounded-md border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
                {tutorProgress.length
                  ? 'No learners match the current filters.'
                  : 'No Reading Coach activity yet. Assigned students will appear here once cases are available.'}
              </div>
            )}
          </Card>

          {/* History card removed; use sidebar History link (/admin/sessions) */}
        </div>
      )}
    </div>
  )
}
