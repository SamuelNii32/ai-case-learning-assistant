import React from 'react'
import { CheckCircle2, FileText } from 'lucide-react'

export default function CompleteSummary({
  fileName,
  fileSize,
  pageCount,
  imageCount,
  uploadDate,
  onStartAnalysis,
  onUploadAnother,
  isInstructor = false,
  onAssignToClass,
}) {
  return (
    <div className="space-y-6">
      <div className="p-6 rounded-2xl bg-blue-50 border border-blue-200 text-center">
        <div className="flex flex-col items-center gap-4">
          <div className="w-16 h-16 bg-blue-50 rounded-full flex items-center justify-center">
            <CheckCircle2 className="w-8 h-8 text-[#125691]" />
          </div>
          <div className="space-y-2">
            <p className="text-xl font-semibold text-slate-900">Upload Complete!</p>
            <p className="text-slate-600">Your case study is ready for analysis</p>
          </div>
        </div>
      </div>

      <div className="p-6 rounded-2xl border bg-white">
        <div className="space-y-4">
          <div className="flex items-start gap-3 pb-4 border-b border-slate-200">
            <FileText className="w-5 h-5 text-[#125691] mt-0.5" />
            <div className="flex-1">
              <p className="font-medium text-slate-900">{fileName}</p>
              <p className="text-sm text-slate-600">
                {isInstructor ? 'Ready to assign to a class' : 'Ready for analysis'}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-sm text-slate-600">File Size</p>
              <p className="font-medium text-slate-900">{fileSize}</p>
            </div>
            <div>
              <p className="text-sm text-slate-600">Pages</p>
              <p className="font-medium text-slate-900">{pageCount} pages</p>
            </div>
            <div>
              <p className="text-sm text-slate-600">Images</p>
              <p className="font-medium text-slate-900">{imageCount} images</p>
            </div>
            <div>
              <p className="text-sm text-slate-600">Uploaded</p>
              <p className="font-medium text-slate-900">{uploadDate}</p>
            </div>
          </div>
        </div>
      </div>

      {isInstructor ? (
        <div className="flex flex-col sm:flex-row gap-3">
          <button
            className="flex-1 rounded-xl bg-[#125691] px-5 py-3 text-white"
            onClick={onAssignToClass}
          >
            Assign to Class
          </button>
          <button
            className="flex-1 rounded-xl border border-slate-200 bg-white px-5 py-3"
            onClick={onStartAnalysis}
          >
            Open for Analysis
          </button>
          <button
            className="flex-1 rounded-xl border border-slate-200 bg-white px-5 py-3"
            onClick={onUploadAnother}
          >
            Upload Another
          </button>
        </div>
      ) : (
        <div className="flex flex-col sm:flex-row gap-3">
          <button
            className="flex-1 rounded-xl bg-[#125691] px-5 py-3 text-white"
            onClick={onStartAnalysis}
          >
            Start Analysis
          </button>
          <button
            className="flex-1 rounded-xl border border-slate-200 bg-white px-5 py-3"
            onClick={onUploadAnother}
          >
            Upload Another
          </button>
        </div>
      )}
    </div>
  )
}
