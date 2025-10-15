import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Sparkles, Upload, ArrowLeft, CheckCircle2, FileText, Loader2 } from "lucide-react";

export default function UploadPage() {
  const navigate = useNavigate();
  const [uploadState, setUploadState] = useState("idle"); // idle | uploading | processing | complete
  const [uploadProgress, setUploadProgress] = useState(0);
  const [fileName, setFileName] = useState("");
  const [fileSize, setFileSize] = useState("");
  const [pageCount, setPageCount] = useState(0);
  const [figureCount, setFigureCount] = useState(0);
  const [imageCount, setImageCount] = useState(0);
  const [uploadDate, setUploadDate] = useState("");

  function formatMB(bytes) {
    return (bytes / (1024 * 1024)).toFixed(2) + " MB";
  }

  function handleFileUpload(e) {
    const file = e.target.files?.[0];
    if (!file) return;

    // basic client-side checks (you should still validate server-side)
    const name = (file.name || "").toLowerCase();
    const isPdf = (file.type || "").toLowerCase().includes("pdf") || /\.pdf$/i.test(name);
    if (!isPdf) {
      alert("Please upload a PDF file (.pdf)");
      return;
    }

    const sizeInMB = file.size / (1024 * 1024);
    if (sizeInMB > 50) {
      alert("File is too large. Max 50MB.");
      return;
    }

    // seed metadata
    setFileName(file.name);
    setFileSize(formatMB(file.size));
    setPageCount(Math.floor(Math.random() * 20) + 5);
    setFigureCount(Math.floor(Math.random() * 8) + 2);
    setImageCount(Math.floor(Math.random() * 15) + 3);
    setUploadDate(new Date().toLocaleString());

    // start upload simulation
    setUploadState("uploading");
    setUploadProgress(0);
    const uploadInterval = setInterval(() => {
      setUploadProgress((prev) => {
        const next = prev + 10;
        if (next >= 100) {
          clearInterval(uploadInterval);
          setUploadProgress(100);
          setUploadState("processing");
          setTimeout(() => setUploadState("complete"), 2000);
        }
        return Math.min(next, 100);
      });
    }, 200);
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
            <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center">
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
            <p className="text-lg text-slate-600">Upload a PDF case study to begin your AI-powered analysis journey</p>
          </div>

            {uploadState === "idle" && (
            <div className="rounded-2xl border-2 border-dashed border-slate-300 hover:border-blue-400 transition-colors">
              <label className="block cursor-pointer">
                <input type="file" accept=".pdf" className="hidden" onChange={handleFileUpload} />
                <div className="p-20 text-center space-y-6">
                  <div className="w-20 h-20 bg-blue-50 rounded-full flex items-center justify-center mx-auto">
                    <Upload className="w-10 h-10 text-blue-600" />
                  </div>
                  <div className="space-y-3">
                    <p className="text-xl font-semibold text-slate-900">Click to upload or drag and drop</p>
                    <p className="text-slate-600">PDF files up to 50MB</p>
                  </div>
                  <div className="pt-4">
                    <button type="button" className="inline-flex items-center rounded-md bg-blue-600 text-white px-4 py-2">Select File</button>
                  </div>
                </div>
              </label>
            </div>
          )}

          {uploadState === "uploading" && (
            <div className="p-8 rounded-2xl border bg-white">
              <div className="space-y-6">
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 bg-blue-50 rounded-lg flex items-center justify-center flex-shrink-0">
                    <FileText className="w-6 h-6 text-blue-600" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="font-medium text-slate-900 truncate">{fileName}</p>
                    <p className="text-sm text-slate-600">{fileSize} • Uploading...</p>
                  </div>
                </div>
                <div className="space-y-2">
                  <div className="h-2 bg-slate-200 rounded-full overflow-hidden">
                    <div className="h-full bg-blue-600 transition-[width]" style={{ width: `${uploadProgress}%` }} />
                  </div>
                  <p className="text-sm text-slate-600 text-right">{uploadProgress}%</p>
                </div>
              </div>
            </div>
          )}

          {uploadState === "processing" && (
            <div className="p-12 rounded-2xl border bg-white text-center">
              <div className="w-20 h-20 bg-blue-50 rounded-full flex items-center justify-center mx-auto mb-4">
                <Loader2 className="w-10 h-10 text-blue-600 animate-spin" />
              </div>
              <div className="space-y-2">
                <p className="text-xl font-semibold text-slate-900">Processing Your Case</p>
                <p className="text-slate-600">Analyzing document structure and content...</p>
              </div>
            </div>
          )}

          {uploadState === "complete" && (
            <div className="space-y-6">
              <div className="p-6 rounded-2xl bg-blue-50 border border-blue-200 text-center">
                <div className="flex flex-col items-center gap-4">
                  <div className="w-16 h-16 bg-blue-50 rounded-full flex items-center justify-center">
                    <CheckCircle2 className="w-8 h-8 text-blue-600" />
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
                    <FileText className="w-5 h-5 text-blue-600 mt-0.5" />
                    <div className="flex-1">
                      <p className="font-medium text-slate-900">{fileName}</p>
                      <p className="text-sm text-slate-600">Ready for analysis</p>
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
                      <p className="text-sm text-slate-600">Figures</p>
                      <p className="font-medium text-slate-900">{figureCount} figures</p>
                    </div>
                    <div>
                      <p className="text-sm text-slate-600">Images</p>
                      <p className="font-medium text-slate-900">{imageCount} images</p>
                    </div>
                    <div>
                      <p className="text-sm text-slate-600">Type</p>
                      <p className="font-medium text-slate-900">PDF Document</p>
                    </div>
                    <div>
                      <p className="text-sm text-slate-600">Uploaded</p>
                      <p className="font-medium text-slate-900">{uploadDate}</p>
                    </div>
                  </div>
                </div>
              </div>

              <div className="flex flex-col sm:flex-row gap-3">
                <button className="flex-1 rounded-xl bg-blue-600 px-5 py-3 text-white" onClick={() => navigate("/workspace")}>Start Analysis</button>
                <button className="flex-1 rounded-xl border border-slate-200 bg-white px-5 py-3" onClick={() => setUploadState("idle")}>Upload Another</button>
              </div>
            </div>
          )}

            {uploadState === "idle" && (
            <div className="pt-8 border-t border-slate-200">
              <h3 className="text-lg font-semibold text-slate-900 mb-4">What happens next?</h3>
              <div className="space-y-3">
                <div className="flex gap-3">
                  <div className="w-6 h-6 bg-blue-50 rounded-full flex items-center justify-center flex-shrink-0 text-blue-600 text-sm font-semibold">1</div>
                  <p className="text-slate-600">Your PDF will be securely uploaded and processed</p>
                </div>
                <div className="flex gap-3">
                  <div className="w-6 h-6 bg-blue-50 rounded-full flex items-center justify-center flex-shrink-0 text-blue-600 text-sm font-semibold">2</div>
                  <p className="text-slate-600">AI will analyze the document structure and content</p>
                </div>
                <div className="flex gap-3">
                  <div className="w-6 h-6 bg-blue-50 rounded-full flex items-center justify-center flex-shrink-0 text-blue-600 text-sm font-semibold">3</div>
                  <p className="text-slate-600">You'll be taken to the workspace to begin your analysis</p>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
