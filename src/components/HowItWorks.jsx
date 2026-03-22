import { MessageSquare, Lightbulb, FileText } from 'lucide-react'

export function HowItWorks() {
  return (
    <section className="bg-[#f8f5ef] py-12">
      <div className="relative mx-auto max-w-[1180px] px-4">
        <div className="absolute inset-x-4 top-4 h-[430px] rounded-[38px] bg-[#d8cfc3]" />

        <div className="relative z-10">
          <div className="pt-12 text-center">
            <h2 className="text-[38px] font-medium leading-none text-[#2C2218] md:text-[50px]">
              How It <span className="font-black italic text-[#C96A08]">WORKS</span>
            </h2>

            <p className="mx-auto mt-4 max-w-[780px] text-[16px] leading-[1.35] text-[#b06717] md:text-[18px]">
              Lorem ipsum dolor sit amet consectetur. Nam hendrerit mi lectus odio semper. Amet sit
              tellus mauris morbi sit aliquam cras fermentum posuere.
            </p>
          </div>

          <div className="mt-20 flex flex-col items-center gap-6 md:flex-row md:items-start md:justify-center">
            {/* Step 1 */}
            <div className="w-[350px] shrink-0">
              <div className="relative">
                <div className="absolute bottom-[-16px] left-5 h-[220px] w-[350px] rounded-[24px] bg-[#cdbfaf]" />
                <div className="relative h-[220px] w-[350px] rounded-[24px] bg-[#f4efe9] p-4 shadow-[0_10px_18px_rgba(44,34,24,0.08)]">
                  <div className="absolute left-3 top-3 flex h-10 w-10 items-center justify-center rounded-full bg-[#E58A2A] text-[18px] font-bold text-white">
                    1
                  </div>

                  <div className="h-[180px] rounded-[18px] border border-dashed border-[#7d7368] px-6 py-8">
                    <div className="flex h-full flex-col items-center justify-center text-center">
                      <div className="flex h-14 w-14 items-center justify-center rounded-full bg-[#E58A2A] shadow-sm">
                        <FileText className="h-7 w-7 text-white" />
                      </div>

                      <p className="mt-5 text-[15px] font-medium leading-tight text-[#b06717]">
                        Drop Your PDF Here
                      </p>
                      <p className="text-[15px] leading-tight text-[#b06717]">Or Click to Browse</p>

                      <p className="mt-5 text-[12px] text-[#cabfb2]">Supports PDF upto 50MB</p>
                    </div>
                  </div>
                </div>
              </div>

              <div className="mt-9 text-center">
                <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-[#efe7db]">
                  <FileText className="h-5 w-5 text-[#C96A08]" />
                </div>

                <h3 className="mt-5 text-[22px] font-semibold text-[#2C2218]">
                  Upload or Get Assigned a Case
                </h3>

                <p className="mx-auto mt-4 max-w-[330px] text-[15px] leading-[1.45] text-[#5C4C3C]">
                  Upload your own PDF case study or get a case assigned by your instructor through
                  your class workspace.
                </p>
              </div>
            </div>

            {/* Step 2 */}
            <div className="w-[350px] shrink-0">
              <div className="relative">
                <div className="absolute bottom-[-16px] left-5 h-[220px] w-[350px] rounded-[24px] bg-[#cdbfaf]" />
                <div className="relative h-[220px] w-[350px] rounded-[24px] bg-[#f4efe9] p-4 shadow-[0_10px_18px_rgba(44,34,24,0.08)]">
                  <div className="absolute left-3 top-3 flex h-10 w-10 items-center justify-center rounded-full bg-[#E58A2A] text-[18px] font-bold text-white">
                    2
                  </div>

                  <div className="h-[180px] rounded-[18px] border border-dashed border-[#7d7368] px-4 py-4 pt-11">
                    <div className="flex h-full gap-3">
                      <div className="w-[42%] bg-[#e7ddd0] p-3">
                        <div className="text-[12px] font-semibold text-[#a65f17]">Case Study</div>
                        <p className="mt-2 text-[6px] leading-[1.3] text-[#2C2218]">
                          Lorem ipsum dolor sit amet consectetur. Adipiscing nulla duis congue eu
                          augue. Et interdum convallis id aliquam urna. Ultrices urna senectus
                          cursus amet auctor at massa iaculis ultrices.
                        </p>
                      </div>

                      <div className="flex-1">
                        <div className="text-center text-[14px] font-semibold text-[#b06717]">
                          NOTES
                        </div>

                        <div className="mt-2 space-y-2">
                          <div className="rounded-[14px] bg-[#d8cfc3] p-3 text-[11px] leading-[1.35] text-[#5C4C3C]">
                            Key metrics show a 40% increase in wait time.
                          </div>
                          <div className="rounded-[14px] bg-[#d8cfc3] p-3 text-[11px] leading-[1.35] text-[#5C4C3C]">
                            Missing bed availability logs on pages 4–6.
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <div className="mt-9 text-center">
                <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-[#efe7db]">
                  <MessageSquare className="h-5 w-5 text-[#C96A08]" />
                </div>

                <h3 className="mt-5 text-[22px] font-semibold text-[#2C2218]">
                  Notes & Highlights
                </h3>

                <p className="mx-auto mt-4 max-w-[340px] text-[15px] leading-[1.45] text-[#5C4C3C]">
                  Capture highlights and structured notes tied to exact passages — review them
                  alongside your session history for faster learning.
                </p>
              </div>
            </div>

            {/* Step 3 */}
            <div className="w-[350px] shrink-0">
              <div className="relative">
                <div className="absolute bottom-[-16px] left-5 h-[220px] w-[350px] rounded-[24px] bg-[#cdbfaf]" />
                <div className="relative h-[220px] w-[350px] rounded-[24px] bg-[#f4efe9] p-4 shadow-[0_10px_18px_rgba(44,34,24,0.08)]">
                  <div className="absolute left-3 top-3 flex h-10 w-10 items-center justify-center rounded-full bg-[#E58A2A] text-[18px] font-bold text-white">
                    3
                  </div>

                  <div className="h-[180px] rounded-[18px] border border-dashed border-[#7d7368] px-4 py-4 pt-11">
                    <div className="flex h-full gap-3">
                      <div className="w-[43%] bg-[#e7ddd0] p-3">
                        <div className="text-[12px] font-semibold text-[#2C2218]">Analysis</div>
                        <p className="mt-2 text-[6px] leading-[1.28] text-[#2C2218]">
                          Lorem ipsum dolor sit amet consectetur. Adipiscing nulla duis congue eu
                          augue. Et interdum convallis id aliquam urna.
                        </p>
                      </div>

                      <div className="flex-1 bg-white p-3">
                        <div className="flex gap-2">
                          <div className="mt-1 h-3 w-3 rounded-full bg-[#C96A08]" />
                          <div>
                            <p className="text-[6px] leading-[1.28] text-[#2C2218]">
                              Lorem ipsum dolor sit amet consectetur. Adipiscing nulla duis congue
                              eu augue. Et interdum convallis id aliquam urna.
                            </p>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <div className="mt-9 text-center">
                <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-[#efe7db]">
                  <Lightbulb className="h-5 w-5 text-[#C96A08]" />
                </div>

                <h3 className="mt-5 text-[22px] font-semibold text-[#2C2218]">Get AI Insights</h3>

                <p className="mx-auto mt-4 max-w-[330px] text-[15px] leading-[1.45] text-[#5C4C3C]">
                  Receive evidence-grounded answers with direct citations to the source document,
                  helping you learn effectively.
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
