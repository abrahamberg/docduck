export interface Source {
  docId: string;
  filename: string;
  chunkNum: number;
  text: string;
  distance: number;
  citation: string;
  providerType?: string | null;
  providerName?: string | null;
}

export interface QueryRequest {
  question: string;
  topK?: number;
  providerNames?: string[];
  searchDepth?: number;
}

export interface QueryResponse {
  answer: string;
  sources: Source[];
  tokensUsed: number;
}

export interface DocumentResult {
  docId: string;
  filename: string;
  address: string;
  distance: number;
  text: string;
  providerType?: string | null;
  providerName?: string | null;
}

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  message: string;
  history?: ChatMessage[];
  topK?: number;
  providerNames?: string[];
  streamSteps?: boolean;
  searchDepth?: number;
}

export interface ChatResponse {
  answer: string;
  steps: string[];
  files: DocumentResult[];
  sources: Source[];
  tokensUsed: number;
  history: ChatMessage[];
}

export interface ChatStreamUpdate {
  type: 'step' | 'final' | 'error';
  message?: string | null;
  files?: DocumentResult[] | null;
  final?: ChatResponse | null;
}

// Persistent chat types removed - UI uses independent `QueryRequest`/`QueryResponse` for single-question flows.

export interface ProviderInfo {
  providerType: string;
  providerName: string;
  isEnabled: boolean;
  registeredAt: string;
  lastSyncAt?: string | null;
  metadata?: Record<string, string> | null;
}

export interface HealthStatus {
  status: string;
  timestamp: string;
  chunks: number;
  documents: number;
  openAiKeyPresent: boolean;
  dbConnectionPresent: boolean;
}
