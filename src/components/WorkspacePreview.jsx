import React from 'react'
import { Send } from 'lucide-react'

const WorkspacePreview = () => (
  <div className="bg-[#faf6f0] py-12 px-4 flex justify-center">
    <div className="relative w-full max-w-5xl">
      <div className="absolute right-8 -top-6 bg-[#F6EEE5] border border-[#E4C6A1] text-sm font-semibold text-[#6A3A0A] px-4 py-2 rounded-2xl shadow-sm">
        Completion Score: 80%
      </div>
      <div className="rounded-[28px] bg-white shadow-[0_20px_50px_rgba(32,28,24,0.15)] border border-[#F2E7D6] overflow-hidden">
        <div className="grid grid-cols-1 lg:grid-cols-[0.55fr_0.45fr] gap-6 p-6">
          <div className="bg-white rounded-2xl border border-[#E8DDD0] p-6 space-y-4 shadow-sm">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-[#6A3A0A]">Case Study</p>
            <h3 className="text-2xl font-semibold text-[#2C2218]">Healthcare Innovation Case</h3>
            <div className="space-y-2">
              {[1, 2, 3, 4].map(line => (
                <div key={line} className="h-2.5 bg-[#f6f0e8] rounded-full w-full" />
              ))}
            </div>
            <p className="text-sm text-[#5C4C3C] leading-relaxed">
              The team addressed broken communications that kept staffing levels ambiguous and
              patient handoffs slow.
            </p>
            <div className="space-y-1">
              {[1, 2].map(line => (
                <div key={line} className="h-2 bg-[#faefe1] rounded-full w-3/4" />
              ))}
            </div>
          </div>

          <div className="rounded-2xl bg-white border border-[#E8DDD0] p-6 space-y-4 shadow-sm flex flex-col">
            <div className="flex items-center justify-between">
              <h3 className="text-lg font-semibold text-[#2C2218]">AI Assistant</h3>
              <span className="text-xs font-semibold text-[#C96A08]">New</span>
            </div>
            <div className="space-y-3 flex-1">
              {[
                {
                  text: 'Operating room delays dropped to 18 minutes after workflow changes.',
                  cite: 'p:1',
                },
                {
                  text: 'Patient satisfaction improved by 12% once real-time dashboards launched.',
                  cite: 'p:2',
                },
                {
                  text: 'AI recommends focusing on triage automation next quarter.',
                  cite: 'p:3',
                },
              ].map((msg, idx) => (
                <div key={idx} className="rounded-2xl bg-white border border-[#F2E7D6] p-4 shadow-[0_10px_25px_rgba(32,28,24,0.08)]">
                  <p className="text-sm text-[#2C2218] leading-relaxed">{msg.text}</p>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs text-[#6A3A0A] font-semibold">
                    <span className="px-2 py-0.5 rounded-full bg-[#F6EEE5] border border-[#E4C6A1]">{msg.cite}</span>
                  </div>
                </div>
              ))}
            </div>
            <div className="mt-4 border-t border-[#E8DDD0] pt-4 flex items-center gap-3 bg-white">
              <input
                type="text"
                readOnly
                value="Ask about the case..."
                className="flex-1 bg-[#fffdf9] border border-[#f2e7d6] rounded-full px-4 py-2 text-sm text-[#5C4C3C] focus:outline-none"
              />
              <button
                type="button"
                className="h-10 w-10 rounded-full bg-[#C96A08] text-white flex items-center justify-center shadow-sm"
                aria-label="Send message"
              >
                <Send className="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
)

export default WorkspacePreview
