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
  const [analysisPrepStatus, setAnalysisPrepStatus] = useState('idle')

  function formatMB(bytes) {
    return (bytes / (1024 * 1024)).toFixed(2) + ' MB'
  }

  const nextSteps = [
    {
      title: 'Secure upload',
      detail: 'Your PDF is encrypted, uploaded, and stored for processing',
    },
    {
      title: 'AI analyzes the document',
      detail: 'Our models structure the pages, figures, and metadata',
    },
    {
      title: 'Workspace opens for analysis',
      detail: "You're taken into the workspace to explore insights",
    },
  ]

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
    setAnalysisPrepStatus('idle')
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
      const fd = new FormData()
      fd.append('file', file)
      // use centralized API helper
      const { uploadFile, getUploadSummary, buildIndex } = await import('../lib/api')
      const data = await uploadFile(fd)
      setUploadId(data.uploadId)

      setUploadState('processing')
      setUploadProgress(60)
      setAnalysisPrepStatus('preparing')

      // Start indexing immediately and let it run while summary polling happens.
      // Waiting here makes the workspace feel faster because Q&A/Reading Coach
      // can use the index as soon as the student opens the document.
      const indexPromise = buildIndex(data.uploadId)
        .then(summary => {
          setAnalysisPrepStatus('ready')
          return summary
        })
        .catch(idxErr => {
          console.error('Index build failed after upload', idxErr)
          setAnalysisPrepStatus('error')
          return null
        })

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

      setUploadProgress(85)
      const indexSummary = await indexPromise
      if (indexSummary) {
        setAnalysisPrepStatus('ready')
      } else {
        toast.error('Document uploaded, but Q&A preparation failed. You can retry in the workspace.')
      }

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
      setAnalysisPrepStatus('idle')
    }
  }

  return (
    <div className="min-h-screen bg-[#f8f5ef]">
      {/* Header */}
      <header className="border-b border-[#d6c6b4] bg-white/80 backdrop-blur-sm">
        <div className="container mx-auto px-4 h-16 flex items-center justify-between">
          <button
            type="button"
            onClick={() => navigate(-1)}
            className="inline-flex items-center gap-2 text-sm font-medium text-[#2C2218]"
          >
            <ArrowLeft className="w-4 h-4" />
            Back
          </button>

          <div className="flex items-center gap-2">
            <div className="w-8 h-8 bg-[#C96A08] rounded-lg flex items-center justify-center">
              <Sparkles className="w-5 h-5 text-[#f8f5ef]" />
            </div>
            <span className="font-semibold text-lg text-[#2C2218]">CasePilot</span>
          </div>

          <div className="w-20" />
        </div>
      </header>

      <div className="container mx-auto px-4 py-12 max-w-3xl">
        <div className="space-y-5">
          <div className="text-center space-y-1">
            <h1 className="text-4xl font-bold text-[#2C2218]">Upload Your Case Study</h1>
            <p className="text-lg text-[#5C4C3C]">
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
              analysisPrepStatus={analysisPrepStatus}
              onStartAnalysis={() => navigate(`/workspace/${uploadId}`)}
              onUploadAnother={() => setUploadState('idle')}
              isInstructor={auth?.user?.role === 'instructor'}
              onAssignToClass={() => navigate(`/admin/classes?uploadId=${encodeURIComponent(uploadId)}`)}
            />
          )}

          {uploadState === 'idle' && (
            <div className="pt-8 border-t border-[#d6c6b4]">
              <div className="text-lg font-semibold text-[#2C2218] mb-4">What happens next?</div>
              <div className="space-y-4">
                {nextSteps.map((step, index) => (
                  <div key={step.title} className="flex gap-3">
                    <div className="h-10 w-10 rounded-2xl border border-[#C96A08]/30 bg-[#fff2e4] text-[#C96A08] text-sm font-semibold flex items-center justify-center">
                      {index + 1}
                    </div>
                    <div>
                      <p className="text-sm font-semibold text-[#2C2218]">{step.title}</p>
                      <p className="text-sm text-[#5C4C3C] max-w-prose">{step.detail}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
