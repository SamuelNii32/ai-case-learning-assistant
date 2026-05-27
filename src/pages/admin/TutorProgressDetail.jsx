import React, { useEffect, useState, useContext } from 'react'
import { useParams, Link } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import { getStudentTutorProgress } from '@/lib/api'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { ArrowLeft } from 'lucide-react'

function normalizeData(data) {
  // Normalize field names defensively to handle various backend shapes
  const normalize = (obj, mappings) => {
    if (!obj || typeof obj !== 'object') return obj
    const result = { ...obj }
    for (const [oldName, newName] of Object.entries(mappings)) {
      if (oldName in result && !(newName in result)) {
        result[newName] = result[oldName]
      }
    }
    return result
  }

  const fieldMappings = {
    userId: 'studentId',
    fullName: 'studentName',
    caseId: 'uploadId',
    fileName: 'caseName',
    originalFileName: 'caseName',
    currentStep: 'currentNode',
    latestStep: 'currentNode',
    lastActivityAt: 'updatedAt',
  }

  return normalize(data, fieldMappings)
}

function renderText(value, fallback = '—') {
  if (value == null || value === '') return fallback
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  if (Array.isArray(value)) {
    return value.map(item => renderText(item, '')).filter(Boolean).join(', ') || fallback
  }
  if (typeof value === 'object') {
    return (
      value.title ??
      value.name ??
      value.label ??
      value.text ??
      value.status ??
      value.value ??
      value.fullName ??
      fallback
    )
  }
  return String(value)
}

function pickFirstText(...values) {
  for (const value of values) {
    const text = renderText(value, '')
    if (text && text !== '—') return text
  }
  return '—'
}

function titleCase(value) {
  const text = renderText(value, '')
  if (!text || text === '—') return '—'
  return text
    .toString()
    .replace(/[_-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
    .replace(/\b\w/g, ch => ch.toUpperCase())
}

function formatStatus(value) {
  const status = renderText(value, '').toLowerCase()
  if (!status) return '—'
  if (status === 'needs_help') return 'Needs attention'
  if (status === 'in_progress') return 'In progress'
  if (status === 'completed') return 'Completed'
  if (status === 'not_started') return 'Not started'
  return titleCase(status)
}

function statusBadgeClass(value) {
  const status = renderText(value, '').toLowerCase()
  if (status === 'needs_help') return 'bg-amber-100 text-amber-800 border-amber-200'
  if (status === 'completed') return 'bg-emerald-100 text-emerald-800 border-emerald-200'
  if (status === 'in_progress') return 'bg-sky-100 text-sky-800 border-sky-200'
  if (status === 'not_started') return 'bg-slate-100 text-slate-700 border-slate-200'
  return 'bg-[#fdf4eb] text-[#7a5c3c] border-[#f3e0ce]'
}

function scoreBand(score) {
  const num = Number(score)
  if (Number.isNaN(num)) return '—'
  if (num >= 0.8) return 'Strong'
  if (num >= 0.55) return 'Developing'
  return 'Needs work'
}

function formatScore(score) {
  const num = Number(score)
  if (Number.isNaN(num)) return '—'
  return `${num.toFixed(num >= 1 ? 0 : 2)} ${scoreBand(num)}`
}

function renderTimestamp(value) {
  const text = renderText(value, '')
  if (!text || text === '—') return '—'
  const parsed = new Date(text)
  return Number.isNaN(parsed.getTime()) ? text : parsed.toLocaleString()
}

function getStableKey(item, index, prefix) {
  const parts = [
    item?.id,
    item?.stepId,
    item?.eventId,
    item?.questionId,
    item?.nodeId,
    item?.title,
    item?.name,
    index,
  ]
    .map(value => renderText(value, ''))
    .filter(Boolean)
  return `${prefix}-${parts.join('-') || index}`
}

function normalizeFeedback(answer) {
  const source = answer?.feedbackSummary ?? answer?.feedback
  let parsed = source

  if (typeof source === 'string') {
    const trimmed = source.trim()
    if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
      try {
        parsed = JSON.parse(trimmed)
      } catch {
        parsed = source
      }
    }
  }

  if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
    return {
      score: parsed.Score ?? parsed.score ?? answer?.score,
      verdict: parsed.Verdict ?? parsed.verdict ?? '',
      hint: parsed.Hint ?? parsed.hint ?? '',
      rawText: '',
    }
  }

  return {
    score: answer?.score,
    verdict: '',
    hint: '',
    rawText: renderText(parsed, ''),
  }
}

