import { Upload, MessageSquare, Lightbulb, FileText } from 'lucide-react'

export function HowItWorks() {
  return (
    <section className="bg-[#f8f5ef] py-20">
      <div className="container mx-auto px-6">
        <div className="text-center mb-16">
          <h2 className="text-4xl font-bold text-[#2C2218] mb-4">How It Works</h2>
          <p className="text-lg text-[#5C4C3C]">Get started in three structured steps</p>
        </div>

        <div className="grid md:grid-cols-3 gap-12">
          {/* Step 1: Upload */}
          <div className="space-y-6">
            <div className="relative rounded-xl overflow-hidden shadow-lg border border-[#f4e7d8] bg-white">
              <div className="h-60 bg-[#fff6ed] p-6 flex flex-col items-center justify-center">
                <div className="w-full max-w-sm space-y-4">
                  <div className="border-2 border-dashed border-[#e4c6a1] rounded-lg p-8 bg-white/60 flex flex-col items-center gap-3">
                    <div className="w-12 h-12 bg-[#C96A08] rounded-full flex items-center justify-center shadow-sm">
                      <FileText className="w-6 h-6 text-white" />
                    </div>
                    <div className="text-center">
                      <div className="text-sm font-medium text-[#2C2218] flex items-center justify-center gap-1">
                        Drop or receive a case
                      </div>
                      <div className="text-xs text-[#6A3A0A]">PDF upload or instructor assignment</div>
                    </div>
                  </div>
                  <div className="text-xs text-[#6A3A0A] text-center">Supports PDFs up to 50MB with class sync</div>
                </div>
              </div>
              <div className="absolute top-4 left-4 w-10 h-10 bg-[#C96A08] text-white rounded-full flex items-center justify-center font-bold shadow-lg">
                1
              </div>
            </div>
            <div className="text-center space-y-3">
              <div className="w-12 h-12 bg-[#f6efe3] rounded-full flex items-center justify-center mx-auto">
                <FileText className="w-6 h-6 text-[#C96A08]" />
              </div>
              <h3 className="text-xl font-semibold text-[#2C2218]">Upload or Get Assigned a Case</h3>
              <p className="text-[#5C4C3C]">
                Upload your own PDF case study or get a case assigned by your instructor through
                your class workspace.
              </p>
            </div>
          </div>

          {/* Step 2: Notes & Highlights (replaced Choose Your Mode) */}
          <div className="space-y-6">
            <div className="relative rounded-xl overflow-hidden shadow-lg border border-[#f4e7d8] bg-white">
              <div className="h-60 bg-white p-4 flex flex-col">
                <div className="flex-1 flex gap-2">
                  <div className="w-1/2 bg-[#fff6ed] rounded p-2 text-[8px] leading-tight text-[#6A3A0A]">
                    <div className="font-semibold text-[#2C2218] mb-1">Case Document</div>
                    <div className="space-y-1">
                      <div className="h-1 bg-[#f4e3d4] rounded w-full"></div>
                      <div className="h-1 bg-[#f4e3d4] rounded w-5/6"></div>
                      <div className="h-1 bg-[#f4e3d4] rounded w-full"></div>
                      <div className="h-1 bg-[#f4e3d4] rounded w-4/5"></div>
                      <div className="h-1 bg-[#f4e3d4] rounded w-3/4"></div>
                    </div>
                  </div>
                  <div className="w-1/2 bg-white rounded border border-[#f4e3d4] p-2 flex flex-col">
                    <div className="font-semibold text-[#2C2218] mb-2 text-[10px]">Notes</div>
                    <div className="flex-1 overflow-auto space-y-2">
                      <div className="p-2 bg-[#faf4eb] rounded border border-[#f4e3d4] text-sm">
                        <div className="text-xs text-[#5C4C3C] font-medium">Triage delay impact</div>
                        <div className="text-xs text-[#7a5c3e]">
                          Key metrics show a 40% increase in wait time.
                        </div>
                      </div>
                      <div className="p-2 bg-[#faf4eb] rounded border border-[#f4e3d4] text-sm">
                        <div className="text-xs text-[#5C4C3C] font-medium">Data gaps</div>
                        <div className="text-xs text-[#7a5c3e]">
                          Missing bed availability logs on pages 4–6.
                        </div>
                      </div>
                    </div>
                    <div className="mt-2 text-[11px] text-[#7a5c3e]">
                      Highlights sync with sessions and can be exported.
                    </div>
                  </div>
                </div>
              </div>
              <div className="absolute top-4 left-4 w-10 h-10 bg-[#f6efe3] text-[#C96A08] rounded-full flex items-center justify-center font-bold shadow-lg border border-[#e4c6a1]">
                2
              </div>
            </div>
            <div className="text-center space-y-3">
              <div className="w-12 h-12 bg-[#f6efe3] rounded-full flex items-center justify-center mx-auto">
                <MessageSquare className="w-6 h-6 text-[#C96A08]" />
              </div>
              <h3 className="text-xl font-semibold text-[#2C2218]">Notes & Highlights</h3>
              <p className="text-[#5C4C3C]">
                Capture highlights and structured notes tied to exact passages — review them
                alongside your session history for faster learning.
              </p>
            </div>
          </div>

          {/* Step 3: Get Insights */}
          <div className="space-y-6">
            <div className="relative rounded-xl overflow-hidden shadow-lg border border-[#f4e7d8] bg-white">
              <div className="h-60 bg-white p-4 flex flex-col">
                <div className="flex-1 flex gap-2">
                  <div className="w-1/2 bg-[#fff6ed] rounded p-2 text-[8px] leading-tight text-[#6A3A0A]">
                    <div className="font-semibold text-[#2C2218] mb-1">Supply Chain Analysis</div>
                    <div className="space-y-1 text-[#7a5c3e]">
                      <div>The global supply chain has</div>
                      <div className="bg-[#f6efe3] px-0.5 inline-block">experienced disruptions due to</div>
                      <div className="bg-[#ffe4c3] px-0.5 inline-block">pandemic-related shutdowns</div>
                      <div>and geopolitical tensions.</div>
                    </div>
                    <div className="mt-2 text-[7px] text-[#80705e]">Page 3</div>
                  </div>
                  <div className="w-1/2 bg-white rounded border border-[#f4e3d4] p-2 space-y-2">
                    <div className="flex gap-1.5">
                      <div className="w-4 h-4 bg-[#C96A08] rounded-full flex-shrink-0"></div>
                      <div className="flex-1 space-y-1">
                        <div className="text-[7px] text-[#5C4C3C] leading-tight">
                          The main challenge is pandemic-related supply chain disruptions...
                        </div>
                        <div className="inline-flex items-center gap-0.5 px-1 py-0.5 bg-[#fff6ed] text-[#C96A08] rounded text-[6px] border border-[#f4e3d4]">
                          <FileText className="w-2 h-2" />
                          Page 3
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
              <div className="absolute top-4 left-4 w-10 h-10 bg-[#f6efe3] text-[#C96A08] rounded-full flex items-center justify-center font-bold shadow-lg border border-[#e4c6a1]">
                3
              </div>
            </div>
            <div className="text-center space-y-3">
              <div className="w-12 h-12 bg-[#f6efe3] rounded-full flex items-center justify-center mx-auto">
                <Lightbulb className="w-6 h-6 text-[#C96A08]" />
              </div>
              <h3 className="text-xl font-semibold text-[#2C2218]">Get AI Insights</h3>
              <p className="text-[#5C4C3C]">
                Receive evidence-grounded answers with direct citations to the source document,
                helping you learn effectively.
              </p>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
