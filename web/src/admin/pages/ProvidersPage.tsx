import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { createProvider, deleteProvider, listProviders, probeProvider, updateProvider } from '../api';
import type { ProviderProbeResult, ProviderSettings } from '../types';

type ProviderTemplate = {
  type: string;
  label: string;
  description: string;
  defaultName: string;
  defaultEnabled: boolean;
  defaults: Record<string, unknown>;
};

const providerTemplates: ProviderTemplate[] = [
  {
    type: 'local',
    label: 'Local File System',
    description: 'Index files from a directory mounted into the API container.',
    defaultName: 'LocalFiles',
    defaultEnabled: true,
    defaults: {
      rootPath: '/data/documents',
      recursive: true,
      fileExtensions: ['.docx', '.pdf', '.txt'],
      excludePatterns: [],
    },
  },
  {
    type: 's3',
    label: 'Amazon S3',
    description: 'Connect to an S3 bucket; supply credentials or enable instance profile access.',
    defaultName: 'S3',
    defaultEnabled: true,
    defaults: {
      bucketName: 'your-bucket-name',
      prefix: '',
      region: 'us-east-1',
      accessKeyId: 'AWS_ACCESS_KEY_ID',
      secretAccessKey: 'AWS_SECRET_ACCESS_KEY',
      sessionToken: '',
      useInstanceProfile: false,
      fileExtensions: ['.docx', '.pdf', '.txt'],
    },
  },
  {
    type: 'onedrive',
    label: 'Microsoft OneDrive',
    description: 'Use Microsoft Graph credentials to index a OneDrive or SharePoint document library.',
    defaultName: 'OneDrive',
    defaultEnabled: true,
    defaults: {
      accountType: 'business',
      tenantId: 'YOUR_TENANT_ID',
      clientId: 'YOUR_CLIENT_ID',
      clientSecret: 'YOUR_CLIENT_SECRET',
      siteId: '',
      driveId: '',
      folderPath: '/Shared Documents/Docs',
      fileExtensions: ['.docx'],
    },
  },
];

const defaultTemplate = providerTemplates[0];

const stringifySettings = (settings: Record<string, unknown>): string => JSON.stringify(settings, null, 2);

const parseSettings = (value: string): Record<string, unknown> | null => {
  try {
    const parsed = JSON.parse(value);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>;
    }
  } catch {
    // ignored
  }

  return null;
};

const buildTemplatePayload = (template: ProviderTemplate, name: string, enabled: boolean): Record<string, unknown> => ({
  enabled,
  name,
  ...template.defaults,
});

const findTemplate = (type: string): ProviderTemplate => providerTemplates.find(template => template.type === type) ?? defaultTemplate;

const toPositiveInteger = (raw: string): number | undefined | null => {
  const trimmed = raw.trim();
  if (!trimmed) {
    return undefined;
  }

  const value = Number(trimmed);
  if (!Number.isFinite(value) || value <= 0) {
    return null;
  }

  return Math.floor(value);
};

