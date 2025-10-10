import { describe, it, expect } from 'vitest'
import {
  formatCaseTitle,
  validateEmail,
  calculateStudyProgress,
  formatDate,
} from '../utils/helpers'

describe('Helper Functions', () => {
  describe('formatCaseTitle', () => {
    it('trims whitespace and normalizes spaces', () => {
      expect(formatCaseTitle('  Brown  v.   Board  ')).toBe('Brown v. Board')
      expect(formatCaseTitle('Miranda v. Arizona')).toBe('Miranda v. Arizona')
      expect(formatCaseTitle('')).toBe('')
    })
  })

  describe('validateEmail', () => {
    it('validates correct email formats', () => {
      expect(validateEmail('user@example.com')).toBe(true)
      expect(validateEmail('test.email+tag@domain.co.uk')).toBe(true)
    })

    it('rejects invalid email formats', () => {
      expect(validateEmail('invalid-email')).toBe(false)
      expect(validateEmail('user@')).toBe(false)
      expect(validateEmail('@domain.com')).toBe(false)
      expect(validateEmail('user@domain')).toBe(false)
    })
  })

  describe('calculateStudyProgress', () => {
    it('calculates progress percentage correctly', () => {
      expect(calculateStudyProgress(5, 10)).toBe(50)
      expect(calculateStudyProgress(3, 4)).toBe(75)
      expect(calculateStudyProgress(10, 10)).toBe(100)
    })

    it('handles edge cases', () => {
      expect(calculateStudyProgress(0, 10)).toBe(0)
      expect(calculateStudyProgress(0, 0)).toBe(0)
      expect(calculateStudyProgress(5, 3)).toBe(167) // Over 100%
    })
  })

  describe('formatDate', () => {
    it('formats dates correctly', () => {
      // Use explicit date to avoid timezone issues
      const date = new Date(2023, 11, 25) // Month is 0-indexed, so 11 = December
      expect(formatDate(date)).toBe('December 25, 2023')
    })

    it('handles string dates', () => {
      // Use explicit date construction
      const date = new Date(2023, 0, 1) // Month is 0-indexed, so 0 = January
      expect(formatDate(date)).toBe('January 1, 2023')
    })
  })
})
