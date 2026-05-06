import { MessageSquare, Lightbulb, FileText } from 'lucide-react'

export function HowItWorks() {
  return (
    <section className="bg-[#f3eee7] py-12">
      <div className="mx-auto max-w-[1120px] px-4">
        <div className="mx-auto mb-10 w-full max-w-[1100px] rounded-[44px] rounded-tr-[72px] rounded-br-[72px] rounded-bl-none border border-[#e6d9c9] bg-[#E2D9CE] px-6 py-6 md:px-10">
          <div className="text-center">
            <h2 className="text-[30px] font-medium leading-tight text-[#2C2218] md:text-[36px]">
              How It <span className="font-black italic text-[#C96A08]">WORKS</span>
            </h2>

            <p className="mx-auto mt-4 max-w-[760px] text-[13px] leading-[1.45] text-[#C96A08] md:text-[14px]">
              Upload a case, capture key notes, and receive grounded AI insights that help students
              understand complex documents faster and with better structure.
            </p>
          </div>
        </div>

        <div className="mt-16 flex flex-col items-center gap-10 md:flex-row md:items-start md:justify-start md:mx-auto md:max-w-[1100px] md:px-6 md:-ml-4">
          {/* Step 1 */}
          <div className="w-[320px] shrink-0 md:-ml-2">
            <div className="relative mb-4 w-full rounded-[34px] rounded-tl-none rounded-tr-none rounded-br-[60px] border border-[#e6d9c9] border-t-0 bg-[#E2D9CE] px-4 pt-12 pb-0 md:px-6 md:pt-14 min-h-[280px] -mt-12 md:-mt-16 md:mx-0">
              <div className="pointer-events-none absolute -right-5 top-6 h-10 w-10 translate-x-1/2 rounded-bl-[120px] bg-[#f3eee7]" />
              <div className="relative h-[190px] w-full rounded-[24px] bg-white p-4 shadow-[0_8px_18px_rgba(44,34,24,0.06)]">
                <div className="absolute -left-3 -top-3 flex h-11 w-11 items-center justify-center rounded-full bg-[#E58A2A] text-[18px] font-bold text-white">
                  1
                </div>

                <div className="flex h-full flex-col items-center justify-center rounded-[18px] border border-dashed border-[#b8aea2] px-6 py-6 text-center">
                  <div className="flex h-14 w-14 items-center justify-center rounded-full bg-[#E58A2A] shadow-sm">
                    <FileText className="h-7 w-7 text-white" />
                  </div>

                  <p className="mt-4 text-[15px] font-medium leading-tight text-[#b06717]">
                    Upload Your PDF Case
                  </p>
                  <p className="text-[15px] leading-tight text-[#b06717]">
                    or browse from your files
                  </p>

                  <p className="mt-4 text-[12px] text-[#b7ab9d]">PDF only · up to 50MB</p>
                </div>
              </div>
            </div>

            <div className="mt-6 text-center">
              <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-[#efe7db]">
                <FileText className="h-5 w-5 text-[#C96A08]" />
              </div>

              <h3 className="mx-auto mt-5 max-w-[290px] text-[24px] font-semibold leading-[1.2] text-[#2C2218]">
                Upload or Receive a Case
              </h3>

              <p className="mx-auto mt-4 max-w-[315px] text-[15px] leading-[1.45] text-[#5C4C3C]">
                Start with a case you upload yourself or one assigned by an instructor in your class
                workspace.
              </p>
            </div>
          </div>

          {/* Step 2 */}
          <div className="w-[320px] shrink-0">
            <div className="relative h-[190px] w-[320px] rounded-[24px] bg-white p-4 shadow-[0_8px_18px_rgba(44,34,24,0.06)]">
              <div className="absolute -left-3 -top-3 flex h-11 w-11 items-center justify-center rounded-full bg-[#E58A2A] text-[18px] font-bold text-white">
                2
              </div>

              <div className="h-full rounded-[18px] border border-dashed border-[#b8aea2] px-4 py-4">
                <div className="flex h-full gap-3">
                  <div className="w-[43%] rounded-[12px] bg-[#e7ddd0] p-3">
                    <div className="text-[11px] font-semibold text-[#a65f17]">Case Notes</div>
                    <p className="mt-2 text-[7px] leading-[1.3] text-[#2C2218]">
                      The student highlights the main issue, identifies the evidence, and records
                      the most important facts for review.
                    </p>
                  </div>

                  <div className="flex flex-1 flex-col">
                    <div className="text-center text-[12px] font-semibold text-[#b06717]">
                      NOTES
                    </div>

                    <div className="mt-2 flex flex-col gap-2">
                      <div className="rounded-[12px] bg-[#d8cfc3] px-3 py-2 text-[8px] leading-[1.2] text-[#5C4C3C]">
                        Staff shortages increased patient wait times during peak hours.
                      </div>
                      <div className="rounded-[12px] bg-[#d8cfc3] px-3 py-2 text-[8px] leading-[1.2] text-[#5C4C3C]">
                        Missing logs made it harder to track bed availability accurately.
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div className="mt-27 text-center">
              <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-[#efe7db]">
                <MessageSquare className="h-5 w-5 text-[#C96A08]" />
              </div>

              <h3 className="mx-auto mt-5 max-w-[290px] text-[24px] font-semibold text-[#2C2218]">
                Take Notes & Highlight
              </h3>

              <p className="mx-auto mt-4 max-w-[315px] text-[15px] leading-[1.45] text-[#5C4C3C]">
                Save key findings and organize important observations so the most useful parts of
                the case are easy to revisit.
              </p>
            </div>
          </div>

          {/* Step 3 */}
          <div className="w-[320px] shrink-0">
            <div className="relative h-[190px] w-[320px] rounded-[24px] bg-white p-4 shadow-[0_8px_18px_rgba(44,34,24,0.06)]">
              <div className="absolute -left-3 -top-3 flex h-11 w-11 items-center justify-center rounded-full bg-[#E58A2A] text-[18px] font-bold text-white">
                3
              </div>

              <div className="h-full rounded-[18px] border border-dashed border-[#b8aea2] px-4 py-4">
                <div className="flex h-full gap-3">
                  <div className="w-[43%] rounded-[12px] bg-[#e7ddd0] p-3">
                    <div className="text-[11px] font-semibold text-[#2C2218]">Analysis</div>
                    <p className="mt-2 text-[7px] leading-[1.35] text-[#2C2218]">
                      The case points to operational delays caused by poor communication and weak
                      process visibility.
                    </p>
                  </div>

                  <div className="flex-1 rounded-[12px] bg-white p-3">
                    <div className="flex gap-2">
                      <div className="mt-1 h-3 w-[3px] rounded-full bg-[#C96A08]" />
                      <p className="text-[7px] leading-[1.35] text-[#2C2218]">
                        AI can surface the strongest evidence, connect it to the student’s notes,
                        and explain the likely causes behind the issue.
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div className="mt-27 text-center">
              <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-[#efe7db]">
                <Lightbulb className="h-5 w-5 text-[#C96A08]" />
              </div>

              <h3 className="mx-auto mt-5 max-w-[290px] text-[24px] font-semibold text-[#2C2218]">
                Get AI Insights
              </h3>

              <p className="mx-auto mt-4 max-w-[315px] text-[15px] leading-[1.45] text-[#5C4C3C]">
                Ask questions and receive grounded answers that help students understand the case
                clearly and move from reading to analysis.
              </p>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
