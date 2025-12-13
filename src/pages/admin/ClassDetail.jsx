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
} from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Card } from '@/components/ui/card'
import { ArrowLeft, Users, BookOpen } from 'lucide-react'
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [classId])

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
              {details?.name || 'Class'}
            </h1>
            {details?.description && (
              <p className="text-sm text-slate-600 mt-1">{details.description}</p>
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
              <Button type="submit" disabled={addingStudent} className="w-full sm:w-auto">
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
                  className="w-full px-3 py-2 border border-slate-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
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
              <Button type="submit" disabled={assigningCase || !uploadId} className="w-full sm:w-auto">
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

          {/* History card removed; use sidebar History link (/admin/sessions) */}
        </div>
      )}
    </div>
  )
}
