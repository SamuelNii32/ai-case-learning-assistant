export default function SortControl({ dir = 'desc', onToggle }) {
  const label = dir === 'desc' ? 'Sort: Newest' : 'Sort: Oldest'
  const ariaLabel = dir === 'desc' ? 'Sort by date: Newest first' : 'Sort by date: Oldest first'

  return (
    <div className="flex items-center justify-end">
      <button
        type="button"
        onClick={() => onToggle?.(dir === 'desc' ? 'asc' : 'desc')}
        aria-label={ariaLabel}
        className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-slate-300 bg-white text-slate-700 hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2 text-sm"
      >
        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 16V4m0 0L3 8m4-4l4 4m6 0v12m0 0l4-4m-4 4l-4-4" />
        </svg>
        {label}
      </button>
    </div>
  )
}
