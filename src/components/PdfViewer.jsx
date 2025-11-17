// src/components/PdfViewer.jsx
import React, { useEffect, useRef, useState, useCallback } from 'react'

export default function PdfViewer({ src, onReady, initialScale = 1.5, fitToWidth = true }) {
  const scrollHostRef = useRef(null)
  const pdfRef = useRef(null) // PDFDocumentProxy
  const [numPages, setNumPages] = useState(0)
  const numPagesRef = useRef(0)
  const pageMapRef = useRef(new Map()) // pageNumber -> { container, canvas, overlay, rendered, scale, pageHeightPt }

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const bodyAbortRetryRef = useRef(false)
  const [loadToggle, setLoadToggle] = useState(0)

  // keep onReady stable
  const onReadyRef = useRef(onReady)
  useEffect(() => {
    onReadyRef.current = onReady
  }, [onReady])

  // serialize destroys across src changes (prevents worker race)
  const destroyChainRef = useRef(Promise.resolve())

  // --- element refs per page ---
  const setContainer = useCallback((n, el) => {
    const rec = pageMapRef.current.get(n) || {}
    rec.container = el
    pageMapRef.current.set(n, rec)
  }, [])

  const renderPageRef = useRef(null)
  // Keep track of active render tasks per page so we can cancel them
  const renderTaskMapRef = useRef(new Map())

  const setCanvas = useCallback((n, el) => {
    const rec = pageMapRef.current.get(n) || {}
    const hadCanvas = !!rec.canvas
    rec.canvas = el
    pageMapRef.current.set(n, rec)
    // kick off first paint as soon as page 1's canvas mounts
    if (n === 1 && el && !rec.rendered && !hadCanvas) {
      requestAnimationFrame(() => renderPageRef.current?.(1))
    }
  }, [])

  const setOverlay = useCallback((n, el) => {
    const rec = pageMapRef.current.get(n) || {}
    rec.overlay = el
    pageMapRef.current.set(n, rec)
  }, [])

  // --- compute scale to fit width ---
  const computeWidthScale = useCallback(async page => {
    const host = scrollHostRef.current
    const hostWidth = host?.clientWidth || window.innerWidth || 800
    const base = page.getViewport({ scale: 1 }) // width/height at scale=1 honoring PDF rotation
    const target = Math.max(100, hostWidth) // adjust if you add horizontal padding
    const scale = target / base.width
    return Math.max(0.25, Math.min(scale, 5))
  }, [])

  // --- stable page renderer (fixed orientation) ---
  renderPageRef.current = async n => {
    if (!pdfRef.current) return
    const rec = pageMapRef.current.get(n) || {}
    if (!rec.canvas) return

    const page = await pdfRef.current.getPage(n)
    const scale = fitToWidth ? await computeWidthScale(page) : initialScale

    // By default, getViewport uses page.rotate (correct orientation).
    // We keep the 2D context at identity and pass HiDPI scaling via `transform`.
    const viewport = page.getViewport({ scale })
    const canvas = rec.canvas
    const ctx = canvas.getContext('2d')

    const dpr = window.devicePixelRatio || 1
    canvas.width = Math.floor(viewport.width * dpr)
    canvas.height = Math.floor(viewport.height * dpr)
    canvas.style.width = `${Math.floor(viewport.width)}px`
    canvas.style.height = `${Math.floor(viewport.height)}px`

    try {
      // Cancel any existing render task for this page
      const previous = renderTaskMapRef.current.get(n)
      if (previous && previous.cancel) {
        try {
          previous.cancel()
        } catch (errCancel) {
          console.debug('[PdfViewer] previous render cancel failed', errCancel)
        }
      }

      const renderTask = page.render({
        canvasContext: ctx,
        viewport,
        // IMPORTANT: use transform for HiDPI; don't call ctx.setTransform(...)
        transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : undefined,
      })
      renderTaskMapRef.current.set(n, renderTask)
      await renderTask.promise
      renderTaskMapRef.current.delete(n)
    } catch (err) {
      // RenderingCancelledException is expected when a render task is
      // cancelled due to a document reload or user navigation. Treat it as
      // debug-level noise rather than an error to avoid alarming logs.
      try {
        if (err && err.name === 'RenderingCancelledException') {
          console.debug('[PdfViewer] page.render cancelled for page', n)
        } else {
          console.error('[PdfViewer] page.render failed for page', n, err)
        }
      } catch (logErr) {
        console.error('[PdfViewer] page.render unexpected error', n, err, logErr)
      }
    }

    rec.rendered = true
    rec.scale = scale
    rec.pageHeightPt = page.view[3]
    pageMapRef.current.set(n, rec)
  }

  const ensureRendered = useCallback(async n => {
    const rec = pageMapRef.current.get(n)
    if (!rec || !rec.rendered) {
      await renderPageRef.current(n)
    }
  }, [])

  const scrollToPage = useCallback(
    async n => {
      const max = numPagesRef.current || 1
      if (n < 1 || n > max) return
      await ensureRendered(n)
      if (n + 1 <= max) ensureRendered(n + 1)
      if (n - 1 >= 1) ensureRendered(n - 1)
      const rec = pageMapRef.current.get(n)
      if (rec?.container) {
        rec.container.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }
    },
    [ensureRendered]
  )

  const showHighlight = useCallback(
    async ({ page /*, bbox */ }) => {
      await scrollToPage(page)
      const rec = pageMapRef.current.get(page)
      if (!rec) return

      // Instead of filling the whole page with a colored overlay, briefly add
      // a subtle focus class to the page container so it gets a soft border /
      // shadow. This communicates "this is the page you jumped to" without
      // coloring every pixel.
      const container = rec.container
      if (!container) return

      container.classList.add('pdf-page-focus')

      // remove after the focus animation completes
      setTimeout(() => {
        try {
          container.classList.remove('pdf-page-focus')
        } catch {
          /* ignore */
        }
      }, 1200)
    },
    [scrollToPage]
  )

  // --- load document (serialized) ---
  useEffect(() => {
  let cancelled = false
  let pdfjsVersion = 'unknown'
    let task
    ;(async () => {
      try {
        await destroyChainRef.current
      } catch (errDC) {
        console.debug('[PdfViewer] destroyChain await failed', errDC)
      }

      setError(null)
      setLoading(true)
      setNumPages(0)
      pageMapRef.current.clear()
      if (scrollHostRef.current) scrollHostRef.current.scrollTop = 0

      if (!src) {
        setLoading(false)
        setError('No PDF source (src) provided.')
        return
      }

      try {
        // Dynamically load pdf.js and the worker setup so heavy code is only
        // downloaded when the PDF viewer is actually used.
        const [pdfjsModule] = await Promise.all([import('pdfjs-dist'), import('../lib/pdfjs-setup')])
        const { getDocument, version: pdfjsVersion } = pdfjsModule

        // If the PDF source is protected by Authorization, fetch it with the
        // bearer token and pass the ArrayBuffer to pdf.js via `data`. That lets
        // us include headers (pdf.js cannot add custom headers to its internal
        // URL fetches).
        try {
          // Use centralized getter so tests or AuthContext can provide the
          // token. This avoids ad-hoc localStorage reads across the codebase.
          const { getAuthToken } = await import('@/lib/api')
          const token = getAuthToken()
          if (token) {
            const res = await fetch(src, { headers: { Authorization: `Bearer ${token}` } })
            // Log helpful diagnostics for aborted/403/401 responses
            console.debug('[PdfViewer] fetched PDF', { url: src, status: res.status, contentType: res.headers.get('content-type'), pdfjsVersion })
            if (!res.ok) {
              const text = await res.text().catch(() => '')
              console.error('[PdfViewer] PDF fetch returned non-ok status', res.status, text)
              throw new Error(`PDF fetch failed: ${res.status}`)
            }
            const buf = await res.arrayBuffer()
            console.debug('[PdfViewer] fetched buffer length', buf?.byteLength)
            task = getDocument({ data: buf })
          } else {
            console.debug('[PdfViewer] no auth token; loading PDF by URL', src)
            task = getDocument({ url: src })
          }
        } catch (fetchErr) {
          // fallback to url-based load if the authenticated fetch fails for any reason
          console.warn('[PdfViewer] authenticated fetch failed, falling back to URL load', fetchErr)
          task = getDocument({ url: src })
        }
        const pdf = await task.promise
        if (cancelled) return

  pdfRef.current = pdf
  setNumPages(pdf.numPages)
  numPagesRef.current = pdf.numPages
  console.debug('[PdfViewer] PDF loaded', { numPages: pdf.numPages })

        onReadyRef.current && onReadyRef.current({ scrollToPage, showHighlight })
      } catch (e) {
        if (cancelled) return
        // Add more diagnostics for aborted BodyStreamBuffer errors
        try {
          console.error('[PdfViewer] load failed:', {
            message: e?.message,
            name: e?.name,
            stack: e?.stack,
            src,
            pdfjsVersion: typeof pdfjsVersion !== 'undefined' ? pdfjsVersion : 'unknown',
            numPages: numPagesRef.current || 0,
          })
        } catch (logErr) {
          console.error('[PdfViewer] error logging failed', logErr)
        }

        // Retry once for transient BodyStreamBuffer aborts (pdf.js internal stream abort)
        const msg = String(e?.message || '')
        if (!bodyAbortRetryRef.current && /BodyStreamBuffer was aborted/i.test(msg)) {
          bodyAbortRetryRef.current = true
          console.debug('[PdfViewer] detected BodyStreamBuffer abort — retrying load once')
          // small delay before retrying
          setTimeout(() => {
            try {
              // Trigger a non-destructive retry by toggling `loadToggle`. This
              // re-runs the PDF loading effect without reloading the whole page.
              setLoading(false)
              setError(null)
              setLoadToggle(prev => prev + 1)
              console.debug('[PdfViewer] scheduled non-reload retry (loadToggle incremented)')
            } catch (retryErr) {
              console.error('[PdfViewer] retry attempt failed', retryErr)
              setError(e?.message || 'Failed to load PDF.')
            }
          }, 300)
          return
        }

        setError(e?.message || 'Failed to load PDF.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => {
      cancelled = true
      // Do NOT call `task.destroy()` here when using a shared pdf.js worker instance
      // (we set GlobalWorkerOptions.workerPort in pdfjs-setup). Destroying the
      // loading task can mark the shared worker as pending-destroy and cause
      // subsequent `PDFWorker.create` calls to fail with
      // "the worker is being destroyed". Instead, clear the chain and allow
      // the shared worker to live for the lifetime of the page.
      destroyChainRef.current = Promise.resolve()
    }
  }, [src, scrollToPage, showHighlight, loadToggle])

  // --- lazy render when visible ---
  useEffect(() => {
    const host = scrollHostRef.current
    if (!host || !numPages) return

    const io = new IntersectionObserver(
      entries => {
        for (const e of entries) {
          if (e.isIntersecting) {
            const n = Number(e.target.getAttribute('data-page'))
            if (n) ensureRendered(n)
          }
        }
      },
      { root: host, rootMargin: '200px 0px 400px 0px', threshold: 0.01 }
    )

    for (let n = 1; n <= numPages; n++) {
      const rec = pageMapRef.current.get(n)
      if (rec?.container) io.observe(rec.container)
    }

    return () => io.disconnect()
  }, [numPages, ensureRendered])

  // --- keep fit-to-width on resize ---
  useEffect(() => {
    if (!fitToWidth) return
    const host = scrollHostRef.current
    if (!host) return
    let raf = 0
    const ro = new ResizeObserver(() => {
      cancelAnimationFrame(raf)
      raf = requestAnimationFrame(async () => {
        const max = numPagesRef.current
        for (let n = 1; n <= max; n++) {
          const rec = pageMapRef.current.get(n)
          if (rec?.rendered) {
            rec.rendered = false
            pageMapRef.current.set(n, rec)
            await renderPageRef.current(n)
          }
        }
      })
    })
    ro.observe(host)
    return () => {
      cancelAnimationFrame(raf)
      ro.disconnect()
    }
  }, [fitToWidth])

  // --- shells ---
  const pages = []
  for (let i = 1; i <= (numPages || 0); i++) {
    pages.push(
      <div
        key={i}
        data-page={i}
        ref={el => setContainer(i, el)}
        className="pdf-page"
        style={{
          position: 'relative',
          margin: '0 auto 12px auto',
          background: '#fff',
          boxShadow: '0 1px 4px rgba(0,0,0,0.1)',
          width: 'fit-content',
        }}
      >
        <canvas ref={el => setCanvas(i, el)} style={{ display: 'block' }} />
        <div
          ref={el => setOverlay(i, el)}
          className="pdf-flash"
          style={{
            position: 'absolute',
            inset: 0,
            pointerEvents: 'none',
            // Keep overlay present for future bbox highlights, but don't fill the
            // whole page. Use transparent background by default.
            background: 'transparent',
            opacity: 0,
            transition: 'opacity 700ms ease',
            borderRadius: 4,
            zIndex: 1,
          }}
        />
      </div>
    )
  }

  return (
    <div
      ref={scrollHostRef}
      className="pdf-scroll-host"
      style={{
        width: '100%',
        height: '100%',
        overflow: 'auto',
        background: '#f5f7fb',
        padding: '16px 0',
      }}
    >
      {error ? (
        <div style={{ color: '#b00020', textAlign: 'center', paddingTop: 24 }}>
          <div style={{ fontWeight: 600, marginBottom: 8 }}>PDF load error</div>
          <div>{String(error)}</div>
        </div>
      ) : pages.length > 0 ? (
        pages
      ) : (
        <div style={{ color: '#666', textAlign: 'center', paddingTop: 24 }}>
          {loading ? 'Loading PDF…' : 'No pages to display.'}
        </div>
      )}
      <style>{`
        .pdf-flash.pdf-flash-show{ opacity: 1; }
        /* subtle focus style applied to the page container when jumping */
        .pdf-page-focus{
          transition: box-shadow 220ms ease, border-color 220ms ease, transform 220ms ease;
          box-shadow: 0 8px 20px rgba(2,6,23,0.06), 0 2px 6px rgba(2,6,23,0.04);
          border: 1px solid rgba(2,6,23,0.06);
          transform: translateY(-2px);
          border-radius: 6px;
        }
      `}</style>
    </div>
  )
}
