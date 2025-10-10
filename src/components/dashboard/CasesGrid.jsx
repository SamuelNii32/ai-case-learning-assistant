import { Link } from 'react-router-dom'

function statusClasses(status) {
  if (status === 'completed') return 'bg-emerald-50 text-emerald-700 border border-emerald-200'
  return 'bg-amber-50 text-amber-700 border border-amber-200'
}

function SkeletonCard() {
  return (
    <div className="overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm animate-pulse">
      <div className="aspect-[5/3] bg-slate-100" />
      <div className="p-5 space-y-3">
        <div className="h-5 w-3/4 bg-slate-100 rounded" />
        <div className="h-4 w-full bg-slate-100 rounded" />
        <div className="h-4 w-5/6 bg-slate-100 rounded" />
      </div>
    </div>
  )
}

export default function CasesGrid({ items = [], loading = false }) {
  if (loading) {
    return (
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {[...Array(6)].map((_, i) => (
          <SkeletonCard key={i} />
        ))}
      </div>
    )
  }

  if (!items.length) {
    return (
      <div className="col-span-full rounded-lg border border-slate-200 bg-white p-8 text-center">
        <h3 className="text-slate-900 font-semibold">No cases match your filters</h3>
        <p className="text-slate-600 mt-1">Try clearing search or changing the status filter.</p>
        <div className="mt-4">
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-blue-600 text-white text-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
          >
            Upload a Case
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      {items.map(c => (
        <Link key={c.id} to={`/workspace/${c.id}`} className="block group">
          <div className="overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm hover:shadow-md transition-shadow">
            <div className="relative aspect-[5/3] overflow-hidden">
              <img
                src={c.image || '/placeholder.svg'}
                alt={c.title}
                className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                loading="lazy"
                decoding="async"
                width={1000}
                height={600}
              />

              <span className="absolute top-3 right-3 px-3 py-1 rounded-full text-xs font-medium bg-blue-600 text-white">
                {c.mode}
              </span>
            </div>

            <div className="p-5 space-y-2">
              <h3 className="font-semibold text-lg text-slate-900 group-hover:text-blue-600 transition-colors">
                {c.title}
              </h3>
              <p className="text-sm text-slate-600 leading-relaxed">{c.description}</p>
              <div className="pt-2">
                <span
                  className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-medium ${statusClasses(c.status)}`}
                >
                  {c.status === 'completed' ? 'Completed' : 'In Progress'}
                </span>
              </div>
            </div>
          </div>
        </Link>
      ))}
    </div>
  )
}
