// src/lib/pdfjs-setup.js
// One-time PDF.js worker setup for Vite/React
// Export an async initializer so the worker chunk is only loaded when explicitly requested.

export async function initPdfWorker() {
  const { GlobalWorkerOptions } = await import('pdfjs-dist')
  // Dynamically import the worker module so bundlers keep it separate until needed.
  const WorkerModule = await import('pdfjs-dist/build/pdf.worker.mjs?worker')
  const WorkerConstructor = WorkerModule.default || WorkerModule
  GlobalWorkerOptions.workerPort = new WorkerConstructor()
}

export default initPdfWorker
