import { Upload, MessageSquare, Lightbulb, FileText } from 'lucide-react'

export function HowItWorks() {
  return (
    <section className="bg-slate-50 py-20">
      <div className="container mx-auto px-6">
        <div className="text-center mb-16">
          <h2 className="text-4xl font-bold text-slate-900 mb-4">How It Works</h2>
          <p className="text-lg text-slate-600">Get started in three simple steps</p>
        </div>

        <div className="grid md:grid-cols-3 gap-12">
          {/* Step 1: Upload */}
          <div className="space-y-6">
            <div className="relative rounded-xl overflow-hidden shadow-lg border border-slate-200 bg-white">
              <div className="h-60 bg-gradient-to-br from-slate-50 to-slate-100 p-6 flex flex-col items-center justify-center">
                <div className="w-full max-w-sm space-y-4">
                  <div className="border-2 border-dashed border-slate-200 rounded-lg p-8 bg-slate-50/50 flex flex-col items-center gap-3">
                    <div className="w-12 h-12 bg-[#125691] rounded-full flex items-center justify-center">
                      <Upload className="w-6 h-6 text-white" />
                    </div>
                    <div className="text-center">
                      <div className="text-sm font-medium text-slate-900">Drop your PDF here</div>
                      <div className="text-xs text-slate-500">or click to browse</div>
                    </div>
                  </div>
                  <div className="text-xs text-slate-400 text-center">Supports PDF up to 50MB</div>
                </div>
              </div>
              <div className="absolute top-4 left-4 w-10 h-10 bg-[#125691] text-white rounded-full flex items-center justify-center font-bold shadow-lg">
                1
              </div>
            </div>
            <div className="text-center space-y-3">
              <div className="w-12 h-12 bg-slate-100 rounded-full flex items-center justify-center mx-auto">
                <Upload className="w-6 h-6 text-[#125691]" />
              </div>
              <h3 className="text-xl font-semibold text-slate-900">Upload Your Case</h3>
              <p className="text-slate-600">
                Upload your case document in PDF format. Our system processes multimodal content
                including text, images, and figures.
              </p>
            </div>
          </div>

          {/* Step 2: Notes & Highlights (replaced Choose Your Mode) */}
          <div className="space-y-6">
            <div className="relative rounded-xl overflow-hidden shadow-lg border border-slate-200 bg-white">
              <div className="h-60 bg-white p-4 flex flex-col">
                <div className="flex-1 flex gap-2">
                  <div className="w-1/2 bg-slate-100 rounded p-2 text-[8px] leading-tight text-slate-600">
                    <div className="font-semibold text-slate-900 mb-1">Case Document</div>
                    <div className="space-y-1">
                      <div className="h-1 bg-slate-300 rounded w-full"></div>
                      <div className="h-1 bg-slate-300 rounded w-5/6"></div>
                      <div className="h-1 bg-slate-300 rounded w-full"></div>
                      <div className="h-1 bg-slate-300 rounded w-4/5"></div>
                      <div className="h-1 bg-slate-300 rounded w-3/4"></div>
                    </div>
                  </div>
                  <div className="w-1/2 bg-white rounded border border-slate-200 p-2 flex flex-col">
                    <div className="font-semibold text-slate-900 mb-2 text-[10px]">Notes</div>
                    <div className="flex-1 overflow-auto space-y-2">
                      <div className="p-2 bg-slate-50 rounded border border-slate-100 text-sm">
                        <div className="text-xs text-slate-700 font-medium">Triage delay impact</div>
                        <div className="text-xs text-slate-500">Key metrics show a 40% increase in wait time.</div>
                      </div>
                      <div className="p-2 bg-slate-50 rounded border border-slate-100 text-sm">
                        <div className="text-xs text-slate-700 font-medium">Data gaps</div>
                        <div className="text-xs text-slate-500">Missing bed availability logs on pages 4–6.</div>
                      </div>
                    </div>
                    <div className="mt-2 text-[11px] text-slate-500">Highlights sync with sessions and can be exported.</div>
                  </div>
                </div>
              </div>
              <div className="absolute top-4 left-4 w-10 h-10 bg-[#125691] text-white rounded-full flex items-center justify-center font-bold shadow-lg">
                2
              </div>
            </div>
            <div className="text-center space-y-3">
              <div className="w-12 h-12 bg-slate-100 rounded-full flex items-center justify-center mx-auto">
                <MessageSquare className="w-6 h-6 text-indigo-600" />
              </div>
              <h3 className="text-xl font-semibold text-slate-900">Notes & Highlights</h3>
              <p className="text-slate-600">
                Capture highlights and structured notes tied to exact passages — review them alongside
                your session history for faster learning.
              </p>
            </div>
          </div>

          {/* Step 3: Get Insights */}
          <div className="space-y-6">
            <div className="relative rounded-xl overflow-hidden shadow-lg border border-slate-200 bg-white">
              <div className="h-60 bg-white p-4 flex flex-col">
                <div className="flex-1 flex gap-2">
                  <div className="w-1/2 bg-slate-100 rounded p-2 text-[8px] leading-tight">
                    <div className="font-semibold text-slate-900 mb-1">Supply Chain Analysis</div>
                    <div className="space-y-1 text-slate-600">
                      <div>The global supply chain has</div>
                      <div className="bg-slate-200 px-0.5 inline-block">
                        experienced disruptions due to
                      </div>
                      <div className="bg-blue-200 px-0.5 inline-block">
                        pandemic-related shutdowns
                      </div>
                      <div>and geopolitical tensions.</div>
                    </div>
                    <div className="mt-2 text-[7px] text-slate-400">Page 3</div>
                  </div>
                  <div className="w-1/2 bg-white rounded border border-slate-200 p-2 space-y-2">
                    <div className="flex gap-1.5">
                      <div className="w-4 h-4 bg-[#125691] rounded-full flex-shrink-0"></div>
                      <div className="flex-1 space-y-1">
                        <div className="text-[7px] text-slate-700 leading-tight">
                          The main challenge is pandemic-related supply chain disruptions...
                        </div>
                        <div className="inline-flex items-center gap-0.5 px-1 py-0.5 bg-slate-100 text-[#125691] rounded text-[6px]">
                          <FileText className="w-2 h-2" />
                          Page 3
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
              <div className="absolute top-4 left-4 w-10 h-10 bg-gradient-to-r from-purple-500 to-pink-600 text-white rounded-full flex items-center justify-center font-bold shadow-lg">
                3
              </div>
            </div>
            <div className="text-center space-y-3">
              <div className="w-12 h-12 bg-gradient-to-r from-purple-100 to-pink-200 rounded-full flex items-center justify-center mx-auto">
                <Lightbulb className="w-6 h-6 text-purple-600" />
              </div>
              <h3 className="text-xl font-semibold text-slate-900">Get AI Insights</h3>
              <p className="text-slate-600">
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
