import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, SpecFleetClient } from './api'
import type {
  ArtifactPreviewView,
  ChatMessageView,
  ConversationSummary,
  OpenSpecOperationResult,
  WorkerTaskStatus,
} from './api'
import { CodeBlock } from './components/CodeBlock'
import { readConfig } from './config'
import './App.css'

const ARTIFACT_TYPES = [
  'Code',
  'Documentation',
  'Worker',
  'TerraformModule',
  'CloudflareTunnelConfig',
  'RabbitMqConfig',
  'OpenSpecProposal',
] as const

const WORKER_TASK_TYPES = [
  'GenerateWorker',
  'GenerateUiPage',
  'GenerateTerraformModule',
  'GenerateCloudflareTunnelConfig',
  'GenerateRabbitMqConfig',
  'GenerateControlPlaneLogic',
] as const

const OPEN_SPEC_OPERATIONS = ['List', 'Show', 'Validate', 'New', 'Apply', 'Archive'] as const
const READ_ONLY_OPERATIONS: readonly string[] = ['List', 'Show', 'Validate']

type StreamState = 'idle' | 'streaming' | 'completed' | 'failed' | 'cancelled'

interface PendingConfirmation {
  preview: ArtifactPreviewView
  target: string
}

function App() {
  const config = useMemo(() => readConfig(), [])
  const client = useMemo(() => new SpecFleetClient(config), [config])

  const [conversations, setConversations] = useState<ConversationSummary[]>([])
  const [activeConversationId, setActiveConversationId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessageView[]>([])
  const [draft, setDraft] = useState('')
  const [streamingText, setStreamingText] = useState('')
  const [streamState, setStreamState] = useState<StreamState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [artifactType, setArtifactType] = useState<string>('')
  const [pending, setPending] = useState<PendingConfirmation | null>(null)

  const abortRef = useRef<AbortController | null>(null)

  const refreshMessages = useCallback(
    async (conversationId: string) => {
      try {
        setMessages(await client.listMessages(conversationId))
      } catch (caught) {
        setError(caught instanceof ApiError ? caught.message : String(caught))
      }
    },
    [client],
  )

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const conversation = await client.startConversation('SpecFleet session')
        if (cancelled) return
        setConversations([conversation])
        setActiveConversationId(conversation.conversationId)
      } catch (caught) {
        if (!cancelled) setError(caught instanceof ApiError ? caught.message : String(caught))
      }
    })()
    return () => {
      cancelled = true
    }
  }, [client])

  const submitPrompt = useCallback(async () => {
    if (!activeConversationId || draft.trim().length === 0 || streamState === 'streaming') return

    const prompt = draft
    setDraft('')
    setError(null)
    setStreamingText('')
    setStreamState('streaming')

    const controller = new AbortController()
    abortRef.current = controller

    try {
      for await (const event of client.streamAnswer(
        activeConversationId,
        prompt,
        artifactType || null,
        controller.signal,
      )) {
        if (event.kind === 'Delta' && event.text) {
          setStreamingText((current) => current + event.text)
        } else if (event.kind === 'Artifact' && event.artifact) {
          setPending({ preview: event.artifact, target: event.artifact.proposedPath })
        } else if (event.kind === 'Completed') {
          setStreamState('completed')
        } else if (event.kind === 'Failed') {
          setStreamState('failed')
          setError(event.error ?? 'The AI provider could not complete the request.')
        } else if (event.kind === 'Cancelled') {
          setStreamState('cancelled')
        }
      }
    } catch (caught) {
      if (controller.signal.aborted) {
        setStreamState('cancelled')
      } else {
        setStreamState('failed')
        setError(caught instanceof ApiError ? caught.message : String(caught))
      }
    } finally {
      abortRef.current = null
      await refreshMessages(activeConversationId)
    }
  }, [activeConversationId, artifactType, client, draft, refreshMessages, streamState])

  const cancelResponse = useCallback(() => {
    abortRef.current?.abort()
    setStreamState('cancelled')
  }, [])

  const resolveConfirmation = useCallback(
    async (decision: 'Confirmed' | 'Declined') => {
      if (!pending) return
      try {
        await client.applyArtifact(pending.preview, pending.target, decision)
      } catch (caught) {
        setError(caught instanceof ApiError ? caught.message : String(caught))
      } finally {
        setPending(null)
      }
    },
    [client, pending],
  )

  const disabled = !activeConversationId

  return (
    <div className="app">
      <header className="app-header">
        <h1>SpecFleet dashboard</h1>
        <p className="subtitle">
          Authenticated AI chat, artifact review, OpenSpec operations, and worker task dispatch.
        </p>
        {config.apiBaseUrl === '' ? (
          <p className="config-warning" role="status" data-testid="config-warning">
            No API base URL configured. Set <code>window.__SPECFLEET_CONFIG__.apiBaseUrl</code> or
            pass <code>?apiBase=</code>.
          </p>
        ) : null}
      </header>

      {error ? (
        <div className="alert" role="alert" data-testid="chat-error">
          {error}
        </div>
      ) : null}

      <div className="layout">
        <section className="panel chat-panel" aria-label="Conversation">
          <h2>Conversation</h2>

          <ul className="conversations" data-testid="conversation-list">
            {conversations.map((conversation) => (
              <li
                key={conversation.conversationId}
                className={conversation.conversationId === activeConversationId ? 'active' : ''}
              >
                {conversation.title}
              </li>
            ))}
          </ul>

          <ul className="history" data-testid="message-history">
            {messages.map((message) => (
              <li key={message.messageId} className={`message message-${message.role.toLowerCase()}`}>
                <span className="role">{message.role}</span>
                <span className="status" data-status={message.status}>
                  {message.status}
                </span>
                <p className="content">{message.content}</p>
                {message.error ? <p className="message-error">{message.error}</p> : null}
              </li>
            ))}
          </ul>

          {streamingText.length > 0 || streamState === 'streaming' ? (
            <div className="streaming" data-testid="streaming-response" data-state={streamState}>
              <span className="role">Assistant</span>
              <p className="content">{streamingText}</p>
              <span className="stream-state">{streamState}</span>
            </div>
          ) : null}

          <form
            className="composer"
            onSubmit={(event) => {
              event.preventDefault()
              void submitPrompt()
            }}
          >
            <label htmlFor="artifact-type">Artifact type</label>
            <select
              id="artifact-type"
              data-testid="artifact-type"
              value={artifactType}
              onChange={(event) => setArtifactType(event.target.value)}
            >
              <option value="">Plain answer</option>
              {ARTIFACT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>

            <label htmlFor="prompt">Prompt</label>
            <textarea
              id="prompt"
              data-testid="prompt"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              rows={3}
            />

            <div className="composer-actions">
              <button type="submit" data-testid="send" disabled={disabled || streamState === 'streaming'}>
                Send
              </button>
              <button
                type="button"
                data-testid="cancel"
                onClick={cancelResponse}
                disabled={streamState !== 'streaming'}
              >
                Stop
              </button>
            </div>
          </form>
        </section>

        <ArtifactPanel pending={pending} onResolve={resolveConfirmation} />

        <OrchestrationPanel client={client} onError={setError} />
      </div>
    </div>
  )
}

