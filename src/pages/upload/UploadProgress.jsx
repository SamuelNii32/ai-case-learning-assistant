import React from "react";
import { FileText } from "lucide-react";

export default function UploadProgress({ fileName, fileSize, progress }) {
  return (
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
            <div className="h-full bg-blue-600 transition-[width]" style={{ width: `${progress}%` }} />
          </div>
          <p className="text-sm text-slate-600 text-right">{progress}%</p>
        </div>
      </div>
    </div>
  );
}
