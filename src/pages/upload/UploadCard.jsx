import React from 'react'
import { Upload } from 'lucide-react'

export default function UploadCard({ onFileChange }) {
  return (
    <div className="rounded-2xl border-2 border-dashed border-slate-300 hover:border-blue-400 transition-colors">
      <label className="block cursor-pointer">
        <input type="file" accept="*/*" className="hidden" onChange={onFileChange} />
        <div className="p-20 text-center space-y-6">
          <div className="w-20 h-20 bg-slate-50 rounded-full flex items-center justify-center mx-auto">
            <Upload className="w-10 h-10 text-[#125691]" />
          </div>
          <div className="space-y-3">
            <p className="text-xl font-semibold text-slate-900">Click to upload or drag and drop</p>
            <p className="text-slate-600">PDF files up to 50MB</p>
          </div>
          <div className="pt-4">
            <button
              type="button"
              className="inline-flex items-center rounded-md bg-[#125691] text-white px-4 py-2"
            >
              Select File
            </button>
          </div>
        </div>
      </label>
    </div>
  )
}