function ArtifactPanel({
  pending,
  onResolve,
}: {
  pending: PendingConfirmation | null
  onResolve: (decision: 'Confirmed' | 'Declined') => void
}) {
  if (!pending) {
    return (
      <section className="panel artifact-panel" aria-label="Artifact review">
        <h2>Artifact review</h2>
        <p className="empty" data-testid="no-artifact">
          No artifact preview is awaiting confirmation.
        </p>
      </section>
    )
  }

  const { preview, target } = pending

  return (
    <section className="panel artifact-panel" aria-label="Artifact review">
      <h2>Artifact review</h2>
      <dl className="artifact-meta">
        <dt>Type</dt>
        <dd data-testid="artifact-type-label">{preview.artifactType}</dd>
        <dt>Proposed path</dt>
        <dd data-testid="artifact-path">{preview.proposedPath}</dd>
        <dt>Target</dt>
        <dd data-testid="artifact-target">{target}</dd>
        <dt>Validation</dt>
        <dd data-testid="artifact-valid">{preview.isValid ? 'valid' : 'invalid'}</dd>
      </dl>

      {preview.validationFailures.length > 0 ? (
        <ul className="validation-failures" data-testid="validation-failures">
          {preview.validationFailures.map((failure) => (
            <li key={`${failure.rule}:${failure.message}`}>
              <strong>{failure.rule}</strong>: {failure.message}
            </li>
          ))}
        </ul>
      ) : null}

      <CodeBlock code={preview.content} language={preview.language} testId="artifact-code" />

      {preview.isValid ? (
        <div className="confirm-actions">
          <button type="button" data-testid="confirm-apply" onClick={() => onResolve('Confirmed')}>
            Confirm and apply
          </button>
          <button type="button" data-testid="decline-apply" onClick={() => onResolve('Declined')}>
            Decline
          </button>
        </div>
      ) : (
        <p className="empty" data-testid="not-applyable">
          This content failed validation and cannot be applied.
        </p>
      )}
    </section>
  )
}

