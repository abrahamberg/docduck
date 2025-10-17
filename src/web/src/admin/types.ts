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

export interface OpenAiSettingsDto {
  settings: {
    enabled: boolean;
    apiKey: string;
    baseUrl: string;
    chatModel: string;
    chatModelSmall: string;
    chatModelLarge: string;
    embedModel: string;
    embedBatchSize: number;
    maxTokens: number;
    temperature: number;
    refineSystemPrompt: string;
  };
  updatedAt: string;
}

export interface OpenAiSettingsUpdate {
  enabled: boolean;
  apiKey: string;
  baseUrl: string;
  chatModel: string;
  chatModelSmall: string;
  chatModelLarge: string;
  embedModel: string;
  embedBatchSize: number;
  maxTokens: number;
  temperature: number;
  refineSystemPrompt: string;
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
