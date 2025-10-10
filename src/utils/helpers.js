// Example utility functions for your AI Case Learning Assistant
export const formatCaseTitle = title => {
  return title.trim().replace(/\s+/g, ' ')
}

export const validateEmail = email => {
  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
  return emailRegex.test(email)
}

export const calculateStudyProgress = (completedCases, totalCases) => {
  if (totalCases === 0) return 0
  return Math.round((completedCases / totalCases) * 100)
}

export const formatDate = date => {
  return new Date(date).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  })
}
