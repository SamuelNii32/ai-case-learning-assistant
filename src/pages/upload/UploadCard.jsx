import React, { useRef } from 'react'
import { Upload } from 'lucide-react'

export default function UploadCard({ onFileChange }) {
  const inputRef = useRef(null)

  const handleButtonClick = e => {
    // prevent the button from submitting forms or stealing focus
    e.preventDefault()
    inputRef.current?.click()
  }

  const handleWrapperClick = e => {
    if (e.target instanceof HTMLButtonElement) return
    inputRef.current?.click()
  }

  const handleWrapperKeyDown = e => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      inputRef.current?.click()
    }
  }

  return (
    <div
      className="group rounded-2xl border-2 border-dashed border-[#d6c6b4] bg-white transition-colors duration-200 hover:border-[#C96A08] hover:bg-[#fff2e8]"
      onClick={handleWrapperClick}
      onKeyDown={handleWrapperKeyDown}
      role="button"
      tabIndex={0}
    >
      {/* prefer only PDF uploads */}
        <input
          id="upload-input"
          ref={inputRef}
          type="file"
          accept="application/pdf"
          className="hidden"
          onChange={onFileChange}
          aria-label="Upload PDF file"
        />
        <div className="p-20 text-center space-y-6">
          <div className="w-20 h-20 bg-[#eed7c3] rounded-full flex items-center justify-center mx-auto shadow-inner">
            <Upload className="w-10 h-10 text-[#C96A08]" />
          </div>
          <div className="space-y-3">
            <p className="text-xl font-semibold text-[#2C2218]">Click to upload or drag and drop</p>
            <p className="text-[#5C4C3C]">PDF files up to 50MB</p>
          </div>
          <div className="pt-4">
            <button
              type="button"
              onClick={handleButtonClick}
              className="inline-flex items-center rounded-md bg-[#C96A08] hover:bg-[#a05706] text-white px-4 py-2"
            >
              Select File
            </button>
          </div>
        </div>
    </div>
  )
}
