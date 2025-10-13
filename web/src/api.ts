import { ChatRequest, ChatResponse, ProviderInfo, QueryRequest, QueryResponse, HealthStatus, DocumentResult, ChatStreamUpdate } from './types';

const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://localhost:5000';

async function http<T>(path: string, options: RequestInit = {}): Promise<T> {
  const resp = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers || {}),
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

export async function postQuery(req: QueryRequest): Promise<QueryResponse> {
  return http<QueryResponse>(`/query`, { method: 'POST', body: JSON.stringify(req) });
}

export async function postDocSearch(req: QueryRequest): Promise<{ query: string; count: number; results: DocumentResult[] }> {
  return http(`/docsearch`, { method: 'POST', body: JSON.stringify(req) });
}

export async function postChat(req: ChatRequest): Promise<ChatResponse> {
  return http<ChatResponse>(`/chat`, { method: 'POST', body: JSON.stringify(req) });
}

export async function postChatStream(req: ChatRequest, onUpdate: (update: ChatStreamUpdate) => void): Promise<void> {
  const resp = await fetch(`${API_BASE}/chat`, {
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

  const reader = resp.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let shouldStop = false;

  while (true) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });

    let boundary = buffer.indexOf('\n\n');
    while (boundary !== -1) {
      const rawEvent = buffer.slice(0, boundary).trim();
      buffer = buffer.slice(boundary + 2);

      for (const line of rawEvent.split('\n')) {
        if (line.startsWith('data: ')) {
          const json = line.slice(6);
          if (json) {
            const payload = JSON.parse(json) as ChatStreamUpdate;
            onUpdate(payload);
            if (payload.type === 'final' || payload.type === 'error') {
              shouldStop = true;
            }
          }
        }
      }

      boundary = buffer.indexOf('\n\n');
    }

    if (shouldStop) {
      break;
    }
  }

  if (!shouldStop && buffer.trim().length > 0) {
    const trailingLine = buffer.trim().split('\n').find(line => line.startsWith('data: '));
    if (trailingLine) {
      const payload = JSON.parse(trailingLine.slice(6)) as ChatStreamUpdate;
      onUpdate(payload);
    }
  }
}

export async function getHealth(): Promise<HealthStatus> {
  return http<HealthStatus>(`/health`);
}
