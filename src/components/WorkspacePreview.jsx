import React from 'react'
import { Send } from 'lucide-react'

const WorkspacePreview = () => (
  <div className="flex justify-center bg-[#f3eee7] px-3 py-6 md:px-6">
    <div className="relative w-full max-w-[800px]">
      <div className="absolute inset-x-6 top-6 bottom-[-26px] rounded-[30px] bg-[#d8ccbc]" />
      <div className="absolute inset-x-3 top-3 bottom-[-12px] rounded-[30px] bg-[#e8dfd3]" />

      <div className="relative overflow-visible rounded-[28px] bg-[#f7f3ee] p-3 shadow-[0_14px_30px_rgba(44,34,24,0.07)]">
        <div className="rounded-[24px] bg-[#6f3904] px-6 py-4 text-[#f7efe6]">
          <div className="flex items-center justify-between gap-4">
            <h2 className="text-[24px] font-medium tracking-[-0.03em]">AI Assistant</h2>
            <span className="text-[15px] font-medium underline underline-offset-4">
              Case Workspace
            </span>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-[1.04fr_0.86fr]">
          <div className="rounded-[24px] bg-[#e8e2da] px-5 pb-6 pt-5 shadow-[inset_0_1px_0_rgba(255,255,255,0.35)]">
            <div className="ml-6 inline-flex max-w-[320px] rounded-[14px] bg-[#d8ccbf] px-4 py-3 text-[14px] font-semibold leading-snug text-[#6b3a06] shadow-[0_4px_10px_rgba(44,34,24,0.04)]">
              Supply Chain Management Analysis
            </div>

            <p className="mt-5 max-w-[380px] text-[12px] leading-[1.45] text-[#33261d]">
              The case explores how fragmented communication across teams slowed decisions, created
              uncertainty in staffing, and weakened coordination across the broader supply chain.
              Updates were passed through disconnected channels, ownership was often unclear, and
              delays built up because critical information was not always visible when teams needed
              it most.
            </p>

            <div className="mt-5 max-w-[390px] rounded-[20px] bg-[#d5a57f] px-4 py-4 shadow-[0_8px_14px_rgba(44,34,24,0.04)]">
              <div className="flex gap-4">
                <div className="w-[3px] shrink-0 rounded-full bg-[#6c2a1c]" />
                <div className="flex-1">
                  <p className="text-[12px] leading-[1.42] text-[#33261d]">
                    The analysis shows that improving visibility across teams can reduce delays,
                    clarify ownership, and create a more consistent workflow across the supply
                    chain.
                  </p>
                </div>
              </div>
            </div>

            <p className="mt-5 max-w-[390px] text-[12px] leading-[1.45] text-[#33261d]">
              By centralizing updates, surfacing patterns, and highlighting the most relevant
              signals, the workspace helps users move from raw information to clearer decisions with
              less friction.
            </p>

            <div className="mt-10 text-center text-[20px] font-semibold tracking-[-0.03em] text-[#6b3a06]">
              Page 1/42
            </div>
          </div>

          <div className="relative overflow-visible rounded-[24px] bg-[#e8e2da] px-5 pb-6 pt-5 shadow-[inset_0_1px_0_rgba(255,255,255,0.35)]">
            <div className="absolute right-[-26px] top-[-10px] z-20">
              <div className="absolute inset-x-0 bottom-[-9px] h-full translate-x-[8px] rounded-[18px] rounded-bl-none rounded-tr-none bg-[#9b5a14]" />
              <div className="relative w-[260px] rounded-[18px] rounded-bl-none rounded-tr-none bg-[#e68b29] px-5 py-4 text-white">
                <p className="text-center text-[10px] font-semibold uppercase tracking-[0.16em] text-white/95 underline decoration-white/80 underline-offset-4">
                  Completion Score
                </p>

                <div className="mt-3 h-[6px] w-full overflow-hidden rounded-full bg-[#efb06b]">
                  <div className="h-full w-[66%] bg-[#75400b]" />
                </div>

                <div className="mt-3 text-center text-[12px] font-medium text-white">
                  <span className="mr-1 font-semibold">72%</span>
                  <span>Keep Going!!</span>
                </div>
              </div>
            </div>

            <div className="pt-12">
              <div className="flex items-start gap-3">
                <span className="mt-1 inline-flex h-[40px] w-[40px] shrink-0 rounded-[10px] bg-[#e68b29]" />
                <p className="max-w-[220px] text-[12px] leading-[1.4] text-[#33261d]">
                  AI identifies communication gaps as one of the main causes of workflow delays in
                  the case.
                </p>
              </div>

              <div className="ml-[52px] mt-4 h-[58px] rounded-[18px] bg-[#d9d0c6]" />

              <div className="mt-6 flex items-start gap-3">
                <span className="mt-1 inline-flex h-[40px] w-[40px] shrink-0 rounded-[10px] bg-[#e68b29]" />
                <p className="max-w-[220px] text-[12px] leading-[1.4] text-[#33261d]">
                  The workspace highlights the sections that matter most, making it easier to
                  connect evidence with decisions.
                </p>
              </div>

              <div className="ml-[52px] mt-4 h-[58px] rounded-[18px] bg-[#d9d0c6]" />

              <div className="mt-8 flex items-center gap-3 rounded-[18px] bg-[#f7f3ee] px-3 py-2.5 shadow-[inset_0_1px_0_rgba(255,255,255,0.85)]">
                <input
                  type="text"
                  readOnly
                  value="Ask a question"
                  className="flex-1 bg-transparent text-[12px] text-[#b69a81] outline-none"
                />
                <button
                  type="button"
                  className="flex h-8 w-8 items-center justify-center rounded-[10px] bg-[#e68b29] text-white"
                  aria-label="Send message"
                >
                  <Send className="h-3.5 w-3.5" />
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
