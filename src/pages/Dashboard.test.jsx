import { describe, expect, it } from 'vitest'
import { normalizeCaseList } from './Dashboard'

describe('normalizeCaseList', () => {
  it('accepts wrapped arrays and normalizes names and ids', () => {
    const output = normalizeCaseList({
      items: [
        {
          uploadId: 'abc-123',
          originalFileName: 'Sample Case.pdf',
          createdAt: '2024-01-02T00:00:00Z',
        },
      ],
    })

    expect(output).toHaveLength(1)
    expect(output[0]).toMatchObject({
      id: 'abc-123',
      uploadId: 'abc-123',
      title: 'Sample Case',
      fileName: 'Sample Case.pdf',
      createdAt: '2024-01-02T00:00:00Z',
    })
  })

  it('returns an empty array for non-array payloads', () => {
    expect(normalizeCaseList({ error: 'not found' })).toEqual([])
    expect(normalizeCaseList(null)).toEqual([])
    expect(normalizeCaseList(undefined)).toEqual([])
  })
})
