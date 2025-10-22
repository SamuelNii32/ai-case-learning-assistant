import { Route, Highlighter, History, Shield } from 'lucide-react'

export function ValueProps() {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-6 pt-8">
      <div className="flex items-start gap-3">
        <Route className="w-5 h-5 text-[#125691] flex-shrink-0 mt-0.5" />
        <div>
          <div className="text-sm font-medium text-slate-900">Guided case walkthrough</div>
          <div className="text-xs text-slate-600">
            Problem → Evidence → Analysis → Recommendation → Reflection
          </div>
        </div>
      </div>
      <div className="flex items-start gap-3">
        <Highlighter className="w-5 h-5 text-[#125691] flex-shrink-0 mt-0.5" />
        <div>
          <div className="text-sm font-medium text-slate-900">Dynamic highlighting</div>
          <div className="text-xs text-slate-600">
            Answers link to exact lines, charts, or figures
          </div>
        </div>
      </div>
      <div className="flex items-start gap-3">
        <History className="w-5 h-5 text-[#125691] flex-shrink-0 mt-0.5" />
        <div>
          <div className="text-sm font-medium text-slate-900">Session history & notes</div>
          <div className="text-xs text-slate-600">
            Resume where you left off, keep structured notes
          </div>
        </div>
      </div>
      <div className="flex items-start gap-3">
        <Shield className="w-5 h-5 text-[#125691] flex-shrink-0 mt-0.5" />
        <div>
          <div className="text-sm font-medium text-slate-900">Private by design</div>
          <div className="text-xs text-slate-600">Your documents stay in your control</div>
        </div>
      </div>
    </div>
  )
}
