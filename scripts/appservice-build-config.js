export function validateAppServiceBuild({ command, mode, env }) {
  if (command !== 'build' || mode !== 'appservice') return

  if (String(env.VITE_AUTH_MODE || '').trim().toLowerCase() !== 'bff') {
    throw new Error('The Azure App Service build requires VITE_AUTH_MODE=bff.')
  }

  if (String(env.VITE_API_BASE || '').trim()) {
    throw new Error(
      'The Azure App Service BFF build must not set VITE_API_BASE; API requests must remain same-origin.',
    )
  }
}