export const ProvidersPage: React.FC = () => {
  const [providers, setProviders] = useState<ProviderSettings[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedProvider, setSelectedProvider] = useState<ProviderSettings | null>(null);
  const [settingsJson, setSettingsJson] = useState('');
  const [providerEnabled, setProviderEnabled] = useState(true);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [probing, setProbing] = useState(false);
  const [probeResult, setProbeResult] = useState<ProviderProbeResult | null>(null);
  const [probeMaxDocs, setProbeMaxDocs] = useState('');
  const [probeMaxBytes, setProbeMaxBytes] = useState('');

  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [createType, setCreateType] = useState(defaultTemplate.type);
  const [createName, setCreateName] = useState(defaultTemplate.defaultName);
  const [createEnabled, setCreateEnabled] = useState(defaultTemplate.defaultEnabled);
  const [createSettingsJson, setCreateSettingsJson] = useState(
    stringifySettings(buildTemplatePayload(defaultTemplate, defaultTemplate.defaultName, defaultTemplate.defaultEnabled))
  );
  const [createError, setCreateError] = useState<string | null>(null);
  const [createSaving, setCreateSaving] = useState(false);

  const loadProviders = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await listProviders();
      setProviders(response.providers);
    } catch (err: any) {
      setError(err.message || 'Failed to load providers');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadProviders();
  }, [loadProviders]);

  const openDialog = (provider: ProviderSettings) => {
    setSelectedProvider(provider);
    setDialogOpen(true);
    const payload = provider.settings ?? {};
    const safePayload = typeof payload === 'object' && payload !== null ? payload : {};
    setSettingsJson(JSON.stringify(safePayload, null, 2));
    setProviderEnabled(provider.enabled);
    setFormError(null);
    setProbeResult(null);
    setProbeMaxDocs('');
    setProbeMaxBytes('');
  };

  const closeDialog = () => {
    setDialogOpen(false);
    setSelectedProvider(null);
    setFormError(null);
    setProbeResult(null);
    setSettingsJson('');
    setProviderEnabled(true);
    setProbeMaxDocs('');
    setProbeMaxBytes('');
  };

  const buildPayload = (): Record<string, unknown> | null => {
    const parsed = parseSettings(settingsJson);
    if (!parsed) {
      setFormError('Settings must be valid JSON.');
      return null;
    }

    const payload: Record<string, unknown> = { ...parsed, enabled: providerEnabled };

    if (selectedProvider) {
      payload.name = selectedProvider.providerName;
    }

    setFormError(null);
    return payload;
  };

  const handleSave = async () => {
    if (!selectedProvider) {
      return;
    }

    const payload = buildPayload();
    if (!payload) {
      return;
    }

    try {
      setSaving(true);
      await updateProvider(selectedProvider.providerType, selectedProvider.providerName, payload);
      closeDialog();
      await loadProviders();
    } catch (err: any) {
      setFormError(err.message || 'Failed to save provider.');
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!selectedProvider) {
      return;
    }

    const shouldDelete = window.confirm(`Delete provider "${selectedProvider.providerName}"?`);
    if (!shouldDelete) {
      return;
    }

    try {
      setDeleting(true);
      await deleteProvider(selectedProvider.providerType, selectedProvider.providerName);
      closeDialog();
      await loadProviders();
    } catch (err: any) {
      setFormError(err.message || 'Failed to delete provider.');
    } finally {
      setDeleting(false);
    }
  };

  const handleProbe = async () => {
    if (!selectedProvider) {
      return;
    }

    const payload = buildPayload();
    if (!payload) {
      return;
    }

    try {
      setProbing(true);
      const maxDocsValue = toPositiveInteger(probeMaxDocs);
      if (maxDocsValue === null) {
        setFormError('Max documents must be a positive number.');
        return;
      }

      const maxBytesValue = toPositiveInteger(probeMaxBytes);
      if (maxBytesValue === null) {
        setFormError('Max preview bytes must be a positive number.');
        return;
      }

      const options: { maxDocuments?: number; maxPreviewBytes?: number } = {};
      if (maxDocsValue !== undefined) {
        options.maxDocuments = maxDocsValue;
      }
      if (maxBytesValue !== undefined) {
        options.maxPreviewBytes = maxBytesValue;
      }

      const result = await probeProvider(selectedProvider.providerType, payload, options);
      setProbeResult(result);
    } catch (err: any) {
      setFormError(err.message || 'Failed to probe provider.');
      setProbeResult(null);
    } finally {
      setProbing(false);
    }
  };

  const handleSettingsChange = (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    setSettingsJson(event.target.value);
    setFormError(null);
    setProbeResult(null);
  };

  const handleEnabledToggle = (event: React.ChangeEvent<HTMLInputElement>) => {
    setProviderEnabled(event.target.checked);
    setProbeResult(null);
    setFormError(null);
  };

  const resetCreateState = (template: ProviderTemplate = defaultTemplate) => {
    setCreateType(template.type);
    setCreateName(template.defaultName);
    setCreateEnabled(template.defaultEnabled);
    setCreateSettingsJson(stringifySettings(buildTemplatePayload(template, template.defaultName, template.defaultEnabled)));
    setCreateError(null);
    setCreateSaving(false);
  };

  const openCreateDialog = () => {
    resetCreateState();
    setCreateDialogOpen(true);
  };

  const closeCreateDialog = () => {
    setCreateDialogOpen(false);
    resetCreateState();
  };

  const activeCreateTemplate = useMemo(() => findTemplate(createType), [createType]);

  const handleCreateTypeChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const template = findTemplate(event.target.value);
    setCreateType(template.type);
    setCreateName(template.defaultName);
    setCreateEnabled(template.defaultEnabled);
    setCreateSettingsJson(stringifySettings(buildTemplatePayload(template, template.defaultName, template.defaultEnabled)));
    setCreateError(null);
  };

  const handleCreateNameChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const nextName = event.target.value;
    setCreateName(nextName);
    setCreateError(null);
    setCreateSettingsJson(current => {
      const parsed = parseSettings(current);
      if (!parsed) {
        return current;
      }

      return stringifySettings({ ...parsed, name: nextName });
    });
  };

  const handleCreateEnabledToggle = (event: React.ChangeEvent<HTMLInputElement>) => {
    const enabled = event.target.checked;
    setCreateEnabled(enabled);
    setCreateError(null);
    setCreateSettingsJson(current => {
      const parsed = parseSettings(current);
      if (!parsed) {
        return current;
      }

      return stringifySettings({ ...parsed, enabled });
    });
  };

  const handleCreateSettingsChange = (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    setCreateSettingsJson(event.target.value);
    setCreateError(null);
  };

  const handleCreateReset = () => {
    const template = findTemplate(createType);
    setCreateName(template.defaultName);
    setCreateEnabled(template.defaultEnabled);
    setCreateSettingsJson(stringifySettings(buildTemplatePayload(template, template.defaultName, template.defaultEnabled)));
    setCreateError(null);
  };

  const handleCreateProvider = async () => {
    const trimmedName = createName.trim();
    if (trimmedName.length === 0) {
      setCreateError('Provider name is required.');
      return;
    }

    const parsed = parseSettings(createSettingsJson);
    if (!parsed) {
      setCreateError('Settings must be valid JSON.');
      return;
    }

    const payload: Record<string, unknown> = { ...parsed, name: trimmedName, enabled: createEnabled };

    try {
      setCreateSaving(true);
      await createProvider(createType, trimmedName, payload);
      closeCreateDialog();
      await loadProviders();
    } catch (err: any) {
      setCreateError(err.message || 'Failed to create provider.');
    } finally {
      setCreateSaving(false);
    }
  };

  const handleProbeMaxDocsChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setProbeMaxDocs(event.target.value);
    setProbeResult(null);
    setFormError(null);
  };

  const handleProbeMaxBytesChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setProbeMaxBytes(event.target.value);
    setProbeResult(null);
    setFormError(null);
  };

  const createNameError = createDialogOpen && createName.trim().length === 0 ? 'Provider name is required.' : null;
  const createDisabled = createSaving || Boolean(createNameError);

  if (loading) {
    return <CircularProgress />;
  }

  if (error) {
    return <Alert severity="error">{error}</Alert>;
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 1.5 }}>
        <Typography variant="h5" sx={{ fontWeight: 600 }}>Providers</Typography>
        <Button variant="contained" onClick={openCreateDialog}>Add Provider</Button>
      </Box>
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        {providers.length === 0 && (
          <Paper sx={{ p: 2 }}>
            <Typography>No providers have been registered yet.</Typography>
          </Paper>
        )}
        {providers.map(provider => (
          <Paper key={`${provider.providerType}:${provider.providerName}`} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                {provider.providerName} ({provider.providerType})
              </Typography>
              <Button variant="outlined" size="small" onClick={() => openDialog(provider)}>
                Edit
              </Button>
            </Box>
            <Chip
              label={provider.enabled ? 'Enabled' : 'Disabled'}
              color={provider.enabled ? 'success' : 'default'}
              size="small"
            />
            <Typography variant="body2" sx={{ opacity: 0.7 }}>
              Last updated: {new Date(provider.updatedAt).toLocaleString()}
            </Typography>
          </Paper>
        ))}
      </Box>

      <Dialog open={createDialogOpen} onClose={closeCreateDialog} maxWidth="md" fullWidth>
        <DialogTitle>Add Provider</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextField
            select
            label="Provider Type"
            value={createType}
            onChange={handleCreateTypeChange}
            SelectProps={{ native: true }}
          >
            {providerTemplates.map(template => (
              <option key={template.type} value={template.type}>
                {template.label}
              </option>
            ))}
          </TextField>
          <Typography variant="body2" sx={{ opacity: 0.7 }}>
            {activeCreateTemplate.description}
          </Typography>
          <TextField
            label="Provider Name"
            value={createName}
            onChange={handleCreateNameChange}
            helperText={createNameError ?? 'Unique friendly name; stored as the provider settings "name".'}
            error={Boolean(createNameError)}
            autoFocus
          />
          <FormControlLabel
            control={<Switch checked={createEnabled} onChange={handleCreateEnabledToggle} />}
            label="Enabled"
          />
          <TextField
            label="Settings JSON"
            value={createSettingsJson}
            onChange={handleCreateSettingsChange}
            multiline
            minRows={12}
            InputProps={{ sx: { fontFamily: 'monospace', fontSize: '0.875rem' } }}
            helperText="Customize the provider configuration before creating it."
          />
          {createError && <Alert severity="error">{createError}</Alert>}
        </DialogContent>
        <DialogActions sx={{ justifyContent: 'space-between', px: 3, pb: 2 }}>
          <Button onClick={handleCreateReset} disabled={createSaving}>Reset Template</Button>
          <Box sx={{ display: 'flex', gap: 1 }}>
            <Button onClick={closeCreateDialog} disabled={createSaving}>Cancel</Button>
            <Button variant="contained" onClick={handleCreateProvider} disabled={createDisabled}>
              {createSaving ? 'Creating…' : 'Create'}
            </Button>
          </Box>
        </DialogActions>
      </Dialog>

      <Dialog open={dialogOpen} onClose={closeDialog} maxWidth="md" fullWidth>
        <DialogTitle>Edit Provider</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          {selectedProvider && (
            <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
              {selectedProvider.providerName} ({selectedProvider.providerType})
            </Typography>
          )}
          <FormControlLabel
            control={<Switch checked={providerEnabled} onChange={handleEnabledToggle} />}
            label="Enabled"
          />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              label="Max Documents"
              type="number"
              value={probeMaxDocs}
              onChange={handleProbeMaxDocsChange}
              helperText="Optional limit when probing connectivity."
              inputProps={{ min: 1 }}
            />
            <TextField
              label="Max Preview Bytes"
              type="number"
              value={probeMaxBytes}
              onChange={handleProbeMaxBytesChange}
              helperText="Optional byte budget per sampled document."
              inputProps={{ min: 1 }}
            />
          </Stack>
          <TextField
            label="Settings JSON"
            value={settingsJson}
            onChange={handleSettingsChange}
            multiline
            minRows={12}
            InputProps={{ sx: { fontFamily: 'monospace', fontSize: '0.875rem' } }}
            error={Boolean(formError)}
            helperText={formError ?? 'Update the provider configuration payload and save to persist changes.'}
          />
          {probeResult && (
            <Alert severity={probeResult.success ? 'success' : 'warning'}>
              {probeResult.message}
            </Alert>
          )}
          {probeResult?.documents.length ? (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              <Typography variant="subtitle2">Sample documents</Typography>
              {probeResult.documents.map(doc => (
                <Paper key={doc.documentId} variant="outlined" sx={{ p: 1.5 }}>
                  <Typography variant="subtitle2" sx={{ fontWeight: 500 }}>
                    {doc.filename}
                  </Typography>
                  <Typography variant="body2" sx={{ opacity: 0.7 }}>
                    {doc.mimeType ?? 'unknown mime type'} · {doc.sizeBytes ? `${doc.sizeBytes} bytes` : 'size unknown'} · bytes read: {doc.bytesRead}
                  </Typography>
                </Paper>
              ))}
            </Box>
          ) : null}
        </DialogContent>
        <DialogActions sx={{ justifyContent: 'space-between', px: 3, pb: 2 }}>
          <Button color="error" onClick={handleDelete} disabled={deleting || saving || probing}>
            {deleting ? 'Deleting…' : 'Delete'}
          </Button>
          <Box sx={{ display: 'flex', gap: 1 }}>
            <Button onClick={closeDialog} disabled={saving || deleting}>Cancel</Button>
            <Button onClick={handleProbe} disabled={saving || deleting || probing}>
              {probing ? 'Probing…' : 'Probe'}
            </Button>
            <Button variant="contained" onClick={handleSave} disabled={saving || deleting}>
              {saving ? 'Saving…' : 'Save'}
            </Button>
          </Box>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
