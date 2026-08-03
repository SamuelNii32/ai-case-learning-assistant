import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import ReadingCoachPanel from '@/components/ReadingCoachPanel'

const noop = vi.fn()

describe('ReadingCoachPanel accessibility states', () => {
  it('announces loading state', () => {
    render(<ReadingCoachPanel loading state={null} error={null} onAnswerChange={noop} />)
    expect(screen.getByRole('status')).toHaveTextContent(/loading reading coach/i)
  })

  it('announces errors and exposes retry', () => {
    render(<ReadingCoachPanel error="Unable to load" onRetry={noop} onAnswerChange={noop} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Unable to load')
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })
})
