import React, { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, AlertTriangle, CheckCircle2, Clock3 } from 'lucide-react'
import { getStudentTutorProgress } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'

function formatStatus(value) {
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
  if (raw === 'needs_help') return 'border-red-200 bg-red-50 text-red-700'
  if (raw === 'completed') return 'border-green-200 bg-green-50 text-green-700'
  if (raw === 'in_progress') return 'border-blue-200 bg-blue-50 text-blue-700'
  return 'border-slate-200 bg-slate-50 text-slate-600'
}

function formatDate(value) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function normalizeFeedback(answer) {
  const summary = answer?.feedbackSummary
  if (summary && typeof summary === 'object') {
    return {
      score: summary.Score ?? summary.score ?? answer?.score,
      verdict: summary.Verdict ?? summary.verdict ?? '',
      hint: summary.Hint ?? summary.hint ?? '',
    }
  }

  if (typeof answer?.feedback === 'string') {
    try {
      const parsed = JSON.parse(answer.feedback)
      return {
        score: parsed.Score ?? parsed.score ?? answer?.score,
        verdict: parsed.Verdict ?? parsed.verdict ?? '',
        hint: parsed.Hint ?? parsed.hint ?? '',
      }
    } catch {
      return { score: answer?.score, verdict: answer.feedback, hint: '' }
    }
  }

  return { score: answer?.score, verdict: '', hint: '' }
}

function scoreLabel(score) {
  const value = Number(score)
  if (Number.isNaN(value)) return ''
  if (value >= 0.8) return 'Strong'
  if (value >= 0.55) return 'Developing'
  return 'Needs work'
}

function scoreClass(score) {
  const value = Number(score)
  if (value >= 0.8) return 'border-green-200 bg-green-50 text-green-700'
  if (value >= 0.55) return 'border-amber-200 bg-amber-50 text-amber-700'
  return 'border-red-200 bg-red-50 text-red-700'
}

