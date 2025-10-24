// src/components/PdfViewer.jsx
import React, { useEffect, useRef, useState, useCallback } from 'react'
import '../lib/pdfjs-setup' // must set GlobalWorkerOptions.workerPort
import { getDocument } from 'pdfjs-dist'

export default function PdfViewer({ src, onReady, initialScale = 1.5, fitToWidth = true }) {
  const scrollHostRef = useRef(null)
  const pdfRef = useRef(null) // PDFDocumentProxy
  const [numPages, setNumPages] = useState(0)
  const numPagesRef = useRef(0)
  const pageMapRef = useRef(new Map()) // pageNumber -> { container, canvas, overlay, rendered, scale, pageHeightPt }

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

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
      console.error('[PdfViewer] page.render failed for page', n, err)
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
      const el = rec?.overlay
      if (!el) return

      // make sure it’s above the canvas and will animate
      el.style.zIndex = '1'
      el.style.transition = el.style.transition || 'opacity 700ms ease'

      // fade in
      el.style.opacity = '1'

      // fade back out after a short delay
      setTimeout(() => {
        el.style.opacity = '0'
      }, 900)
    },
    [scrollToPage]
  )

  // --- load document (serialized) ---
  useEffect(() => {
    let cancelled = false
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
        task = getDocument({ url: src })
        const pdf = await task.promise
        if (cancelled) return

        pdfRef.current = pdf
        setNumPages(pdf.numPages)
        numPagesRef.current = pdf.numPages

        onReadyRef.current && onReadyRef.current({ scrollToPage, showHighlight })
      } catch (e) {
        if (cancelled) return
        console.error('[PdfViewer] load failed:', e)
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
  }, [src, scrollToPage, showHighlight])

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
            background: 'rgba(255, 213, 0, 0.45)',
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
      <style>{`.pdf-flash.pdf-flash-show{ opacity: 1; }`}</style>
    </div>
  )
}
