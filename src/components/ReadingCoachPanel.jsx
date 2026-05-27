import React from 'react'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { AlertCircle, CheckCircle, HelpCircle } from 'lucide-react'

export default function ReadingCoachPanel({
  state,
  loading,
  error,
  answerDraft,
  onAnswerChange,
  onSubmit,
  submitting,
  onAskForHelp,
  onRetry,
}) {
  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center p-4">
        <div className="text-center">
          <div className="animate-spin h-8 w-8 border-4 border-[#C96A0A] border-t-transparent rounded-full mx-auto mb-4" />
          <p className="text-sm text-[#5C4C3C]">Loading Reading Coach...</p>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center p-4">
        <div className="text-center">
          <AlertCircle className="h-12 w-12 text-amber-500 mx-auto mb-4" />
          <p className="text-sm text-[#5C4C3C]">{error}</p>
        </div>
      </div>
    )
  }

  if (!state) {
    return (
      <div className="flex-1 flex items-center justify-center p-4">
        <div className="text-center space-y-3">
          <p className="text-sm text-[#5C4C3C]">Starting Reading Coach…</p>
          {onRetry && (
            <Button onClick={onRetry} variant="outline" className="mx-auto">
              Retry
            </Button>
          )}
        </div>
      </div>
    )
  }

  const {
    stepNumber,
    totalSteps,
    stepSummary,
    narrative,
    cites = [],
    question,
    feedback,
    stage,
  } = state

  // Determine feedback color based on score
  const scoreColor =
    feedback?.score >= 0.75 ? 'text-green-600' : feedback?.score >= 0.55 ? 'text-amber-600' : 'text-red-600'

  const scoreIcon =
    feedback?.score >= 0.75 ? <CheckCircle className="w-4 h-4" /> : <AlertCircle className="w-4 h-4" />

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-white">
      {/* Header */}
      <div className="border-b border-[#e4d6c7] p-4">
        <div className="flex items-center justify-between mb-2">
          <h3 className="font-semibold text-[#2C2218]">Reading Coach</h3>
          <span className="text-xs text-[#5C4C3C]">
            Step {stepNumber} of {totalSteps}
          </span>
        </div>
        <p className="text-sm font-medium text-[#C96A08]">{stepSummary}</p>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4 space-y-4">
        {/* Narrative */}
        {narrative && (
          <div className="rounded-2xl border border-[#e4d6c7] bg-[#faf8f5] p-4 space-y-3">
            <div className="text-sm leading-relaxed text-[#5C4C3C] whitespace-pre-wrap">
              {narrative}
            </div>

            {/* Citation chips */}
            {cites && cites.length > 0 && (
              <div className="flex flex-wrap gap-2 pt-2">
                {cites.map(page => (
                  <span
                    key={`p${page}`}
                    className="inline-block px-2 py-1 text-xs rounded-full border border-[#E4C6A1] bg-[#F6EEE5] text-[#6A3A0A]"
                  >
                    p:{page}
                  </span>
                ))}
              </div>
            )}
          </div>
        )}

        {/* Feedback (if stage is retry or recap) */}
        {feedback && (
          <div className={`rounded-lg border p-3 space-y-2 ${feedback.score >= 0.75 ? 'border-green-200 bg-green-50' : feedback.score >= 0.55 ? 'border-amber-200 bg-amber-50' : 'border-red-200 bg-red-50'}`}>
            <div className={`flex items-center gap-2 font-semibold ${scoreColor}`}>
              {scoreIcon}
              <span>{feedback.verdict || 'Feedback'}</span>
            </div>
            {feedback.hint && <p className="text-xs text-[#5C4C3C]">{feedback.hint}</p>}
          </div>
        )}

        {/* Question & Answer (only if stage !== recap) */}
        {stage !== 'recap' && question && (
          <div className="space-y-3">
            <div className="text-sm font-medium text-[#2C2218]">{question}</div>
            <div className="space-y-2">
              <Textarea
                placeholder="Enter your answer here..."
                value={answerDraft}
                onChange={e => onAnswerChange(e.target.value)}
                className="w-full min-h-[80px] max-h-[160px] resize-none px-3 py-2 leading-relaxed text-sm focus-visible:border-[#C96A0A] focus-visible:outline-none"
                disabled={submitting}
              />
              <div className="flex gap-2">
                <Button
                  onClick={onSubmit}
                  disabled={submitting || !answerDraft.trim()}
                  className="flex-1 bg-[#C96A08] text-white hover:bg-[#b85f0a] disabled:opacity-50"
                >
                  {submitting ? 'Submitting...' : 'Submit Answer'}
                </Button>
                <Button
                  onClick={onAskForHelp}
                  variant="outline"
                  className="flex-1"
                >
                  <HelpCircle className="w-4 h-4 mr-1" />
                  Ask for Help
                </Button>
              </div>
            </div>
          </div>
        )}

        {/* Recap section (if stage === recap) */}
        {stage === 'recap' && (
          <div className="space-y-4">
            <div className="rounded-2xl border border-[#e4d6c7] bg-[#faf8f5] p-4">
              <h4 className="font-semibold text-[#2C2218] mb-2">Final Recap</h4>
              <div className="text-sm leading-relaxed text-[#5C4C3C] whitespace-pre-wrap">
                {narrative}
              </div>
            </div>
            <div className="space-y-2">
              <Button className="w-full bg-[#C96A08] text-white hover:bg-[#b85f0a]">
                Save Recap to Notes
              </Button>
              <Button variant="outline" className="w-full">
                Go to Guided Analysis
              </Button>
              <Button variant="outline" className="w-full">
                Ask Follow-up in Chat
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
