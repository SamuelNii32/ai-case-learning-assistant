import React from 'react'
import { Button } from './ui/button'
import { ArrowRight, ChevronRight, RefreshCw, Sparkles } from 'lucide-react'

import { getChoiceFocusMeta } from './guidedModeCatalog'

function ThinkingIndicator() {
  return (
    <div className="thinking-indicator">
      <Sparkles className="thinking-indicator-icon h-4 w-4" />
      <span>CasePilot is reading the next passage</span>
      <div className="thinking-dots">
        <div className="thinking-dot" />
        <div className="thinking-dot" />
        <div className="thinking-dot" />
      </div>
    </div>
  )
}

function splitNarrative(value) {
  return String(value || '')
    .split('\n\n')
    .map(part => part.trim())
    .filter(Boolean)
}

function NarrativeBlock({ narrative, showRevealAnimation = false }) {
  const paragraphs = splitNarrative(narrative)

  if (!paragraphs.length) {
    return null
  }

  return (
    <div className={`rounded-3xl border border-[#e4d6c7] bg-white p-6 shadow-sm ${showRevealAnimation ? 'narrative-block-reveal' : ''}`}>
      <div className="space-y-4 text-sm leading-relaxed text-[#5C4C3C]">
        {paragraphs.map((paragraph, idx) => (
          <p key={idx} className={showRevealAnimation ? 'paragraph-reveal' : ''} style={showRevealAnimation ? { '--paragraph-index': idx } : {}}>
            {paragraph}
          </p>
        ))}
      </div>
    </div>
  )
}

function FocusCard({ choice, onChoice, isLoading }) {
  const meta = getChoiceFocusMeta(choice)
  const cardNumber = String(choice.order || choice.id || '').replace(/\D/g, '')

  return (
    <button
      type="button"
      disabled={isLoading}
      onClick={() => onChoice(choice.id)}
      className="focus-card group flex h-full min-h-[204px] w-full flex-col justify-between text-left disabled:cursor-not-allowed disabled:opacity-70"
    >
      <span className="accent-bar" aria-hidden="true" />

      <div className="space-y-4">
        <div className="flex items-center justify-between text-xs font-medium text-[#7c6758]">
          <span>{cardNumber ? `0${cardNumber}`.slice(-2) : '01'}</span>
          <span>
            {meta.stepCount ? `${meta.stepCount} steps` : 'Focus path'}
            {meta.duration ? ` • ${meta.duration}` : ''}
          </span>
        </div>
        <div className="space-y-2">
          <p className="card-title">
            {meta.title}
          </p>
          <p className="card-desc">{meta.description}</p>
        </div>
      </div>
      <div className="card-footer mt-6 flex items-center justify-start gap-2">
        <span className="begin-label">Begin</span>
        <ArrowRight className="begin-arrow" />
      </div>
    </button>
  )
}

function PathChoiceCard({ choice, onChoice, isLoading, loadingChoiceId }) {
  const meta = getChoiceFocusMeta(choice)
  const isThisChoiceLoading = loadingChoiceId === choice.id

  return (
    <button
      type="button"
      disabled={isLoading}
      onClick={() => onChoice(choice.id)}
      className="choice-card group flex w-full items-center justify-between gap-4 rounded-[0.75rem] border border-[#e4d6c7] bg-white px-6 py-5 text-left shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#C96A08]/25 disabled:cursor-not-allowed disabled:opacity-50"
    >
      <div className="min-w-0 space-y-1 pr-2">
        <p className="text-base font-medium text-[#2C2218]">{meta.title}</p>
        <p className="text-sm leading-relaxed text-[#5C4C3C]">{meta.description}</p>
      </div>
      <div className="choice-card-icon flex flex-shrink-0 items-center gap-2 text-sm font-medium text-[#5C4C3C]">
        {isThisChoiceLoading ? (
          <>
            <span className="text-[#C96A08]">Loading</span>
            <div className="inline-flex h-4 w-4 items-center justify-center">
              <div className="animate-spin h-3 w-3 border-2 border-[#C96A08] border-t-transparent rounded-full" />
            </div>
          </>
        ) : (
          <>
            <span>Continue</span>
            <ChevronRight className="h-4 w-4 transition-transform duration-300" />
          </>
        )}
      </div>
    </button>
  )
}

