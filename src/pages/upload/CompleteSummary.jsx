import React from 'react'
import { CheckCircle2, FileText } from 'lucide-react'

export default function CompleteSummary({
  fileName,
  fileSize,
  pageCount,
  imageCount,
  uploadDate,
  analysisPrepStatus = 'idle',
  onStartAnalysis,
  onUploadAnother,
  isInstructor = false,
  onAssignToClass,
}) {
  return (
    <div className="space-y-6">
      <div className="p-6 rounded-2xl bg-[#f5ecde] border border-[#d6c6b4] text-center">
        <div className="flex flex-col items-center gap-4">
          <div className="w-16 h-16 bg-[#f5ecde] rounded-full flex items-center justify-center">
            <CheckCircle2 className="w-8 h-8 text-[#C96A08]" />
          </div>
          <div className="space-y-2">
            <p className="text-xl font-semibold text-[#2C2218]">Upload Complete!</p>
            <p className="text-[#5C4C3C]">Your case study is ready for analysis</p>
          </div>
        </div>
      </div>

      <div className="p-6 rounded-2xl border border-[#d6c6b4] bg-[#f8f5ef]">
        <div className="space-y-4">
          <div className="flex items-start gap-3 pb-4 border-b border-[#d6c6b4]">
            <FileText className="w-5 h-5 text-[#C96A08] mt-0.5" />
            <div className="flex-1">
              <p className="font-medium text-[#2C2218]">{fileName}</p>
            <p className="text-sm text-[#5C4C3C]">
                {isInstructor ? 'Ready to assign to a class' : 'Ready for analysis'}
              </p>
              <p className="mt-1 text-xs text-[#7A5C3E]">
                {analysisPrepStatus === 'ready' && 'Q&A and Reading Coach are prepared.'}
                {analysisPrepStatus === 'preparing' && 'Preparing Q&A and Reading Coach...'}
                {analysisPrepStatus === 'error' &&
                  'Q&A preparation needs a retry in the workspace.'}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-sm text-[#5C4C3C]">File Size</p>
              <p className="font-medium text-[#2C2218]">{fileSize}</p>
            </div>
            <div>
              <p className="text-sm text-[#5C4C3C]">Pages</p>
              <p className="font-medium text-[#2C2218]">{pageCount} pages</p>
            </div>
            <div>
              <p className="text-sm text-[#5C4C3C]">Images</p>
              <p className="font-medium text-[#2C2218]">{imageCount} images</p>
            </div>
            <div>
              <p className="text-sm text-[#5C4C3C]">Uploaded</p>
              <p className="font-medium text-[#2C2218]">{uploadDate}</p>
            </div>
          </div>
        </div>
      </div>

      {isInstructor ? (
        <div className="flex flex-col sm:flex-row gap-3">
          <button
            className="flex-1 rounded-xl bg-[#C96A08] px-5 py-3 text-white shadow-sm shadow-[#C96A08]/30"
            onClick={onAssignToClass}
          >
            Assign to Class
          </button>
          <button
            className="flex-1 rounded-xl border border-[#d6c6b4] bg-white px-5 py-3 text-[#2C2218]"
            onClick={onStartAnalysis}
          >
            Open for Analysis
          </button>
          <button
            className="flex-1 rounded-xl border border-[#d6c6b4] bg-white px-5 py-3 text-[#2C2218]"
            onClick={onUploadAnother}
          >
            Upload Another
          </button>
        </div>
      ) : (
        <div className="flex flex-col sm:flex-row gap-3">
          <button
            className="flex-1 rounded-xl bg-[#C96A08] px-5 py-3 text-white shadow-sm shadow-[#C96A08]/30"
            onClick={onStartAnalysis}
          >
            Start Analysis
          </button>
          <button
            className="flex-1 rounded-xl border border-[#d6c6b4] bg-white px-5 py-3 text-[#2C2218]"
            onClick={onUploadAnother}
          >
            Upload Another
          </button>
        </div>
      )}
    </div>
  )
}
