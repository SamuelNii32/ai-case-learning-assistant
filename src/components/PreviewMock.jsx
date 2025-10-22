import { MessageSquare, FileText } from 'lucide-react'

export function PreviewMock() {
  return (
    <div className="relative">
      <div className="bg-white rounded-xl shadow-2xl border border-slate-200 overflow-hidden">
        {/* Browser chrome */}
        <div className="bg-slate-50 border-b border-slate-200 px-4 py-3 flex items-center gap-2">
          <div className="flex items-center gap-1.5">
            <div className="w-3 h-3 rounded-full bg-red-500"></div>
            <div className="w-3 h-3 rounded-full bg-yellow-500"></div>
            <div className="w-3 h-3 rounded-full bg-green-500"></div>
          </div>
          <div className="flex-1 text-right">
            <span className="text-xs text-slate-500">Case Workspace</span>
          </div>
        </div>

        <div className="flex h-[450px]">
          {/* PDF Viewer Mock - Left Side */}
          <div className="w-1/2 bg-slate-100 p-4 border-r border-slate-200 overflow-hidden">
            <div className="bg-white rounded shadow-sm p-4 h-full space-y-3 text-xs">
              <div className="font-semibold text-slate-900">Supply Chain Disruption Analysis</div>
              <div className="space-y-2 text-slate-600">
                <p className="leading-relaxed">
                  The global supply chain has experienced unprecedented disruptions due to various
                  factors including pandemic-related shutdowns, geopolitical tensions, and natural
                  disasters.
                </p>
                <p className="leading-relaxed bg-slate-50 p-2 rounded border-l-2 border-[#125691]">
                  Key suppliers in Southeast Asia reported a 40% reduction in production capacity
                  during Q2 2023, leading to significant delays in component delivery.
                </p>
                <p className="leading-relaxed">
                  Companies must develop robust mitigation strategies to ensure business continuity
                  and maintain customer satisfaction during these challenging times.
                </p>
              </div>
              <div className="text-xs text-slate-400 pt-2">Page 3 of 24</div>
            </div>
          </div>

          {/* Chat Interface Mock - Right Side */}
          <div className="w-1/2 bg-white flex flex-col">
            <div className="flex-1 p-4 space-y-4 overflow-hidden">
              {/* AI Message 1 */}
              <div className="flex items-start gap-2">
                <div className="w-8 h-8 rounded-full bg-[#125691] flex-shrink-0 flex items-center justify-center">
                  <MessageSquare className="w-4 h-4 text-white" />
                </div>
                <div className="flex-1 space-y-1">
                  <div className="text-xs text-slate-900 leading-relaxed">
                    Based on the case, the primary challenge is the 40% reduction in production
                    capacity from Southeast Asian suppliers.
                  </div>
                  <div className="inline-flex items-center gap-1 px-2 py-0.5 bg-slate-100 text-[#125691] rounded text-[10px]">
                    <FileText className="w-3 h-3" />
                    Page 3
                  </div>
                </div>
              </div>

              {/* User Message */}
              <div className="flex items-start gap-2 justify-end">
                <div className="bg-slate-100 rounded-lg px-3 py-2 max-w-[80%]">
                  <div className="text-xs text-slate-900">
                    What mitigation strategies should we consider?
                  </div>
                </div>
              </div>

              {/* AI Message 2 */}
              <div className="flex items-start gap-2">
                <div className="w-8 h-8 rounded-full bg-[#125691] flex-shrink-0 flex items-center justify-center">
                  <MessageSquare className="w-4 h-4 text-white" />
                </div>
                <div className="flex-1 space-y-1">
                  <div className="text-xs text-slate-900 leading-relaxed">
                    Consider diversifying your supplier base and developing contingency plans for
                    critical components...
                  </div>
                </div>
              </div>
            </div>

            {/* Input Area */}
            <div className="border-t border-slate-200 p-3">
              <div className="flex items-center gap-2 px-3 py-2 bg-slate-50 rounded-lg border border-slate-200">
                <span className="text-xs text-slate-400 flex-1">Ask a question...</span>
                <div className="w-6 h-6 bg-[#125691] rounded flex items-center justify-center">
                  <MessageSquare className="w-3 h-3 text-white" />
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
