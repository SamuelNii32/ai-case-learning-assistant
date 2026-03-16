import React from 'react'
import { FileText } from 'lucide-react'

export default function UploadProgress({ fileName, fileSize, progress }) {
  return (
    <div className="p-8 rounded-2xl border border-[#d6c6b4] bg-[#f8f5ef]">
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 bg-[#f5ecde] rounded-lg flex items-center justify-center flex-shrink-0">
            <FileText className="w-6 h-6 text-[#C96A08]" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="font-medium text-[#2C2218] truncate">{fileName}</p>
            <p className="text-sm text-[#5C4C3C]">{fileSize} • Uploading...</p>
          </div>
        </div>
        <div className="space-y-2">
          <div className="h-2 bg-slate-200 rounded-full overflow-hidden">
            <div
              className="h-full bg-[#C96A08] transition-[width]"
              style={{ width: `${progress}%` }}
            />
          </div>
          <p className="text-sm text-[#5C4C3C] text-right">{progress}%</p>
        </div>
      </div>
    </div>
  )
}
