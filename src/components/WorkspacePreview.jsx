import React from 'react'
import { Send } from 'lucide-react'

const WorkspacePreview = () => (
  <div className="flex justify-center bg-[#f6f1eb] px-2 py-8">
    <div className="relative w-full max-w-4xl">
      <div className="absolute inset-x-6 top-4 bottom-[-14px] rounded-[36px] bg-[#d6c9bb]" />

      <div className="relative overflow-visible rounded-[34px] bg-[#f4efe9] p-2.5 shadow-[0_16px_40px_rgba(44,34,24,0.12)]">
        <div className="rounded-[24px] bg-[#6b3a0a] px-6 py-4 text-[#f8f1e8]">
          <div className="flex items-center justify-between">
            <h2 className="text-[24px] font-medium tracking-[-0.01em]">AI Assistant</h2>
            <span className="text-sm font-medium underline underline-offset-4">Case Workspace</span>
          </div>
        </div>

        <div className="mt-2.5 grid grid-cols-1 gap-3 lg:grid-cols-[1.05fr_0.95fr]">
          <div className="rounded-[28px] bg-[#e9e3db] p-4 shadow-[inset_0_1px_0_rgba(255,255,255,0.3)] md:p-5">
            <div className="inline-flex rounded-full bg-[#c89d78] px-5 py-2 text-sm font-semibold text-[#2c2218] shadow-[0_8px_16px_rgba(44,34,24,0.08)]">
              Supply Chain Management Analysis
            </div>

            <p className="mt-5 text-[13px] leading-[1.25] text-[#2c2218]">
              The case explores how fragmented communication across teams slowed decisions, created
              uncertainty in staffing, and weakened coordination across the broader supply chain.
              Updates were passed through disconnected channels, ownership was often unclear, and
              delays built up because critical information was not always visible when teams needed
              it most. Over time, this made routine planning harder and reduced confidence in
              day-to-day operational decisions.
            </p>

            <div className="mt-5 rounded-[24px] bg-[#d1a078] p-4 shadow-[0_10px_20px_rgba(44,34,24,0.08)]">
              <div className="flex gap-4">
                <div className="w-1 shrink-0 rounded-full bg-[#6d2b1f]" />
                <div className="flex-1">
                  <p className="text-[13px] leading-[1.2] text-[#2c2218]">
                    The analysis shows that improving visibility across teams can reduce delays,
                    clarify ownership, and create a more consistent workflow across the supply
                    chain. When updates are easier to track and handoffs are supported by shared
                    context, teams spend less time resolving confusion and more time acting on the
                    next best step.
                  </p>
                </div>
              </div>
            </div>

            <p className="mt-5 text-[13px] leading-[1.25] text-[#2c2218]">
              By centralizing updates, surfacing patterns, and highlighting the most relevant
              signals, the workspace helps users move from raw information to clearer decisions with
              less friction. Instead of searching across disconnected notes and scattered reports,
              users can focus on the evidence that matters, understand where blockers are emerging,
              and make decisions with stronger context and better timing.
            </p>

            <div className="mt-20 text-center text-[22px] font-semibold tracking-[-0.03em] text-[#6b3a0a] md:text-[26px]">
              Page 1/42
            </div>
          </div>

          <div className="relative rounded-[28px] bg-[#e9e3db] p-4 shadow-[inset_0_1px_0_rgba(255,255,255,0.3)] md:p-5">
            <div className="absolute right-0 top-[-8px] w-[240px] rounded-[20px] bg-[#d97a1c] px-5 py-4 text-white shadow-[0_10px_0px_rgba(92,48,7,0.6)]">
              <p className="text-[11px] font-semibold uppercase tracking-[0.15em] text-white/90">
                Completion Score
              </p>
              <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-white/30">
                <div className="h-full w-[65%] bg-[#6b3a0a]" />
              </div>
              <p className="mt-3 text-sm text-white/90">
                <span className="mr-1 text-[26px] font-semibold text-white">72%</span>
                Keep Going!!
              </p>
            </div>

            <div className="pt-14">
              <div className="flex items-start gap-3">
                <span className="mt-1 inline-flex h-12 w-12 shrink-0 rounded-[12px] bg-[#e58a2a]" />
                <p className="max-w-[250px] text-[13px] leading-[1.18] text-[#2c2218]">
                  AI identifies communication gaps as one of the main causes of workflow delays in
                  the case. Repeated handoff issues, inconsistent updates, and missing context
                  appear across multiple sections, suggesting that the problem is structural rather
                  than isolated to a single team.
                </p>
              </div>

              <div className="mt-4 ml-[60px] h-[82px] rounded-[20px] bg-[#d8d0c7]" />

              <div className="mt-6 flex items-start gap-3">
                <span className="mt-1 inline-flex h-12 w-12 shrink-0 rounded-[12px] bg-[#e58a2a]" />
                <p className="max-w-[250px] text-[13px] leading-[1.18] text-[#2c2218]">
                  The workspace highlights the sections that matter most, making it easier to
                  connect evidence with decisions. Instead of reviewing every document line by line,
                  users can focus on the passages that explain what changed, where the delay began,
                  and what action can improve the process next.
                </p>
              </div>

              <div className="mt-5 ml-[60px] h-[82px] rounded-[20px] bg-[#d8d0c7]" />

              <div className="mt-6 flex items-center gap-3 rounded-full bg-[#f6f1eb] p-1.5 shadow-[inset_0_1px_0_rgba(255,255,255,0.7)]">
                <input
                  type="text"
                  readOnly
                  value="Ask a question"
                  className="flex-1 bg-transparent px-4 py-2 text-sm text-[#b89d84] outline-none"
                />
                <button
                  type="button"
                  className="flex h-8 w-8 items-center justify-center rounded-[10px] bg-[#e58a2a] text-white"
                  aria-label="Send message"
                >
                  <Send className="h-4 w-4" />
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
)

export default WorkspacePreview
