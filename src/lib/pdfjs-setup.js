// src/lib/pdfjs-setup.js
// One-time PDF.js worker setup for Vite/React

import { GlobalWorkerOptions } from 'pdfjs-dist'
import pdfjsWorker from 'pdfjs-dist/build/pdf.worker.mjs?worker'

// Give PDF.js a real Worker instance (best for Vite/Rollup)
GlobalWorkerOptions.workerPort = new pdfjsWorker()