function answerToText(answer, fallback = '—') {
  return pickFirstText(answer?.answer, answer?.text, answer?.response, answer?.content, fallback)
}

export default function TutorProgressDetail() {
  const { classId, studentId, uploadId } = useParams()
  const auth = useContext(AuthContext)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [data, setData] = useState(null)

  const learnerName =
    data?.studentName ||
    data?.student?.fullName ||
    data?.studentEmail ||
    data?.student?.email ||
    'Learner'
  const learnerEmail = data?.studentEmail || data?.student?.email || ''
  const caseName =
    data?.caseName ||
    data?.caseFileName ||
    data?.caseInfo?.name ||
    data?.caseInfo?.originalFileName ||
    'Assigned case'
  const uploadIdValue = data?.uploadId || data?.caseInfo?.uploadId || ''
  const currentStep = data?.currentStep || {}
  const statusValue = data?.status || data?.state || '—'
  const needsAttention = Boolean(
    data?.needsAttention || renderText(data?.status, '').toLowerCase() === 'needs_help'
  )
  const answers = Array.isArray(data?.answers) ? data.answers : []
  const helpEvents = Array.isArray(data?.helpEvents) ? data.helpEvents : []
  const helpEventsWithQuestion = helpEvents.filter(event => {
    const questionText = pickFirstText(event?.question, event?.message, event?.title, '')
    return questionText !== '—'
  })

  if (import.meta.env.DEV) {
    console.log('[TutorProgressDetail]', data)
  }

  useEffect(() => {
    if (!auth?.loggedIn || auth?.user?.role !== 'instructor') {
      return
    }

    let cancelled = false

    ;(async () => {
      try {
        setLoading(true)
        setError(null)
        const result = await getStudentTutorProgress(classId, studentId, uploadId)
        if (!cancelled) {
          setData(normalizeData(result))
        }
      } catch (err) {
        if (!cancelled) {
          console.error('Failed to load tutor progress:', err)
          setError(err?.message || 'Failed to load progress')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [classId, studentId, uploadId, auth?.loggedIn, auth?.user?.role])

  if (!auth?.loggedIn || auth?.user?.role !== 'instructor') {
    return (
      <div className="min-h-screen bg-[#faf6f0] py-10">
        <div className="max-w-5xl mx-auto px-4">
          <div className="p-6 md:p-8 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm">
            <h2 className="text-2xl font-bold text-[#2c2218]">Access denied</h2>
            <p className="mt-2 text-sm text-[#7a5c3c]">Instructor access required.</p>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-[#faf6f0] py-10">
      <div className="max-w-5xl mx-auto px-4 space-y-6">
        <div className="flex items-center gap-3">
          <Link to={`/admin/classes/${encodeURIComponent(classId)}`}>
            <Button variant="ghost" size="sm" className="inline-flex items-center gap-2">
              <ArrowLeft className="h-4 w-4" />
              Back
            </Button>
          </Link>
        </div>

        <Card className="p-5 md:p-6 bg-white border border-[#f4e7d8] rounded-[12px] shadow-sm">
          <div className="flex flex-col gap-2">
            <div>
              <h1 className="text-2xl md:text-3xl font-bold text-[#2c2218]">
                Reading Coach Progress
              </h1>
              <p className="mt-1 text-sm text-[#5C4C3C]">
                {learnerName} · {caseName}
              </p>
              {learnerEmail ? (
                <div className="mt-1 text-sm text-[#8b7462]">{learnerEmail}</div>
              ) : null}
            </div>
          </div>
        </Card>

        {loading ? (
          <Card className="p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm text-center">
            <p className="text-[#7a5c3e]">Loading progress details…</p>
          </Card>
        ) : error ? (
          <Card className="p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm">
            <p className="text-[#c76008] font-medium">Error</p>
            <p className="text-sm text-[#7a5c3e] mt-2">{error}</p>
          </Card>
        ) : !data ? (
          <Card className="p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm text-center">
            <p className="text-[#7a5c3e]">No progress data found.</p>
          </Card>
        ) : (
          <div className="space-y-6">
            {(needsAttention || renderText(statusValue, '').toLowerCase() === 'needs_help') && (
              <Card className="p-4 bg-amber-50 border border-amber-200 rounded-[12px] shadow-sm">
                <p className="text-sm font-medium text-amber-900">
                  This learner may need follow-up on this reading step.
                </p>
              </Card>
            )}

            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
              {[
                {
                  label: 'Status',
                  value: formatStatus(statusValue),
                  raw: statusValue,
                },
                {
                  label: 'Current step',
                  value: pickFirstText(currentStep?.title, currentStep?.name, currentStep?.id, data.currentNode, data.latestStep),
                },
                {
                  label: 'Completed steps',
                  value: renderText(data.completedSteps ?? data.totalCompletedSteps ?? data.stepsCompleted ?? data.totalStepsCompleted, '—'),
                },
                { label: 'Answer attempts', value: renderText(data.answerAttempts, '—') },
                { label: 'Weak attempts', value: renderText(data.weakAttempts, '—') },
                { label: 'Help requests', value: renderText(data.helpRequests, '—') },
              ].map(card => (
                <Card key={card.label} className="p-4 bg-white border border-[#f3e0ce] rounded-[12px] shadow-sm">
                  <div className="text-[11px] uppercase tracking-wide text-[#8b7462]">{card.label}</div>
                  <div className="mt-2 flex items-center gap-2 flex-wrap">
                    <span
                      className={`inline-flex items-center rounded-full border px-2.5 py-1 text-xs font-semibold ${
                        card.label === 'Status'
                          ? statusBadgeClass(card.raw ?? card.value)
                          : 'bg-[#fdf4eb] text-[#2c2218] border-[#f3e0ce]'
                      }`}
                    >
                      {card.value}
                    </span>
                  </div>
                </Card>
              ))}
            </div>

            <Card className="p-5 md:p-6 bg-white border border-[#f4e7d8] rounded-[12px] shadow-sm">
              <h2 className="text-lg font-semibold text-[#2c2218] mb-4">Learner summary</h2>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6 text-sm">
                <div>
                  <div className="font-medium text-[#2c2218]">{learnerName}</div>
                  {learnerEmail ? <div className="text-[#7a5c3c]">{learnerEmail}</div> : null}
                </div>
                <div>
                  <div className="font-medium text-[#2c2218]">{caseName}</div>
                </div>
              </div>
            </Card>

            <Card className="p-5 md:p-6 bg-white border border-[#f4e7d8] rounded-[12px] shadow-sm">
              <div className="flex items-center justify-between gap-4 mb-4">
                <h2 className="text-lg font-semibold text-[#2c2218]">Answers</h2>
                <span className="text-xs text-[#7a5c3c]">{answers.length} response{answers.length === 1 ? '' : 's'}</span>
              </div>

              {answers.length === 0 ? (
                <div className="rounded-[10px] border border-dashed border-[#e4d6c7] bg-[#fdfbf8] p-4 text-sm text-[#7a5c3c]">
                  No Reading Coach answers yet.
                </div>
              ) : (
                <div className="space-y-4">
                  {answers.map((answer, idx) => {
                    const feedback = normalizeFeedback(answer)
                    const scoreText = formatScore(feedback.score)
                    const answerText = answerToText(answer, '')
                    const stepLabel = pickFirstText(answer?.stepTitle, answer?.stepId, `Step ${idx + 1}`)

                    return (
                      <div
                        key={getStableKey(answer, idx, 'answer')}
                        className="rounded-[12px] border border-[#f3e0ce] bg-[#fffdfb] p-4 shadow-sm"
                      >
                        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                          <div className="min-w-0">
                            <div className="text-xs uppercase tracking-wide text-[#8b7462]">{renderText(stepLabel)}</div>
                            {answer.question && (
                              <div className="mt-2 text-sm font-medium text-[#2c2218]">{renderText(answer.question)}</div>
                            )}
                          </div>
                          {feedback.score !== undefined && (
                            <span className="inline-flex w-fit items-center rounded-full border border-[#f3e0ce] bg-[#fdf4eb] px-2.5 py-1 text-xs font-semibold text-[#7a5c3c]">
                              {scoreText}
                            </span>
                          )}
                        </div>

                        {answerText && (
                          <div className="mt-3 text-sm text-[#2c2218] whitespace-pre-wrap">
                            <div className="text-[11px] uppercase tracking-wide text-[#8b7462] mb-1">Student answer</div>
                            {answerText}
                          </div>
                        )}

                        {(feedback.verdict || feedback.hint || feedback.rawText) && (
                          <div className="mt-4 grid gap-3 md:grid-cols-2">
                            {feedback.verdict && (
                              <div className="rounded-[10px] bg-[#f8fafc] border border-[#e2e8f0] p-3">
                                <div className="text-[11px] uppercase tracking-wide text-[#64748b]">Verdict</div>
                                <div className="mt-1 text-sm text-[#1e293b]">{renderText(feedback.verdict)}</div>
                              </div>
                            )}
                            {feedback.hint && (
                              <div className="rounded-[10px] bg-[#f8fafc] border border-[#e2e8f0] p-3">
                                <div className="text-[11px] uppercase tracking-wide text-[#64748b]">Hint</div>
                                <div className="mt-1 text-sm text-[#1e293b]">{renderText(feedback.hint)}</div>
                              </div>
                            )}
                          </div>
                        )}

                        {feedback.rawText && !feedback.verdict && !feedback.hint && (
                          <div className="mt-4 rounded-[10px] bg-[#f8fafc] border border-[#e2e8f0] p-3">
                            <div className="text-[11px] uppercase tracking-wide text-[#64748b]">Feedback</div>
                            <div className="mt-1 text-sm text-[#1e293b] whitespace-pre-wrap">{feedback.rawText}</div>
                          </div>
                        )}

                        <div className="mt-4 flex flex-wrap items-center gap-3 text-xs text-[#7a5c3c]">
                          {answer.createdAt && <span>Created {renderTimestamp(answer.createdAt)}</span>}
                          {answer.stepId && <span>Step ID: {renderText(answer.stepId)}</span>}
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}
            </Card>

            {helpEventsWithQuestion.length > 0 ? (
              <Card className="p-5 md:p-6 bg-white border border-[#f4e7d8] rounded-[12px] shadow-sm">
                <h2 className="text-lg font-semibold text-[#2c2218] mb-4">Help requests</h2>
                <div className="space-y-3">
                  {helpEventsWithQuestion.map((event, idx) => (
                    <div key={getStableKey(event, idx, 'help')} className="rounded-[10px] border border-[#f3e0ce] bg-[#fdfbf8] p-3 text-sm text-[#2c2218]">
                      <div className="flex flex-wrap items-center gap-2 text-xs text-[#7a5c3c]">
                        <span className="font-medium text-[#2c2218]">{renderText(event.question || event.title || event.type || `Help request ${idx + 1}`)}</span>
                        {event.createdAt && <span>· {renderTimestamp(event.createdAt)}</span>}
                      </div>
                      {event.message && <div className="mt-2 whitespace-pre-wrap">{renderText(event.message)}</div>}
                    </div>
                  ))}
                </div>
              </Card>
            ) : null}

            {uploadIdValue ? (
              <div className="text-[11px] text-[#8b7462] text-right">
                Upload ID: {renderText(uploadIdValue)}
              </div>
            ) : null}
          </div>
        )}
      </div>
    </div>
  )
}
