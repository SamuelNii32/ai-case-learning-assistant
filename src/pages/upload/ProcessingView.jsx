import React from "react";
import { Loader2 } from "lucide-react";

export default function ProcessingView() {
  return (
    <div className="p-12 rounded-2xl border bg-white text-center">
      <div className="w-20 h-20 bg-blue-50 rounded-full flex items-center justify-center mx-auto mb-4">
        <Loader2 className="w-10 h-10 text-blue-600 animate-spin" />
      </div>
      <div className="space-y-2">
        <p className="text-xl font-semibold text-slate-900">Processing Your Case</p>
        <p className="text-slate-600">Analyzing document structure and content...</p>
      </div>
    </div>
  );
}
