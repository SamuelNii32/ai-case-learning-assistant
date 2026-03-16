export default function SortControl({ dir = 'desc', onToggle }) {
  const label = dir === 'desc' ? 'Sort: Newest' : 'Sort: Oldest'
  const ariaLabel = dir === 'desc' ? 'Sort by date: Newest first' : 'Sort by date: Oldest first'

  return (
    <div className="flex items-center justify-end">
      <button
        type="button"
        onClick={() => onToggle?.(dir === 'desc' ? 'asc' : 'desc')}
        aria-label={ariaLabel}
        className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-[#d6c6b4] bg-white text-[#5C4C3C] hover:bg-[#f5ecde] focus:outline-none focus:ring-2 focus:ring-[#C96A08]/60 focus:ring-offset-2 focus:ring-offset-[#f5ecde] text-sm"
      >
        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 16V4m0 0L3 8m4-4l4 4m6 0v12m0 0l4-4m-4 4l-4-4" />
        </svg>
        {label}
      </button>
    </div>
  )
}
