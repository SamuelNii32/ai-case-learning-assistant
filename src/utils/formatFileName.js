// Simple filename/case title formatter — trims, collapses whitespace and
// optionally truncates long names with an ellipsis.
export default function formatFileName(name, maxLength = 60) {
  if (!name && name !== 0) return ''
  const s = String(name).trim().replace(/\s+/g, ' ')
  if (maxLength && s.length > maxLength) {
    return s.slice(0, Math.max(0, maxLength - 1)).trimEnd() + '…'
  }
  return s
}
