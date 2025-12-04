// Small navigation helper so non-React modules can navigate using React Router's navigate
let _navigate = null

export function setNavigator(fn) {
  _navigate = fn
}

export function clearNavigator() {
  _navigate = null
}

export function navigateTo(path, options) {
  try {
    if (_navigate) {
      _navigate(path, options || {})
      return
    }
  } catch {
    /* fall through to window fallback */
  }

  if (typeof window !== 'undefined') {
    try {
      if (options && options.replace) window.location.replace(path)
      else window.location.assign(path)
    } catch {
      /* ignore */
    }
  }
}

export default navigateTo