function CitationRail({ title, citations }) {
  return (
    <div className="rounded-3xl border border-[#e4d6c7] bg-white p-5 shadow-sm">
      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-[#9a8577]">
        {title}
      </p>
      <div className="mt-3 flex flex-wrap gap-2">
        {Array.isArray(citations) && citations.length > 0 ? (
          citations.map(citation => (
            <span
              key={citation}
              className="rounded-full border border-[#e4d6c7] bg-[#faf8f5] px-2.5 py-1 text-xs font-medium text-[#5C4C3C]"
            >
              [p:{citation}]
            </span>
          ))
        ) : (
          <span className="text-sm text-[#5C4C3C]">No citations yet</span>
        )}
      </div>
    </div>
  )
}

export default function GuidedModeFlow({
  tutorStep,
  onChoice,
  isLoading,
  loadingChoiceId,
  activePathTitle,
  onResetPath,
}) {
  if (!tutorStep) return null

  const stage = String(tutorStep.stage || 'overview').toLowerCase()
  const rawNarrative = tutorStep.narrative || ''
  // Skip showing intro/setup text that looks like "The document appears to be..."
  const narrativeText = rawNarrative.toLowerCase().startsWith('the document appears') ? '' : rawNarrative
  const choices = Array.isArray(tutorStep.choices) ? tutorStep.choices : []
  const isRecap = stage === 'recap'
  const isEntry = !activePathTitle && !isRecap
  const stepSummary = tutorStep.stepSummary && tutorStep.stepSummary !== 'Tutor start' ? tutorStep.stepSummary : ''
  // Don't show summary if it's just intro text or if we're at an entry point
  const displaySummary = stepSummary && !isEntry && !stepSummary.toLowerCase().startsWith('the document appears') ? stepSummary : ''

  if (isRecap) {
    return (
      <section className="space-y-6 rounded-3xl border border-[#e4d6c7] bg-[#faf8f5] p-6 shadow-sm font-serif">
        <div className="space-y-2">
          <div className="inline-flex items-center gap-2 rounded-full border border-[#e4d6c7] bg-white px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-[#9a8577]">
            <Sparkles className="h-3.5 w-3.5 text-[#C96A08]" />
            Guided Mode
          </div>
          <h2 className="text-3xl font-semibold tracking-tight text-[#2C2218]">Path Summary</h2>
          <p className="max-w-3xl text-sm leading-relaxed text-[#5C4C3C]">
            You've reached the end of this path. Review the summary below, then choose a new
            focus area.
          </p>
        </div>

        <div className="space-y-4">
          <NarrativeBlock narrative={narrativeText} />
          {displaySummary && (
            <div className="rounded-2xl border border-[#e4d6c7] bg-white px-4 py-3 text-sm text-[#5C4C3C] shadow-sm">
              {displaySummary}
            </div>
          )}

          {onResetPath && (
            <Button
              type="button"
              variant="outline"
              onClick={onResetPath}
              className="h-auto min-h-11 w-full px-4 py-3"
            >
              <RefreshCw className="mr-2 h-4 w-4" />
              Explore another area
            </Button>
          )}
        </div>
      </section>
    )
  }

  if (isEntry) {
    return (
      <section className="space-y-10 rounded-3xl border border-[#e4d6c7] bg-[#faf8f5] p-8 shadow-sm font-serif">
        <div className="space-y-6 max-w-3xl">
          <div className="inline-flex items-center gap-2 rounded-full border border-[#e4d6c7] bg-white px-3 py-1 text-[0.8rem] font-semibold uppercase tracking-[0.24em] text-[#C96A08] shadow-sm">
            <Sparkles className="h-3.5 w-3.5 text-[#C96A08]" />
            Guided Mode
          </div>
          <h2
            className="max-w-2xl text-[2.2rem] leading-[1.05] font-semibold tracking-tight text-[#2C2218] lg:text-[2.6rem]"
            style={{ fontFamily: 'Fraunces, serif' }}
          >
            Start a guided analysis.
          </h2>
          <p className="max-w-2xl text-[0.98rem] leading-[1.65] text-[#5C4C3C] lg:text-[1.05rem]">
            Choose a direction. CasePilot will walk you through the document step by step,
            grounding every claim in the source pages.
          </p>
        </div>

        <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-2">
          {choices.map(choice => (
            <FocusCard
              key={choice.id}
              choice={choice}
              onChoice={onChoice}
              isLoading={isLoading}
            />
          ))}
        </div>

        {displaySummary && <div className="text-xs italic text-[#9a8577]">{displaySummary}</div>}
      </section>
    )
  }

  return (
    <section className="space-y-5 rounded-3xl border border-[#e4d6c7] bg-[#faf8f5] p-6 shadow-sm font-serif">
      <div className="flex flex-col gap-4 border-b border-[#e4d6c7] pb-5">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-full bg-white px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-[#C96A08] shadow-sm">
              Guided Path
            </span>
          </div>
          <h2 className="text-3xl font-semibold tracking-tight text-[#2C2218]">
            {activePathTitle || 'Structured analysis'}
          </h2>
          <p className="max-w-3xl text-sm leading-relaxed text-[#5C4C3C]">
            Follow the path, read the evidence, and choose the next analytical move.
          </p>
        </div>
      </div>

      <div className="space-y-4">
        {!loadingChoiceId && narrativeText && <NarrativeBlock narrative={narrativeText} showRevealAnimation={true} />}

        {!loadingChoiceId && displaySummary && (
          <div className="pullquote-reveal rounded-2xl border border-[#e4d6c7] bg-white px-4 py-3 text-sm text-[#5C4C3C] shadow-sm" style={{ opacity: 0 }}>
            {displaySummary}
          </div>
        )}

        {loadingChoiceId && (
          <ThinkingIndicator />
        )}

        {choices.length > 0 && (
          <div className={`space-y-3 ${!loadingChoiceId ? 'choices-reveal' : ''}`}>
            <div className="space-y-1">
              <p className="text-sm font-semibold text-[#2C2218]">
                Continue this analysis
              </p>
              <p className="text-xs text-[#5C4C3C]">
                Choose a direction
              </p>
            </div>

            <div className="space-y-3">
              {choices.map(choice => (
                <PathChoiceCard
                  key={choice.id}
                  choice={choice}
                  onChoice={onChoice}
                  isLoading={isLoading}
                  loadingChoiceId={loadingChoiceId}
                />
              ))}
            </div>
          </div>
        )}

        {!choices.length && isLoading && (
          <div className="flex items-center justify-center py-6">
            <div className="text-center">
              <div className="animate-spin h-8 w-8 border-4 border-[#C96A08] border-t-transparent rounded-full mx-auto mb-3" />
              <p className="text-sm text-[#5C4C3C]">Loading next step...</p>
            </div>
          </div>
        )}

        {onResetPath && (
          <Button
            type="button"
            variant="outline"
            onClick={onResetPath}
            className="h-auto min-h-11 w-full px-4 py-3"
          >
            <RefreshCw className="mr-2 h-4 w-4" />
            Explore another area
          </Button>
        )}
      </div>
    </section>
  )
}