function OrchestrationPanel({
  client,
  onError,
}: {
  client: SpecFleetClient
  onError: (message: string | null) => void
}) {
  const [operation, setOperation] = useState<string>('List')
  const [workspace, setWorkspace] = useState('')
  const [target, setTarget] = useState('')
  const [result, setResult] = useState<OpenSpecOperationResult | null>(null)
  const [awaitingConfirmation, setAwaitingConfirmation] = useState(false)

  const [taskType, setTaskType] = useState<string>('GenerateWorker')
  const [payload, setPayload] = useState('{"name":"example","language":"csharp"}')
  const [taskStatus, setTaskStatus] = useState<WorkerTaskStatus | null>(null)

  const runOperation = useCallback(
    async (confirmed: boolean) => {
      onError(null)
      try {
        const outcome = await client.runOpenSpecOperation(operation, workspace, target, confirmed)
        setResult(outcome)
        setAwaitingConfirmation(outcome.previewRequired && !outcome.stateChanging)
      } catch (caught) {
        setAwaitingConfirmation(false)
        onError(caught instanceof ApiError ? caught.message : String(caught))
      }
    },
    [client, onError, operation, target, workspace],
  )

  const dispatchTask = useCallback(async () => {
    onError(null)
    try {
      const preview = await client.previewWorkerTask(taskType, payload)
      const status = await client.dispatchWorkerTask(
        taskType,
        preview.payload,
        preview.payloadDigest,
        `ui-${preview.payloadDigest.slice(0, 12)}`,
        taskType,
      )
      setTaskStatus(status)
    } catch (caught) {
      onError(caught instanceof ApiError ? caught.message : String(caught))
    }
  }, [client, onError, payload, taskType])

  const refreshStatus = useCallback(async () => {
    if (!taskStatus) return
    try {
      setTaskStatus(await client.getWorkerTaskStatus(taskStatus.taskId))
    } catch (caught) {
      onError(caught instanceof ApiError ? caught.message : String(caught))
    }
  }, [client, onError, taskStatus])

  const stateChanging = !READ_ONLY_OPERATIONS.includes(operation)

  return (
    <section className="panel orchestration-panel" aria-label="OpenSpec and workers">
      <h2>OpenSpec operations</h2>
      <div className="field">
        <label htmlFor="openspec-operation">Operation</label>
        <select
          id="openspec-operation"
          data-testid="openspec-operation"
          value={operation}
          onChange={(event) => {
            setOperation(event.target.value)
            setAwaitingConfirmation(false)
          }}
        >
          {OPEN_SPEC_OPERATIONS.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="openspec-workspace">Workspace path</label>
        <input
          id="openspec-workspace"
          data-testid="openspec-workspace"
          value={workspace}
          onChange={(event) => setWorkspace(event.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="openspec-target">Target</label>
        <input
          id="openspec-target"
          data-testid="openspec-target"
          value={target}
          onChange={(event) => setTarget(event.target.value)}
        />
      </div>

      <div className="confirm-actions">
        <button type="button" data-testid="openspec-run" onClick={() => void runOperation(false)}>
          {stateChanging ? 'Preview operation' : 'Run operation'}
        </button>
        {stateChanging ? (
          <button
            type="button"
            data-testid="openspec-confirm"
            onClick={() => void runOperation(true)}
            disabled={!awaitingConfirmation}
          >
            Confirm and run
          </button>
        ) : null}
      </div>

      {result ? (
        <div className="operation-result" data-testid="openspec-result">
          <p>
            <strong>{result.operation}</strong> {result.succeeded ? 'succeeded' : 'failed'}
            {result.previewRequired ? ' (confirmation required)' : ''}
          </p>
          <CodeBlock code={result.output || result.error || ''} language="text" testId="openspec-output" />
        </div>
      ) : null}

      <h2>Worker tasks</h2>
      <div className="field">
        <label htmlFor="worker-task-type">Task type</label>
        <select
          id="worker-task-type"
          data-testid="worker-task-type"
          value={taskType}
          onChange={(event) => setTaskType(event.target.value)}
        >
          {WORKER_TASK_TYPES.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="worker-payload">Payload (JSON)</label>
        <textarea
          id="worker-payload"
          data-testid="worker-payload"
          value={payload}
          onChange={(event) => setPayload(event.target.value)}
          rows={3}
        />
      </div>

      <div className="confirm-actions">
        <button type="button" data-testid="worker-dispatch" onClick={() => void dispatchTask()}>
          Dispatch task
        </button>
        <button
          type="button"
          data-testid="worker-refresh"
          onClick={() => void refreshStatus()}
          disabled={!taskStatus}
        >
          Refresh status
        </button>
      </div>

      {taskStatus ? (
        <p className="task-status" data-testid="worker-status" data-status={taskStatus.status}>
          Task {taskStatus.taskId} is {taskStatus.status}
          {taskStatus.status === 'Queued' || taskStatus.status === 'Leased' ? ' (in progress)' : ''}
        </p>
      ) : null}
    </section>
  )
}

export default App