export default function TutorProgressDetail() {
  const { classId, studentId, uploadId } = useParams()
  const navigate = useNavigate()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [data, setData] = useState(null)

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        setLoading(true)
        setError('')
        const result = await getStudentTutorProgress(classId, studentId, uploadId)
        if (!cancelled) setData(result)
      } catch (err) {
        if (!cancelled) setError(err?.message || 'Failed to load Reading Coach detail')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [classId, studentId, uploadId])

  const view = useMemo(() => {
    const learnerName =
      data?.studentName || data?.student?.fullName || data?.studentEmail || data?.student?.email || 'Learner'
    const learnerEmail = data?.studentEmail || data?.student?.email || ''
    const caseName =
      data?.caseName ||
      data?.caseFileName ||
      data?.caseInfo?.name ||
      data?.caseInfo?.originalFileName ||
      'Assigned case'

    return { learnerName, learnerEmail, caseName }
  }, [data])

  if (loading) {
    return <div className="p-6 md:p-8">Loading Reading Coach progress...</div>
  }

  if (error) {
    return (
      <div className="p-6 md:p-8 max-w-5xl mx-auto">
        <Button variant="ghost" size="sm" onClick={() => navigate(`/admin/classes/${classId}`)}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to class
        </Button>
        <Card className="mt-4 p-6 text-red-700">{error}</Card>
      </div>
    )
  }

  const answers = Array.isArray(data?.answers) ? data.answers : []
  const helpEvents = Array.isArray(data?.helpEvents) ? data.helpEvents : []
  const needsAttention = data?.needsAttention || data?.status === 'needs_help'

  return (
    <div className="p-4 md:p-6 max-w-6xl mx-auto space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <Button variant="ghost" size="sm" onClick={() => navigate(`/admin/classes/${classId}`)}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to class
          </Button>
          <div>
            <h1 className="text-2xl md:text-3xl font-bold">Reading Coach Progress</h1>
            <p className="text-sm text-slate-600">
              {view.learnerName} · {view.caseName}
            </p>
            {view.learnerEmail && view.learnerEmail !== view.learnerName ? (
              <p className="text-xs text-slate-500">{view.learnerEmail}</p>
            ) : null}
          </div>
        </div>
        <span className={`inline-flex items-center gap-1 rounded-full border px-3 py-1.5 text-sm font-medium ${statusClass(data?.status)}`}>
          {needsAttention ? <AlertTriangle className="h-4 w-4" /> : data?.status === 'completed' ? <CheckCircle2 className="h-4 w-4" /> : <Clock3 className="h-4 w-4" />}
          {formatStatus(data?.status)}
        </span>
      </div>

      {needsAttention ? (
        <div className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          This learner may need follow-up on this reading step.
        </div>
      ) : null}

      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <Card className="p-4">
          <div className="text-xs text-slate-500">Current step</div>
          <div className="mt-1 font-semibold">{data?.currentStep?.title || 'Not started'}</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs text-slate-500">Completed</div>
          <div className="mt-1 font-semibold">{data?.completedSteps || 0}/{data?.totalSteps || 0}</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs text-slate-500">Answers</div>
          <div className="mt-1 font-semibold">{data?.answerAttempts || answers.length}</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs text-slate-500">Weak attempts</div>
          <div className="mt-1 font-semibold">{data?.weakAttempts || 0}</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs text-slate-500">Help requests</div>
          <div className="mt-1 font-semibold">{data?.helpRequests || helpEvents.length}</div>
        </Card>
      </div>

      <Card className="p-6 space-y-4">
        <div>
          <h2 className="text-lg font-semibold">Answers</h2>
          <p className="text-sm text-slate-600">{answers.length} responses</p>
        </div>

        {answers.length ? (
          <div className="space-y-4">
            {answers.map((answer, index) => {
              const feedback = normalizeFeedback(answer)
              const score = Number(feedback.score ?? answer.score)
              return (
                <div key={answer.id || `${answer.stepId}-${index}`} className="rounded-md border border-slate-200 p-4">
                  <div className="flex flex-col gap-2 md:flex-row md:items-start md:justify-between">
                    <div>
                      <div className="text-sm font-semibold">{answer.stepTitle || answer.stepId || 'Reading step'}</div>
                      <p className="mt-1 text-sm text-slate-700">{answer.question}</p>
                    </div>
                    {!Number.isNaN(score) ? (
                      <span className={`inline-flex w-fit rounded-full border px-2.5 py-1 text-xs font-medium ${scoreClass(score)}`}>
                        {score.toFixed(2)} {scoreLabel(score)}
                      </span>
                    ) : null}
                  </div>

                  {answer.answer ? (
                    <div className="mt-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">Student answer</div>
                      <p className="mt-1 text-sm text-slate-800">{answer.answer}</p>
                    </div>
                  ) : null}

                  {feedback.verdict ? (
                    <div className="mt-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">Verdict</div>
                      <p className="mt-1 text-sm text-slate-800">{feedback.verdict}</p>
                    </div>
                  ) : null}

                  {feedback.hint ? (
                    <div className="mt-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">Coaching note</div>
                      <p className="mt-1 text-sm text-slate-800">{feedback.hint}</p>
                    </div>
                  ) : null}

                  <div className="mt-4 text-xs text-slate-500">
                    {formatDate(answer.createdAt)}
                  </div>
                </div>
              )
            })}
          </div>
        ) : (
          <div className="rounded-md border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
            No Reading Coach answers yet.
          </div>
        )}
      </Card>

      {helpEvents.length ? (
        <Card className="p-6 space-y-3">
          <h2 className="text-lg font-semibold">Help requests</h2>
          {helpEvents.map((event, index) => (
            <div key={event.id || index} className="rounded-md border border-slate-200 p-3 text-sm">
              <div className="font-medium">{event.question || `Help request ${index + 1}`}</div>
              {event.createdAt ? <div className="mt-1 text-xs text-slate-500">{formatDate(event.createdAt)}</div> : null}
            </div>
          ))}
        </Card>
      ) : null}
    </div>
  )
}
