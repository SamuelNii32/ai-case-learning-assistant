// Development-only fetch sniffer
// Wraps window.fetch to log whether Authorization header is present and notes 401 responses.
// Safe to remove before shipping; only loaded in dev mode from main.jsx.

(function () {
  if (typeof window === 'undefined') return

  window.__devFetchLogs = window.__devFetchLogs || []

  const origFetch = window.fetch.bind(window)
  window.fetch = async function (input, init = {}) {
    try {
      let authHeader = undefined
      if (init && init.headers) {
        if (typeof init.headers.get === 'function') {
          authHeader = init.headers.get('Authorization')
        } else if (typeof init.headers === 'object') {
          authHeader = init.headers.Authorization || init.headers.authorization
        }
      }
      // If no header on init, some apps set headers in interceptors; attempt to read from localStorage token safely
      if (!authHeader && window.localStorage) {
        const maybe = window.localStorage.getItem('authToken')
        if (maybe) authHeader = `Bearer ${maybe}`
      }

      const meta = {
        url: (typeof input === 'string' ? input : (input && input.url) || '<request>'),
        hasAuth: !!authHeader,
        time: Date.now(),
      }
      // keep bounded history
      window.__devFetchLogs.unshift(meta)
      if (window.__devFetchLogs.length > 200) window.__devFetchLogs.pop()

      const res = await origFetch(input, init)
      if (res && res.status === 401) {
        console.warn('[devFetchSniffer] fetch -> 401 for', meta.url, 'hasAuth=', meta.hasAuth)
        window.__devFetchLogs.unshift(Object.assign({}, meta, { status: 401 }))
      }
      return res
    } catch (err) {
      console.error('[devFetchSniffer] fetch error', err)
      throw err
    }
  }

  console.info('[devFetchSniffer] installed: window.__devFetchLogs available; wraps window.fetch')
})()
