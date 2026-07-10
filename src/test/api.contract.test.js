import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  assignCaseToClass,
  getClassReadingCoachSummary,
  getPagedItems,
} from '@/lib/api'

describe('API contract helpers', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
  })

  it('sends readingCoachQuestions when assigning a case to a class', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ assigned: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    )
    vi.stubGlobal('fetch', fetchMock)

    await assignCaseToClass('class-123', {
      uploadId: 'upload-456',
      readingCoachQuestions: 'Explain the central problem before recommending an action.',
    })

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/classes/class-123/cases'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          uploadId: 'upload-456',
          readingCoachQuestions:
            'Explain the central problem before recommending an action.',
        }),
      })
    )
  })

  it('requests the class Reading Coach summary endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          summary: {
            assignedStudents: 12,
            assignedCases: 3,
            startedStudents: 8,
          },
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      )
    )
    vi.stubGlobal('fetch', fetchMock)

    const summary = await getClassReadingCoachSummary('class-123')

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/admin/classes/class-123/tutor-progress/summary'),
      expect.objectContaining({ method: 'GET' })
    )
    expect(summary).toEqual({
      assignedStudents: 12,
      assignedCases: 3,
      startedStudents: 8,
    })
  })

  it('unwraps paged envelopes used by session and note views', () => {
    expect(getPagedItems([{ id: 1 }])).toEqual([{ id: 1 }])
    expect(getPagedItems({ items: [{ id: 2 }], totalCount: 1 })).toEqual([{ id: 2 }])
    expect(getPagedItems({ data: { items: [{ id: 3 }] } })).toEqual([{ id: 3 }])
    expect(getPagedItems({ sessions: [{ id: 4 }] })).toEqual([{ id: 4 }])
  })
})
