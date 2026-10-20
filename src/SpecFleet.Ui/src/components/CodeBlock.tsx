/**
 * Readable code preview with syntax highlighting when the language is recognized.
 * Highlighting is applied only when highlight.js knows the language; otherwise the raw
 * text is shown, so generated content is never silently mangled.
 */

import { useEffect, useMemo, useRef } from 'react'
import hljs from 'highlight.js/lib/common'
import 'highlight.js/styles/github-dark.css'

export interface CodeBlockProps {
  code: string
  language: string | null
  testId?: string
}

export function CodeBlock({ code, language, testId }: CodeBlockProps) {
  const codeRef = useRef<HTMLElement>(null)

  const normalizedLanguage = useMemo(() => {
    if (!language) return null
    const candidate = language.toLowerCase()
    return hljs.getLanguage(candidate) ? candidate : null
  }, [language])

  useEffect(() => {
    const element = codeRef.current
    if (!element) return
    element.removeAttribute('data-highlighted')
    element.className = normalizedLanguage ? `language-${normalizedLanguage}` : ''
    if (normalizedLanguage) {
      hljs.highlightElement(element)
    }
  }, [code, normalizedLanguage])

  return (
    <pre className="code-block" data-testid={testId} data-language={normalizedLanguage ?? 'plain'}>
      <code ref={codeRef}>{code}</code>
    </pre>
  )
}
