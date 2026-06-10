import React from 'react'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { describe, it, expect, beforeEach, vi } from 'vitest'
import CasesGrid from '@/components/dashboard/CasesGrid'

// Mock react-router-dom navigate and Link
const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    Link: ({ children }) => children,
    useNavigate: () => mockNavigate,
  }
})

// Mock api functions used by CasesGrid
const mockListSessions = vi.fn()
const mockCreateSession = vi.fn()
vi.mock('@/lib/api', () => ({
  listSessionsMine: (...args) => mockListSessions(...args),
  createSession: (...args) => mockCreateSession(...args),
  renameCase: vi.fn(),
  deleteCase: vi.fn(),
}))

describe('CasesGrid', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('resumes most recent session when one exists', async () => {
    mockListSessions.mockResolvedValue([
      { sessionId: 's1', uploadId: '1', createdAt: '2023-01-01T00:00:00Z' },
    ])

    render(<CasesGrid items={[{ id: '1', title: 'Test Case', createdAt: '2023-01-01' }]} />)

    // Click the case card body to open the most recent workspace.
    fireEvent.click(screen.getAllByRole('button')[0])

    await waitFor(() => {
      expect(mockListSessions).toHaveBeenCalled()
      expect(mockNavigate).toHaveBeenCalled()
      // Expect navigation to include sessionId
      const navArg = mockNavigate.mock.calls[0][0]
      expect(navArg).toContain('/workspace/1')
      expect(navArg).toMatch(/sessionId=/)
    })
  })

  it('creates new session when New workspace clicked', async () => {
    mockCreateSession.mockResolvedValue({ sessionId: 'new-s' })

    render(<CasesGrid items={[{ id: '2', title: 'New Case' }]} />)

    // Open menu and click New workspace
    const menuBtn = screen.getByLabelText('Open actions')
    fireEvent.click(menuBtn)

    const newBtn = await screen.findByText('New workspace')
    fireEvent.click(newBtn)

    await waitFor(() => {
      expect(mockCreateSession).toHaveBeenCalledWith('2')
      expect(mockNavigate).toHaveBeenCalled()
    })
  })
})
