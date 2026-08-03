import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import ErrorBoundary from '@/components/ErrorBoundary'

function BrokenView() {
  throw new Error('internal implementation detail')
}

describe('ErrorBoundary', () => {
  it('shows a safe accessible recovery message', () => {
    render(
      <ErrorBoundary>
        <BrokenView />
      </ErrorBoundary>
    )

    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByText(/please refresh the page/i)).toBeInTheDocument()
    expect(screen.queryByText(/internal implementation detail/i)).not.toBeInTheDocument()
  })
})
