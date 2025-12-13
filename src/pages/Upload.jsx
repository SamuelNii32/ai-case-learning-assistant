import React, { useState, useContext } from 'react'
import { useNavigate } from 'react-router-dom'
import { Sparkles, Upload, ArrowLeft, CheckCircle2, FileText, Loader2 } from 'lucide-react'
import UploadCard from './upload/UploadCard'
import UploadProgress from './upload/UploadProgress'
import ProcessingView from './upload/ProcessingView'
import CompleteSummary from './upload/CompleteSummary'
import toast from 'react-hot-toast'
import { API_BASE } from '../config'
import { AuthContext } from '@/contexts/AuthContext'

export default function UploadPage() {
  const navigate = useNavigate()
  const auth = useContext(AuthContext)
  const [uploadState, setUploadState] = useState('idle')
  const [uploadProgress, setUploadProgress] = useState(0)
  const [fileName, setFileName] = useState('')
  const [fileSize, setFileSize] = useState('')
  const [pageCount, setPageCount] = useState(0)
  const [figureCount, setFigureCount] = useState(0)
  const [imageCount, setImageCount] = useState(0)
  const [uploadDate, setUploadDate] = useState('')
  const [uploadId, setUploadId] = useState('')

  function formatMB(bytes) {
    return (bytes / (1024 * 1024)).toFixed(2) + ' MB'
  }

  async function handleFileUpload(e) {
    const file = e.target.files?.[0]
    if (!file) return

    const name = (file.name || '').toLowerCase()
    const isPdf = (file.type || '').toLowerCase().includes('pdf') || /\.pdf$/i.test(name)
    if (!isPdf) {
      toast.error('Please upload a PDF file (.pdf)')
      return
    }
    if (file.size / (1024 * 1024) > 50) {
      toast.error('File is too large. Max 50MB.')
      return
    }

    setFileName(file.name)
    setUploadState('uploading')
    setUploadProgress(25)

    try {
      // runtime check: ensure API base is present
      if (!API_BASE) {
        const msg =
          'VITE_API_BASE is not set. Did you restart the dev server after creating .env/local?'
        console.error(msg)
        alert(msg)
        setUploadState('idle')
        setUploadProgress(0)
        return
      }
      console.log('Uploading file to API:', API_BASE)
      console.log('File:', { name: file.name, size: file.size })
      const fd = new FormData()
      fd.append('file', file)
      // use centralized API helper
      const { uploadFile, getUploadSummary, buildIndex } = await import('../lib/api')
      const data = await uploadFile(fd)
      setUploadId(data.uploadId)

      setUploadState('processing')
      setUploadProgress(60)

      // Kick off indexing (non-blocking): fire-and-forget so the UI isn't blocked by
      // potentially long-running indexing on the server. We still try to fetch the
      // summary a few times (short polling) to populate pages/figures when it's ready.
      try {
        buildIndex(data.uploadId).catch(idxErr => {
          console.error('Index build kicked off failed', idxErr)
        })
      } catch (err) {
        console.error('Failed to call buildIndex', err)
      }

      // Fetch summary with short retry/poll loop so the UI can proceed quickly but
      // still pick up summary info as soon as the backend produces it.
      let summary = null
      const maxAttempts = 6
      for (let attempt = 0; attempt < maxAttempts; attempt++) {
        try {
          const js = await getUploadSummary(data.uploadId)
          summary = js.summary ?? js
          // If we have pages or a fileSize, assume summary is ready
          if (summary && (summary.pages || summary.pages === 0 || summary.fileSizeBytes)) {
            break
          }
        } catch {
          // swallow and retry shortly
        }
        // small backoff
        await new Promise(r => setTimeout(r, 1000))
      }
      // final attempt (best-effort)
      if (!summary) {
        try {
          const js = await getUploadSummary(data.uploadId)
          summary = js.summary ?? js
        } catch (e) {
          console.debug('Summary still unavailable after retries', e)
          summary = {}
        }
      }
      const s = summary.summary ?? summary

      setPageCount(s.pages ?? 0)
      setFigureCount(s.counts?.figures ?? 0)
      setImageCount(s.counts?.images ?? 0)

      const bytes = s.fileSizeBytes ?? (s.fileSizeMB ? s.fileSizeMB * 1024 * 1024 : file.size)
      setFileSize(formatMB(bytes))
      setUploadDate(new Date(s.uploadedAt ?? Date.now()).toLocaleString())

      setUploadProgress(100)
      // Notify other parts of the app (Dashboard) that a new case uploaded so they can refresh
      try {
        window.dispatchEvent(
          new CustomEvent('case:uploaded', { detail: { uploadId: data.uploadId } })
        )
      } catch (e) {
        console.debug('Failed to dispatch case:uploaded event', e)
      }

      setTimeout(() => setUploadState('complete'), 400)
    } catch (err) {
      // Developer-facing details in console
      console.error('Upload error:', err)

      // Friendly message for end users
      const userMessage =
        typeof err === 'string'
          ? err
          : err && err.message
            ? 'Upload failed. Please try again.'
            : 'Upload failed. Please try again.'

      toast.error(userMessage)

      setUploadState('idle')
      setUploadProgress(0)
    }
  }

  return (
    <div className="min-h-screen bg-slate-50">
      {/* Header */}
      <header className="border-b border-slate-200 bg-white/50 backdrop-blur-sm">
        <div className="container mx-auto px-4 h-16 flex items-center justify-between">
          <button
            type="button"
            onClick={() => navigate(-1)}
            className="inline-flex items-center gap-2 text-sm text-slate-700"
          >
            <ArrowLeft className="w-4 h-4" />
            Back
          </button>

          <div className="flex items-center gap-2">
            <div className="w-8 h-8 bg-[#125691] rounded-lg flex items-center justify-center">
              <Sparkles className="w-5 h-5 text-white" />
            </div>
            <span className="font-semibold text-lg text-slate-900">CaseAI</span>
          </div>

          <div className="w-20" />
        </div>
      </header>

      <div className="container mx-auto px-4 py-12 max-w-3xl">
        <div className="space-y-8">
          <div className="text-center space-y-3">
            <h1 className="text-4xl font-bold text-slate-900">Upload Your Case Study</h1>
            <p className="text-lg text-slate-600">
              Upload a PDF case study to begin your AI-powered analysis journey
            </p>
          </div>

          {uploadState === 'idle' && <UploadCard onFileChange={handleFileUpload} />}

          {uploadState === 'uploading' && (
            <UploadProgress fileName={fileName} fileSize={fileSize} progress={uploadProgress} />
          )}

          {uploadState === 'processing' && <ProcessingView />}

          {uploadState === 'complete' && (
            <CompleteSummary
              fileName={fileName}
              fileSize={fileSize}
              pageCount={pageCount}
              figureCount={figureCount}
              imageCount={imageCount}
              uploadDate={uploadDate}
              onStartAnalysis={() => navigate(`/workspace/${uploadId}`)}
              onUploadAnother={() => setUploadState('idle')}
              isInstructor={auth?.user?.role === 'instructor'}
              onAssignToClass={() => navigate(`/admin/classes?uploadId=${encodeURIComponent(uploadId)}`)}
            />
          )}

          {uploadState === 'idle' && (
            <div className="pt-8 border-t border-slate-200">
              <h3 className="text-lg font-semibold text-slate-900 mb-4">What happens next?</h3>
              <div className="space-y-3">
                <div className="flex gap-3">
                  <div className="w-6 h-6 bg-slate-50 rounded-full flex items-center justify-center flex-shrink-0 text-[#125691] text-sm font-semibold">
                    1
                  </div>
                  <p className="text-slate-600">Your PDF will be securely uploaded and processed</p>
                </div>
                <div className="flex gap-3">
                  <div className="w-6 h-6 bg-slate-50 rounded-full flex items-center justify-center flex-shrink-0 text-[#125691] text-sm font-semibold">
                    2
                  </div>
                  <p className="text-slate-600">
                    AI will analyze the document structure and content
                  </p>
                </div>
                <div className="flex gap-3">
                  <div className="w-6 h-6 bg-slate-50 rounded-full flex items-center justify-center flex-shrink-0 text-[#125691] text-sm font-semibold">
                    3
                  </div>
                  <p className="text-slate-600">
                    You'll be taken to the workspace to begin your analysis
                  </p>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
