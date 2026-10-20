/**
 * Typed client for the SpecFleet API. The dashboard talks to the API over authenticated
 * HTTP and streaming (Server-Sent Events). The client never receives provider credentials;
 * it only sees the answers and previews the server chooses to return.
 */

import type { SpecFleetConfig } from './config'

export interface ConversationSummary {
  conversationId: string
  title: string
  createdAt: string
}

export interface ChatMessageView {
  messageId: string
  conversationId: string
  role: string
  content: string
  status: string
  createdAt: string
  updatedAt: string
  error: string | null
}

export interface ArtifactValidationFailure {
  rule: string
  message: string
}

export interface ArtifactPreviewView {
  artifactId: string
  artifactType: string
  proposedPath: string
  content: string
  language: string | null
  previewDigest: string
  isValid: boolean
  validationFailures: ArtifactValidationFailure[]
}

export type ChatStreamEvent =
  | { kind: 'Delta'; text: string | null; messageId: string; artifact: ArtifactPreviewView | null; error: string | null }
  | { kind: 'Completed'; text: string | null; messageId: string; artifact: ArtifactPreviewView | null; error: string | null }
  | { kind: 'Failed'; text: string | null; messageId: string; artifact: ArtifactPreviewView | null; error: string | null }
  | { kind: 'Cancelled'; text: string | null; messageId: string; artifact: ArtifactPreviewView | null; error: string | null }
  | { kind: 'Artifact'; text: string | null; messageId: string; artifact: ArtifactPreviewView | null; error: string | null }

export interface OpenSpecOperationResult {
  succeeded: boolean
  operation: string
  stateChanging: boolean
  previewRequired: boolean
  output: string
  error: string | null
}

export interface WorkerTaskStatus {
  taskId: string
  taskType: string
  status: string
  attempts?: number
  error?: string | null
}

export interface WorkerTaskPreview {
  taskType: string
  payload: string
  payloadDigest: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/** Reads the ProblemDetails `detail` field the API returns for expected failures. */
async function toApiError(response: Response): Promise<ApiError> {
  let detail = `Request failed with status ${response.status}`
  try {
    const body = await response.json()
    detail = body?.detail ?? body?.title ?? detail
  } catch {
    // Non-JSON error body; keep the default message.
  }
  return new ApiError(response.status, detail)
}

export class SpecFleetClient {
  private readonly config: SpecFleetConfig

  constructor(config: SpecFleetConfig) {
    this.config = config
  }

  private url(path: string): string {
    return `${this.config.apiBaseUrl}${path}`
  }

  private headers(extra: Record<string, string> = {}): Record<string, string> {
    return {
      Authorization: `Bearer ${this.config.accessToken}`,
      ...extra,
    }
  }

  async startConversation(title: string): Promise<ConversationSummary> {
    const response = await fetch(this.url('/api/chat/conversations'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ title }),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as ConversationSummary
  }

  async listMessages(conversationId: string): Promise<ChatMessageView[]> {
    const response = await fetch(this.url(`/api/chat/conversations/${conversationId}/messages`), {
      headers: this.headers(),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as ChatMessageView[]
  }

  async previewArtifact(artifactType: string, content: string): Promise<ArtifactPreviewView> {
    const response = await fetch(this.url('/api/chat/artifacts/preview'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ artifactType, content }),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as ArtifactPreviewView
  }

  async applyArtifact(
    preview: ArtifactPreviewView,
    target: string,
    decision: 'Confirmed' | 'Declined',
  ): Promise<{ decision: string; externalChangeSubmitted: boolean; target: string }> {
    const response = await fetch(this.url('/api/chat/artifacts/apply'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({
        artifactId: preview.artifactId,
        artifactType: preview.artifactType,
        target,
        content: preview.content,
        previewDigest: preview.previewDigest,
        decision,
      }),
    })
    if (!response.ok) throw await toApiError(response)
    return await response.json()
  }

  async runOpenSpecOperation(
    operation: string,
    workspacePath: string,
    target: string,
    confirmed: boolean,
  ): Promise<OpenSpecOperationResult> {
    const response = await fetch(this.url('/api/orchestration/openspec/operations'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ operation, workspacePath, target, confirmed }),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as OpenSpecOperationResult
  }

  async previewWorkerTask(taskType: string, payload: string): Promise<WorkerTaskPreview> {
    // Preview is validated server-side by reusing the dispatch validation path through the
    // payload validator exposed on the worker task endpoint contract.
    const response = await fetch(this.url('/api/orchestration/worker-tasks/preview'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ taskType, payload }),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as WorkerTaskPreview
  }

  async dispatchWorkerTask(
    taskType: string,
    payload: string,
    payloadDigest: string,
    approvalReference: string,
    target: string,
  ): Promise<WorkerTaskStatus> {
    const response = await fetch(this.url('/api/orchestration/worker-tasks'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ taskType, payload, payloadDigest, approvalReference, target }),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as WorkerTaskStatus
  }

  async getWorkerTaskStatus(taskId: string): Promise<WorkerTaskStatus> {
    const response = await fetch(this.url(`/api/orchestration/worker-tasks/${taskId}`), {
      headers: this.headers(),
    })
    if (!response.ok) throw await toApiError(response)
    return (await response.json()) as WorkerTaskStatus
  }

  /**
   * Streams a chat answer as Server-Sent Events. Yields parsed events in arrival order so
   * the caller can render deltas incrementally and react to completion, failure, or
   * cancellation. Pass an AbortSignal to cancel an in-progress response.
   */
  async *streamAnswer(
    conversationId: string,
    prompt: string,
    artifactType: string | null,
    signal?: AbortSignal,
  ): AsyncGenerator<ChatStreamEvent> {
    const response = await fetch(this.url('/api/chat/ask'), {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json', Accept: 'text/event-stream' }),
      body: JSON.stringify({
        conversationId,
        ownerId: '',
        prompt,
        artifactType,
      }),
      signal,
    })

    if (!response.ok) throw await toApiError(response)
    if (!response.body) throw new ApiError(response.status, 'The response contained no stream body.')

    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    try {
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })

        let boundary = buffer.indexOf('\n\n')
        while (boundary !== -1) {
          const raw = buffer.slice(0, boundary)
          buffer = buffer.slice(boundary + 2)
          const data = raw
            .split('\n')
            .filter((line) => line.startsWith('data:'))
            .map((line) => line.slice(5).trimStart())
            .join('\n')
          if (data.length > 0) {
            yield JSON.parse(data) as ChatStreamEvent
          }
          boundary = buffer.indexOf('\n\n')
        }
      }
    } finally {
      reader.releaseLock()
    }
  }
}
