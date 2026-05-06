const ACADEMIC_TARGETS = new Set([
  'findings',
  'methodology',
  'background',
  'concepts',
  'implications',
  'limitations',
])

const BUSINESS_TARGETS = new Set([
  'problem',
  'stakeholders',
  'alternatives',
  'financials',
  'risks',
  'recommendation',
])

const ACADEMIC_FOCUS_CARDS = {
  findings: {
    title: 'Key Findings',
    description: 'Read the paper’s core results first and see what the document is actually claiming.',
    stepCount: 5,
    duration: '~6 min',
  },
  methodology: {
    title: 'Methodology',
    description: 'Trace how the evidence was gathered, structured, and tested.',
    stepCount: 6,
    duration: '~8 min',
  },
  background: {
    title: 'Theory / Background',
    description: 'Understand the framing, prior work, and problem context behind the study.',
    stepCount: 4,
    duration: '~5 min',
  },
  concepts: {
    title: 'Concepts',
    description: 'Review the key terms and ideas carrying the argument forward.',
    stepCount: 4,
    duration: '~5 min',
  },
  implications: {
    title: 'Implications',
    description: 'See how the findings extend beyond the page and what they suggest next.',
    stepCount: 4,
    duration: '~5 min',
  },
  limitations: {
    title: 'Limitations',
    description: 'Identify the boundaries, assumptions, and constraints of the analysis.',
    stepCount: 3,
    duration: '~4 min',
  },
}

const BUSINESS_FOCUS_CARDS = {
  problem: {
    title: 'Main Problem',
    description: 'Start with the business challenge that made this case worth solving.',
    stepCount: 5,
    duration: '~6 min',
  },
  stakeholders: {
    title: 'Stakeholders',
    description: 'Map the people, teams, and decision-makers affected by the case.',
    stepCount: 4,
    duration: '~5 min',
  },
  alternatives: {
    title: 'Alternatives',
    description: 'Compare the main options and the trade-offs behind each path.',
    stepCount: 5,
    duration: '~6 min',
  },
  financials: {
    title: 'Financial Analysis',
    description: 'Review the numbers, assumptions, and economics shaping the decision.',
    stepCount: 6,
    duration: '~8 min',
  },
  risks: {
    title: 'Risks',
    description: 'Look for the operational, strategic, and implementation risks.',
    stepCount: 4,
    duration: '~5 min',
  },
  recommendation: {
    title: 'Recommendation',
    description: 'Finish with the decision, rationale, and what should happen next.',
    stepCount: 4,
    duration: '~5 min',
  },
}

function cleanLines(value) {
  return String(value || '')
    .split('\n')
    .map(line => line.trim())
    .filter(Boolean)
}

function getFirstLine(value) {
  return cleanLines(value)[0] || ''
}

function getSecondLine(value) {
  return cleanLines(value)[1] || ''
}

export function getChoiceFocusMeta(choice = {}) {
  const target = String(choice.target || '').toLowerCase()
  const useBusiness = BUSINESS_TARGETS.has(target)
  const useAcademic = ACADEMIC_TARGETS.has(target) || !useBusiness
  const catalog = useBusiness ? BUSINESS_FOCUS_CARDS : ACADEMIC_FOCUS_CARDS
  const fallbackTitle = getFirstLine(choice.label) || 'Focus area'
  const fallbackDescription = getSecondLine(choice.label) || ''
  const card = catalog[target] || {}

  return {
    title: card.title || fallbackTitle,
    description: card.description || fallbackDescription,
    stepCount: card.stepCount,
    duration: card.duration,
    isBusiness: useBusiness,
    isAcademic: useAcademic,
  }
}
