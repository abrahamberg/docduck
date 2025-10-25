export interface AdminUser {
  id: string;
  username: string;
  isAdmin: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface LoginResponse {
  token: string;
  user: AdminUser;
}

export interface ProviderSettings {
  providerType: string;
  providerName: string;
  enabled: boolean;
  updatedAt: string;
  settings: Record<string, unknown>;
}

export interface ProviderProbeDocument {
  documentId: string;
  filename: string;
  sizeBytes: number | null;
  mimeType: string | null;
  bytesRead: number;
}

export interface ProviderProbeResult {
  success: boolean;
  message: string;
  documents: ProviderProbeDocument[];
}

// New AI Configuration types
export interface AiModelAssignmentDto {
  id: string;
  displayName: string;
  modelId: string;
  baseUrl: string;
  apiKey: string;
  maxContextTokens: number;
  maxOutputTokens: number;
  supportsFunctionCalling: boolean;
  costFactor: number;
  enabled: boolean;
  customHeaders?: Record<string, string>;
  timeoutSeconds: number;
  testStatus?: number; // 0=Untested, 1=Passed, 2=Failed
  lastTestedAt?: string;
  lastTestMessage?: string;
}

export interface AiEmbeddingModelAssignmentDto {
  id: string;
  displayName: string;
  modelId: string;
  baseUrl: string;
  apiKey: string;
  dimensions: number;
  enabled: boolean;
  customHeaders?: Record<string, string>;
  timeoutSeconds: number;
  testStatus?: number;
  lastTestedAt?: string;
  lastTestMessage?: string;
}

export interface AiConfigurationDto {
  enabled: boolean;
  defaultSelectionStrategy: 'Eco' | 'Standard' | 'Turbo';
  
  // Model registry: all available models
  modelRegistry: AiModelAssignmentDto[];
  
  // Tier assignments by ID (optional)
  microModelId?: string;
  miniModelId?: string;
  fullModelId?: string;
  
  // Embedding registry and active selection
  embeddingRegistry: AiEmbeddingModelAssignmentDto[];
  activeEmbeddingModelId: string;
  
  defaultTemperature: number;
  refineSystemPrompt?: string;
}

export interface AiProbeRequest {
  modelAssignment: Omit<AiModelAssignmentDto, 'id' | 'displayName' | 'costFactor'>;
}

export interface AiProbeResponse {
  success: boolean;
  model?: string;
  error?: string;
  latencyMs: number;
}

export interface EmbeddingChangeWarningResponse {
  hasExistingEmbeddings: boolean;
  currentDimensions?: number;
  affectedChunkCount: number;
  warning: string;
}
