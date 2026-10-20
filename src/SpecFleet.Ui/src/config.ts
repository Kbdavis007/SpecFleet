/**
 * Runtime configuration for the independently deployed dashboard.
 *
 * The UI is built once and hosted separately from the API, so the API origin and access
 * token are resolved at runtime (window.__SPECFLEET_CONFIG__) or from the page query
 * string. Nothing here is baked into the bundle at build time.
 */

export interface SpecFleetConfig {
  apiBaseUrl: string
  accessToken: string
}

declare global {
  interface Window {
    __SPECFLEET_CONFIG__?: Partial<SpecFleetConfig>
  }
}

export function readConfig(): SpecFleetConfig {
  const params = new URLSearchParams(window.location.search)
  const runtime = window.__SPECFLEET_CONFIG__ ?? {}

  return {
    apiBaseUrl: (params.get('apiBase') ?? runtime.apiBaseUrl ?? '').replace(/\/$/, ''),
    accessToken: params.get('token') ?? runtime.accessToken ?? '',
  }
}
