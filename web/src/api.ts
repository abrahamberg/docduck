import { ChatRequest, ChatResponse, ProviderInfo, QueryRequest, QueryResponse, HealthStatus } from './types';

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

export async function postChat(req: ChatRequest): Promise<ChatResponse> {
  return http<ChatResponse>(`/chat`, { method: 'POST', body: JSON.stringify(req) });
}

export async function getHealth(): Promise<HealthStatus> {
  return http<HealthStatus>(`/health`);
}
