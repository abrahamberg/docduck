import {
  ProviderInfo,
  QueryRequest,
  QueryResponse,
  HealthStatus,
  DocumentResult,
  ChatStreamUpdate,
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://localhost:5000';

async function http<T>(path: string, options: RequestInit = {}): Promise<T> {
  const resp = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  });
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`Request failed ${resp.status}: ${text}`);
  }
  return resp.json();
}

export async function getProviders(): Promise<ProviderInfo[]> {
  const data = await http<{ providers: ProviderInfo[] }>(`/providers`);
  return data.providers;
}

// Unified query endpoint - handles both simple and streaming modes
export async function postQuery(req: QueryRequest): Promise<QueryResponse> {
  return http<QueryResponse>(`/query`, { method: 'POST', body: JSON.stringify(req) });
}

// Streaming version of query - for showing intermediate thinking steps
export async function postQueryStream(
  req: QueryRequest,
  onUpdate: (update: ChatStreamUpdate) => void
): Promise<void> {
  const resp = await fetchStreamingQuery(req);
  await processStreamResponse(resp, onUpdate);
}

async function fetchStreamingQuery(req: QueryRequest): Promise<Response> {
  const resp = await fetch(`${API_BASE}/query`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'text/event-stream',
    },
    body: JSON.stringify({ ...req, streamSteps: true }),
  });

  if (!resp.ok || !resp.body) {
    const text = await resp.text().catch(() => '');
    throw new Error(`Request failed ${resp.status}: ${text}`);
  }

  return resp;
}

async function processStreamResponse(
  resp: Response,
  onUpdate: (update: ChatStreamUpdate) => void
): Promise<void> {
  if (!resp.body) return;

  const reader = resp.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let shouldStop = false;

  while (!shouldStop) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    shouldStop = processEventBuffer(buffer, onUpdate, (newBuffer) => {
      buffer = newBuffer;
    });

    if (shouldStop) break;
  }

  processRemainingBuffer(buffer, onUpdate);
}

function processEventBuffer(
  buffer: string,
  onUpdate: (update: ChatStreamUpdate) => void,
  updateBuffer: (newBuffer: string) => void
): boolean {
  let shouldStop = false;
  let currentBuffer = buffer;

  let boundary = currentBuffer.indexOf('\n\n');
  while (boundary !== -1) {
    const rawEvent = currentBuffer.slice(0, boundary).trim();
    currentBuffer = currentBuffer.slice(boundary + 2);

    if (processRawEvent(rawEvent, onUpdate)) {
      shouldStop = true;
    }

    boundary = currentBuffer.indexOf('\n\n');
  }

  updateBuffer(currentBuffer);
  return shouldStop;
}

function processRawEvent(rawEvent: string, onUpdate: (update: ChatStreamUpdate) => void): boolean {
  for (const line of rawEvent.split('\n')) {
    if (line.startsWith('data: ')) {
      const json = line.slice(6);
      if (json) {
        const payload = JSON.parse(json) as ChatStreamUpdate;
        onUpdate(payload);
        if (payload.type === 'final' || payload.type === 'error') {
          return true;
        }
      }
    }
  }
  return false;
}

function processRemainingBuffer(
  buffer: string,
  onUpdate: (update: ChatStreamUpdate) => void
): void {
  if (buffer.trim().length === 0) return;

  const trailingLine = buffer
    .trim()
    .split('\n')
    .find((line) => line.startsWith('data: '));
  if (trailingLine) {
    const payload = JSON.parse(trailingLine.slice(6)) as ChatStreamUpdate;
    onUpdate(payload);
  }
}

export async function postDocSearch(
  req: QueryRequest
): Promise<{ query: string; count: number; results: DocumentResult[] }> {
  return http(`/docsearch`, { method: 'POST', body: JSON.stringify(req) });
}

export async function getHealth(): Promise<HealthStatus> {
  return http<HealthStatus>(`/health`);
}
