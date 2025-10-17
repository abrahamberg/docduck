import { LoginResponse, AdminUser, ProviderSettings, OpenAiSettingsDto, OpenAiSettingsUpdate, ProviderProbeResult } from './types';

const API_BASES = (() => {
  const unique = new Set<string>();

  const normalize = (value: string) => {
    if (!value) {
      return value;
    }

    if (value.endsWith('/')) {
      return value.slice(0, -1);
    }

    return value;
  };

  const maybeAdd = (value: string | undefined | null) => {
    if (!value) {
      return;
    }

    const trimmed = value.trim();
    if (!trimmed) {
      return;
    }

    unique.add(normalize(trimmed));
  };

  const envBase = (import.meta.env.VITE_API_BASE as string | undefined | null) ?? undefined;
  if (envBase?.startsWith('http')) {
    maybeAdd(envBase);
  } else if (envBase) {
    const relative = envBase.startsWith('/') ? envBase : `/${envBase}`;
    if (typeof window !== 'undefined') {
      maybeAdd(`${window.location.origin}${relative}`);
    } else {
      maybeAdd(relative);
    }
  }

  if (typeof window !== 'undefined') {
    maybeAdd(`${window.location.origin}/api`);
  }

  maybeAdd('http://localhost:5000');

  return Array.from(unique);
})();

const preferredBaseStorageKey = 'docduck-admin-api-base';

const getStoredPreferredBase = (): string | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  const value = localStorage.getItem(preferredBaseStorageKey);
  if (!value) {
    return null;
  }

  return value;
};

const setStoredPreferredBase = (value: string) => {
  if (typeof window === 'undefined') {
    return;
  }

  localStorage.setItem(preferredBaseStorageKey, value);
};

let selectedApiBase = getStoredPreferredBase();

const getCandidateBases = () => {
  if (!selectedApiBase) {
    return API_BASES;
  }

  return [selectedApiBase, ...API_BASES.filter(base => base !== selectedApiBase)];
};

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = localStorage.getItem('docduck-admin-token');
  const headers = new Headers(options.headers);
  headers.set('Content-Type', 'application/json');
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  let lastNetworkError: Error | null = null;

  for (const base of getCandidateBases()) {
    const target = `${base}${path}`;

    try {
      const resp = await fetch(target, { ...options, headers });
      if (resp.status === 401) {
        localStorage.removeItem('docduck-admin-token');
        localStorage.removeItem('docduck-admin-user');
        selectedApiBase = null;
        if (typeof window !== 'undefined') {
          localStorage.removeItem(preferredBaseStorageKey);
        }
        throw new Error('Unauthorized');
      }

      if (!resp.ok) {
        const text = await resp.text();
        throw new Error(text || `Request failed ${resp.status}`);
      }

      selectedApiBase = base;
      setStoredPreferredBase(base);

      if (resp.status === 204 || resp.status === 205) {
        return undefined as T;
      }

      const contentType = resp.headers.get('content-type') ?? '';
      if (!contentType.includes('application/json')) {
        const text = await resp.text();
        return text as unknown as T;
      }

      return resp.json();
    } catch (error: any) {
      const isNetworkError = error instanceof TypeError || error?.name === 'TypeError';
      if (!isNetworkError) {
        throw error;
      }

      lastNetworkError = error;
      continue;
    }
  }

  if (lastNetworkError) {
    throw lastNetworkError;
  }

  throw new Error('Request failed: no reachable admin API base URL.');
}

export async function login(username: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>('/admin/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password }),
  });
}

export async function getProfile(): Promise<AdminUser> {
  return request<AdminUser>('/admin/auth/profile');
}

export async function listUsers(): Promise<{ users: AdminUser[] }> {
  return request('/admin/users');
}

export async function createUser(username: string, password: string, isAdmin: boolean): Promise<AdminUser> {
  return request('/admin/users', {
    method: 'POST',
    body: JSON.stringify({ username, password, isAdmin }),
  });
}

export async function setAdmin(userId: string, isAdmin: boolean): Promise<void> {
  await request(`/admin/users/${userId}/admin`, {
    method: 'POST',
    body: JSON.stringify({ isAdmin }),
  });
}

export async function changePassword(userId: string, password: string): Promise<void> {
  await request(`/admin/users/${userId}/password`, {
    method: 'POST',
    body: JSON.stringify({ password }),
  });
}

export async function listProviders(): Promise<{ providers: ProviderSettings[]; count: number }> {
  return request('/admin/providers');
}

export async function getOpenAiSettings(): Promise<OpenAiSettingsDto> {
  return request('/admin/ai/openai');
}

export async function updateOpenAiSettings(settings: OpenAiSettingsUpdate): Promise<OpenAiSettingsDto> {
  return request('/admin/ai/openai', {
    method: 'PUT',
    body: JSON.stringify({ settings }),
  });
}

export async function updateProvider(
  providerType: string,
  providerName: string,
  settings: Record<string, unknown>
): Promise<void> {
  const route = `/admin/providers/${encodeURIComponent(providerType)}/${encodeURIComponent(providerName)}`;
  await request(route, {
    method: 'PUT',
    body: JSON.stringify({ settings }),
  });
}

export async function createProvider(
  providerType: string,
  providerName: string,
  settings: Record<string, unknown>
): Promise<void> {
  await updateProvider(providerType, providerName, settings);
}

export async function deleteProvider(providerType: string, providerName: string): Promise<void> {
  const route = `/admin/providers/${encodeURIComponent(providerType)}/${encodeURIComponent(providerName)}`;
  await request(route, {
    method: 'DELETE',
  });
}

export async function probeProvider(
  providerType: string,
  settings: Record<string, unknown>,
  options?: { maxDocuments?: number; maxPreviewBytes?: number }
): Promise<ProviderProbeResult> {
  const payload: Record<string, unknown> = { providerType, settings };
  if (options?.maxDocuments !== undefined) {
    payload.maxDocuments = options.maxDocuments;
  }
  if (options?.maxPreviewBytes !== undefined) {
    payload.maxPreviewBytes = options.maxPreviewBytes;
  }

  return request('/admin/providers/probe', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}
