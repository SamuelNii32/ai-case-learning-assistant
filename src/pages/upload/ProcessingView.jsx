import React from 'react'
import { Loader2 } from 'lucide-react'

export default function ProcessingView() {
  return (
    <div className="p-12 rounded-2xl border border-[#d6c6b4] bg-[#f8f5ef] text-center">
      <div className="w-20 h-20 bg-[#f5ecde] rounded-full flex items-center justify-center mx-auto mb-4">
        <Loader2 className="w-10 h-10 text-[#C96A08] animate-spin" />
      </div>
      <div className="space-y-2">
        <p className="text-xl font-semibold text-[#2C2218]">Processing Your Case</p>
        <p className="text-[#5C4C3C]">Analyzing document structure and content...</p>
      </div>
    </div>
  )
}
