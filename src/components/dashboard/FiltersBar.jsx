import { Link } from 'react-router-dom'

export default function FiltersBar({ active, onChange }) {
  const base =
    'px-4 py-2 rounded-full border text-sm focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2'
  const on = 'bg-blue-600 text-white border-blue-700'
  const off = 'bg-white text-slate-700 border-slate-300 hover:bg-slate-100'

  return (
    <div className="flex items-center gap-3">
      <button
        type="button"
        className={`${base} ${active === 'all' ? on : off}`}
        aria-pressed={active === 'all'}
        onClick={() => onChange?.('all')}
      >
        All
      </button>
      <button
        type="button"
        className={`${base} ${active === 'in-progress' ? on : off}`}
        aria-pressed={active === 'in-progress'}
        onClick={() => onChange?.('in-progress')}
      >
        In&nbsp;Progress
      </button>
      <button
        type="button"
        className={`${base} ${active === 'completed' ? on : off}`}
        aria-pressed={active === 'completed'}
        onClick={() => onChange?.('completed')}
      >
        Completed
      </button>
    </div>
  )
}
