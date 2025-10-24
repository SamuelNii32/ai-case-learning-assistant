// This file was previously a full TypeScript implementation of the PdfViewer.
// The project currently uses the stable JSX implementation at `PdfViewer.jsx`.
// Keep a minimal re-export here so existing imports that reference the TS file
// won't break while avoiding duplicate implementations.

import React, { lazy, Suspense } from 'react'

// Dynamically load the JS viewer at runtime to avoid circular import issues
const LazyViewer = lazy(() => import('./PdfViewer.jsx'))

export default function PdfViewerWrapper(props: any) {
  return (
    <Suspense fallback={<div style={{ padding: 16 }}>Loading PDF viewer…</div>}>
      {/* @ts-ignore allow passing through props to the underlying viewer */}
      <LazyViewer {...props} />
    </Suspense>
  )
}
