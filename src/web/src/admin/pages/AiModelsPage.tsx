import React, { useState, useEffect } from 'react';
import {
  Box,
  Typography,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Button,
  CircularProgress,
  Alert,
  Switch,
  FormControlLabel,
  Select,
  MenuItem,
  Chip,
  IconButton,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  Snackbar,
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Edit as EditIcon,
  Add as AddIcon,
  Delete as DeleteIcon,
} from '@mui/icons-material';
import { getAiConfiguration, updateAiConfiguration, testModel, testEmbedding } from '../api';
import type {
  AiConfigurationDto,
  AiModelAssignmentDto,
  AiEmbeddingModelAssignmentDto,
} from '../types';

// Helper function to convert request template to string format
function getRequestTemplateString(template: any): string | undefined {
  if (typeof template === 'string') {
    return template;
  }
  if (template) {
    return JSON.stringify(template, null, 2);
  }
  return undefined;
}

interface ModelFormData {
  id: string;
  displayName: string;
  modelId: string;
  url: string;
  headers: Record<string, string>;
  requestTemplate?: string; // JSON as string for editing
  responseMapping?: string; // JSON as string for editing
  defaultParams?: string; // JSON as string for editing
}

interface EmbeddingFormData {
  id: string;
  displayName: string;
  modelId: string;
  url: string;
  headers: Record<string, string>;
  requestTemplate?: string;
  responseMapping?: string;
  defaultParams?: string;
  dimensions: number;
}

export const AiModelsPage: React.FC = () => {
  const [config, setConfig] = useState<AiConfigurationDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Model edit dialog state
  const [modelDialogOpen, setModelDialogOpen] = useState(false);
  const [editingModel, setEditingModel] = useState<ModelFormData | null>(null);
  const [modelTouched, setModelTouched] = useState(false);
  const [modelApiKeyChanged, setModelApiKeyChanged] = useState(false);
  const [testingModel, setTestingModel] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  // Embedding edit dialog state
  const [embeddingDialogOpen, setEmbeddingDialogOpen] = useState(false);
  const [editingEmbedding, setEditingEmbedding] = useState<EmbeddingFormData | null>(null);
  const [embeddingTouched, setEmbeddingTouched] = useState(false);
  const [embeddingApiKeyChanged, setEmbeddingApiKeyChanged] = useState(false);
  const [testingEmbedding, setTestingEmbedding] = useState(false);
  const [embeddingTestResult, setEmbeddingTestResult] = useState<{
    success: boolean;
    message: string;
  } | null>(null);

  const loadConfig = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await getAiConfiguration();
      setConfig(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load configuration');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadConfig();
  }, []);

  const handleAddModel = () => {
    const newModel = {
      id: '',
      displayName: '',
      modelId: '',
      url: 'https://api.openai.com/v1/chat/completions',
      headers: { Authorization: 'Bearer YOUR_API_KEY' },
      requestTemplate: '{\n  "model": "{MODEL_ID}",\n  "messages": {MESSAGES}\n}',
      responseMapping: undefined,
      defaultParams: '{}',
    };
    setEditingModel(newModel);
    setEditingModel(newModel);
    setModelTouched(false);
    setModelApiKeyChanged(false);
    setTestResult(null);
    setFieldErrors({});
    setModelDialogOpen(true);
  };

  const handleEditModel = (model: AiModelAssignmentDto) => {
    const modelData = {
      id: model.id,
      displayName: model.displayName || '',
      modelId: model.modelId || '',
      url: model.url || '',
      headers: model.headers || {},
      requestTemplate: getRequestTemplateString(model.requestTemplate),
      responseMapping: model.responseMapping
        ? JSON.stringify(model.responseMapping, null, 2)
        : undefined,
      defaultParams: model.defaultParams ? JSON.stringify(model.defaultParams, null, 2) : '{}',
    };
    setEditingModel(modelData);
    setModelTouched(false);
    setModelApiKeyChanged(false);
    setTestResult(null);
    setFieldErrors({});
    setModelDialogOpen(true);
  };

  const validateModelForm = (): boolean => {
    const errors: Record<string, string> = {};

    if (!editingModel?.id?.trim()) {
      errors.id = 'ID is required';
    }
    if (!editingModel?.displayName?.trim()) {
      errors.displayName = 'Display Name is required';
    }
    if (!editingModel?.modelId?.trim()) {
      errors.modelId = 'Model ID is required';
    }
    if (!editingModel?.url?.trim()) {
      errors.url = 'URL is required';
    } else if (!editingModel.url.match(/^https?:\/\/.+/)) {
      errors.url = 'Invalid URL format';
    }

    // Validate JSON fields
    if (editingModel?.requestTemplate) {
      try {
        JSON.parse(editingModel.requestTemplate);
      } catch {
        errors.requestTemplate = 'Invalid JSON';
      }
    }
    if (editingModel?.responseMapping) {
      try {
        JSON.parse(editingModel.responseMapping);
      } catch {
        errors.responseMapping = 'Invalid JSON';
      }
    }
    if (editingModel?.defaultParams) {
      try {
        JSON.parse(editingModel.defaultParams);
      } catch {
        errors.defaultParams = 'Invalid JSON';
      }
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSaveModel = async () => {
    if (!config || !editingModel || !validateModelForm()) return;

    console.log('Saving model:', editingModel);

    const isNew = !config.modelRegistry?.some((m) => m.id === editingModel.id);

    // Parse JSON fields
    const modelData: any = {
      id: editingModel.id,
      displayName: editingModel.displayName,
      modelId: editingModel.modelId,
      url: editingModel.url,
      headers: editingModel.headers,
      requestTemplate: editingModel.requestTemplate ? editingModel.requestTemplate : undefined,
      responseMapping: editingModel.responseMapping
        ? JSON.parse(editingModel.responseMapping)
        : undefined,
      defaultParams: editingModel.defaultParams ? JSON.parse(editingModel.defaultParams) : {},
    };

    let updatedConfig: AiConfigurationDto;

    if (isNew) {
      // Add new model
      updatedConfig = {
        ...config,
        modelRegistry: [
          ...(config.modelRegistry || []),
          {
            ...modelData,
            enabled: true,
            testStatus: 0, // Untested
            lastTestedAt: undefined,
            lastTestMessage: undefined,
            maxContextTokens: 128000,
            maxOutputTokens: 16000,
            supportsFunctionCalling: true,
            costFactor: 1,
            customHeaders: {},
            timeoutSeconds: 120,
          },
        ],
      };
    } else {
      // Update existing model - preserve existing values if not changed
      updatedConfig = {
        ...config,
        modelRegistry: config.modelRegistry?.map((m) =>
          m.id === editingModel.id
            ? {
                ...m, // Keep existing fields
                ...modelData, // Override with changed fields
              }
            : m
        ),
      };
    }

    console.log('Updated config:', updatedConfig);

    try {
      setSaving(true);
      setError(null);
      console.log('Calling updateAiConfiguration...');
      const saved = await updateAiConfiguration(updatedConfig);
      console.log('Saved successfully:', saved);
      setConfig(saved);
      setSuccess(isNew ? 'Model added successfully' : 'Model updated successfully');

      // Reset changed flags - stay in dialog for testing
      setModelTouched(false);
      setModelApiKeyChanged(false);

      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      console.error('Save error:', err);
      const errorMsg = err instanceof Error ? err.message : 'Failed to save model';
      setError(errorMsg);
    } finally {
      setSaving(false);
    }
  };

  const handleTestModelInDialog = async () => {
    if (!editingModel || !config) return;

    console.log('Testing model:', editingModel.id);

    try {
      setTestingModel(true);
      setTestResult(null);

      // Use API function which handles auth token automatically
      console.log('Calling testModel...');
      const result = await testModel(editingModel.id);
      console.log('Test result:', result);
      setTestResult({ success: result.success, message: result.error || result.model || '' });

      // Update config with test result AND save to database
      const updatedConfig = {
        ...config,
        modelRegistry: config.modelRegistry?.map((m) =>
          m.id === editingModel.id
            ? {
                ...m,
                testStatus: result.success ? 1 : 2,
                lastTestedAt: new Date().toISOString(),
                lastTestMessage: result.error || result.model || '',
              }
            : m
        ),
      };

      setConfig(updatedConfig);

      // Save to database
      await updateAiConfiguration(updatedConfig);
      setSuccess(
        result.success ? 'Model tested successfully - status saved' : 'Test failed - status saved'
      );
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      console.error('Test error:', err);
      const errorMsg = err instanceof Error ? err.message : 'Test failed';
      setTestResult({
        success: false,
        message: errorMsg,
      });
      setError(errorMsg);
    } finally {
      setTestingModel(false);
    }
  };

  const handleTestEmbeddingInDialog = async () => {
    if (!editingEmbedding || !config) return;

    console.log('Testing embedding:', editingEmbedding.id);

    try {
      setTestingEmbedding(true);
      setEmbeddingTestResult(null);

      // Use API function which handles auth token automatically
      console.log('Calling testEmbedding...');
      const result = await testEmbedding(editingEmbedding.id);
      console.log('Test result:', result);
      setEmbeddingTestResult({ success: result.success, message: result.error || result.model || '' });

      // Update config with test result AND save to database
      const updatedConfig = {
        ...config,
        embeddingRegistry: config.embeddingRegistry?.map((e) =>
          e.id === editingEmbedding.id
            ? {
                ...e,
                testStatus: result.success ? 1 : 2,
                lastTestedAt: new Date().toISOString(),
                lastTestMessage: result.error || result.model || '',
              }
            : e
        ),
      };

      setConfig(updatedConfig);

      // Save to database
      await updateAiConfiguration(updatedConfig);
      setSuccess(
        result.success
          ? 'Embedding tested successfully - status saved'
          : 'Test failed - status saved'
      );
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      console.error('Embedding test error:', err);
      const errorMsg = err instanceof Error ? err.message : 'Test failed';
      setEmbeddingTestResult({
        success: false,
        message: errorMsg,
      });
      setError(errorMsg);
    } finally {
      setTestingEmbedding(false);
    }
  };

  const handleDeleteModel = async (modelId: string) => {
    if (!config) return;
    if (!confirm('Are you sure you want to delete this model?')) return;

    try {
      setSaving(true);

      const updatedConfig = {
        ...config,
        modelRegistry: config.modelRegistry?.filter((m) => m.id !== modelId),
        // Clear tier assignments if this model was assigned
        microModelId: config.microModelId === modelId ? undefined : config.microModelId,
        miniModelId: config.miniModelId === modelId ? undefined : config.miniModelId,
        fullModelId: config.fullModelId === modelId ? undefined : config.fullModelId,
      };

      const saved = await updateAiConfiguration(updatedConfig);
      setConfig(saved);
      setSuccess('Model deleted successfully');
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to delete model';
      setError(errorMsg);
    } finally {
      setSaving(false);
    }
  };

  const handleDeleteEmbedding = async (embeddingId: string) => {
    if (!config) return;
    if (!confirm('Are you sure you want to delete this embedding model?')) return;

    try {
      setSaving(true);

      const updatedConfig = {
        ...config,
        embeddingRegistry: config.embeddingRegistry?.filter((e) => e.id !== embeddingId),
        // Clear active embedding if this was it
        activeEmbeddingModelId:
          config.activeEmbeddingModelId === embeddingId ? '' : config.activeEmbeddingModelId,
      };

      const saved = await updateAiConfiguration(updatedConfig);
      setConfig(saved);
      setSuccess('Embedding model deleted successfully');
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to delete embedding model';
      setError(errorMsg);
    } finally {
      setSaving(false);
    }
  };

  const handleAddEmbedding = () => {
    setEditingEmbedding({
      id: '',
      displayName: '',
      modelId: '',
      url: 'https://api.openai.com/v1/embeddings',
      headers: { Authorization: 'Bearer YOUR_API_KEY' },
      requestTemplate:
        '{\n  "model": "{MODEL_ID}",\n  "input": "{INPUT}",\n  "encoding_format": "float"\n}',
      responseMapping: '{"embedding": "$.data[0].embedding"}',
      defaultParams: '{}',
      dimensions: 1536,
    });
    setEmbeddingTouched(false);
    setEmbeddingApiKeyChanged(false);
    setEmbeddingTestResult(null);
    setEmbeddingDialogOpen(true);
  };

  const handleEditEmbedding = (embedding: AiEmbeddingModelAssignmentDto) => {
    const embeddingData = {
      id: embedding.id,
      displayName: embedding.displayName || '',
      modelId: embedding.modelId || '',
      url: embedding.url || '',
      headers: embedding.headers || {},
      requestTemplate: getRequestTemplateString(embedding.requestTemplate),
      responseMapping: embedding.responseMapping
        ? JSON.stringify(embedding.responseMapping, null, 2)
        : undefined,
      defaultParams: embedding.defaultParams
        ? JSON.stringify(embedding.defaultParams, null, 2)
        : '{}',
      dimensions: embedding.dimensions || 1536,
    };
    setEditingEmbedding(embeddingData);
    setEmbeddingTouched(false);
    setEmbeddingApiKeyChanged(false);
    setEmbeddingTestResult(null);
    setEmbeddingDialogOpen(true);
  };

  const handleSaveEmbedding = async () => {
    if (!config || !editingEmbedding) return;

    const isNew = !config.embeddingRegistry?.some((e) => e.id === editingEmbedding.id);

    // Parse JSON fields
    const embeddingData: any = {
      id: editingEmbedding.id,
      displayName: editingEmbedding.displayName,
      modelId: editingEmbedding.modelId,
      url: editingEmbedding.url,
      headers: editingEmbedding.headers,
      requestTemplate: editingEmbedding.requestTemplate
        ? editingEmbedding.requestTemplate
        : undefined,
      responseMapping: editingEmbedding.responseMapping
        ? JSON.parse(editingEmbedding.responseMapping)
        : undefined,
      defaultParams: editingEmbedding.defaultParams
        ? JSON.parse(editingEmbedding.defaultParams)
        : {},
      dimensions: editingEmbedding.dimensions,
    };

    let updatedConfig: AiConfigurationDto;

    if (isNew) {
      updatedConfig = {
        ...config,
        embeddingRegistry: [
          ...(config.embeddingRegistry || []),
          {
            ...embeddingData,
            enabled: true,
            testStatus: 0,
            lastTestedAt: undefined,
            lastTestMessage: undefined,
            timeoutSeconds: 120,
          },
        ],
      };
    } else {
      updatedConfig = {
        ...config,
        embeddingRegistry: config.embeddingRegistry?.map((e) =>
          e.id === editingEmbedding.id
            ? {
                ...e, // Keep existing fields
                ...embeddingData, // Override with changed fields
              }
            : e
        ),
      };
    }

    try {
      setSaving(true);
      setError(null);
      const saved = await updateAiConfiguration(updatedConfig);
      setConfig(saved);
      setSuccess(
        isNew ? 'Embedding model added successfully' : 'Embedding model updated successfully'
      );

      // Reset changed flags - stay in dialog for testing
      setEmbeddingTouched(false);
      setEmbeddingApiKeyChanged(false);

      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save embedding model');
    } finally {
      setSaving(false);
    }
  };

  const handleSave = async () => {
    if (!config) return;
    try {
      setSaving(true);
      setError(null);
      await updateAiConfiguration(config);
      setSuccess('Configuration saved successfully');
      await loadConfig(); // Reload to get server state
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="400px">
        <CircularProgress />
      </Box>
    );
  }

  if (!config) {
    return (
      <Box p={3}>
        <Alert severity="error">Failed to load AI configuration</Alert>
      </Box>
    );
  }

  const getTestStatusChip = (status?: number) => {
    if (!status || status === 0) {
      return <Chip label="Untested" size="small" />;
    }
    if (status === 1) {
      return <Chip label="Passed" size="small" color="success" />;
    }
    return <Chip label="Failed" size="small" color="error" />;
  };

  return (
    <Box p={3}>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4">AI Models Configuration</Typography>
        <Box>
          <IconButton onClick={loadConfig} sx={{ mr: 1 }}>
            <RefreshIcon />
          </IconButton>
          <Button variant="contained" onClick={handleSave} disabled={saving}>
            {saving ? 'Saving...' : 'Save Configuration'}
          </Button>
        </Box>
      </Box>

      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {success && (
        <Alert severity="success" onClose={() => setSuccess(null)} sx={{ mb: 2 }}>
          {success}
        </Alert>
      )}

      <Paper sx={{ mb: 3 }}>
        <Box p={2}>
          <FormControlLabel
            control={
              <Switch
                checked={config.enabled ?? false}
                onChange={(e) => setConfig({ ...config, enabled: e.target.checked })}
              />
            }
            label="Enable AI Features"
          />
        </Box>
      </Paper>

      {/* Tier Assignments */}
      <Paper sx={{ mb: 3 }}>
        <Box p={2}>
          <Typography variant="h6" gutterBottom>
            Tier Assignments
          </Typography>
          <Box display="flex" gap={2} flexDirection="column">
            <Box>
              <Typography variant="body2" gutterBottom>
                Micro Model:
              </Typography>
              <Select
                value={config.microModelId || ''}
                onChange={(e) =>
                  setConfig({ ...config, microModelId: e.target.value || undefined })
                }
                fullWidth
                size="small"
              >
                <MenuItem value="">None</MenuItem>
                {config.modelRegistry?.map((m) => (
                  <MenuItem key={m.id} value={m.id}>
                    {m.displayName} ({m.modelId})
                  </MenuItem>
                ))}
              </Select>
            </Box>
            <Box>
              <Typography variant="body2" gutterBottom>
                Mini Model:
              </Typography>
              <Select
                value={config.miniModelId || ''}
                onChange={(e) => setConfig({ ...config, miniModelId: e.target.value || undefined })}
                fullWidth
                size="small"
              >
                <MenuItem value="">None</MenuItem>
                {config.modelRegistry?.map((m) => (
                  <MenuItem key={m.id} value={m.id}>
                    {m.displayName} ({m.modelId})
                  </MenuItem>
                ))}
              </Select>
            </Box>
            <Box>
              <Typography variant="body2" gutterBottom>
                Full Model:
              </Typography>
              <Select
                value={config.fullModelId || ''}
                onChange={(e) => setConfig({ ...config, fullModelId: e.target.value || undefined })}
                fullWidth
                size="small"
              >
                <MenuItem value="">None</MenuItem>
                {config.modelRegistry?.map((m) => (
                  <MenuItem key={m.id} value={m.id}>
                    {m.displayName} ({m.modelId})
                  </MenuItem>
                ))}
              </Select>
            </Box>
            <Box>
              <Typography variant="body2" gutterBottom>
                Active Embedding Model:
              </Typography>
              <Select
                value={config.activeEmbeddingModelId || ''}
                onChange={(e) =>
                  setConfig({ ...config, activeEmbeddingModelId: e.target.value || '' })
                }
                fullWidth
                size="small"
              >
                <MenuItem value="">None</MenuItem>
                {config.embeddingRegistry?.map((e) => (
                  <MenuItem key={e.id} value={e.id}>
                    {e.displayName} ({e.modelId})
                  </MenuItem>
                ))}
              </Select>
            </Box>
          </Box>
        </Box>
      </Paper>

      {/* Chat Models Registry */}
      <Paper sx={{ mb: 3 }}>
        <Box p={2}>
          <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
            <Typography variant="h6">Chat Models Registry</Typography>
            <Button startIcon={<AddIcon />} variant="outlined" onClick={handleAddModel}>
              Add Model
            </Button>
          </Box>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>ID</TableCell>
                  <TableCell>Display Name</TableCell>
                  <TableCell>Model ID</TableCell>
                  <TableCell>Base URL</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {config.modelRegistry?.map((model) => (
                  <TableRow key={model.id}>
                    <TableCell>{model.id}</TableCell>
                    <TableCell>{model.displayName}</TableCell>
                    <TableCell>{model.modelId}</TableCell>
                    <TableCell>{model.baseUrl || 'Default'}</TableCell>
                    <TableCell>{getTestStatusChip(model.testStatus)}</TableCell>
                    <TableCell>
                      <IconButton size="small" onClick={() => handleEditModel(model)}>
                        <EditIcon fontSize="small" />
                      </IconButton>
                      <IconButton
                        size="small"
                        onClick={() => handleDeleteModel(model.id)}
                        color="error"
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </Box>
      </Paper>

      {/* Embedding Models Registry */}
      <Paper>
        <Box p={2}>
          <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
            <Typography variant="h6">Embedding Models Registry</Typography>
            <Button startIcon={<AddIcon />} variant="outlined" onClick={handleAddEmbedding}>
              Add Embedding Model
            </Button>
          </Box>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>ID</TableCell>
                  <TableCell>Display Name</TableCell>
                  <TableCell>Model ID</TableCell>
                  <TableCell>Base URL</TableCell>
                  <TableCell>Dimensions</TableCell>
                  <TableCell>Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {config.embeddingRegistry?.map((embedding) => (
                  <TableRow key={embedding.id}>
                    <TableCell>{embedding.id}</TableCell>
                    <TableCell>{embedding.displayName}</TableCell>
                    <TableCell>{embedding.modelId}</TableCell>
                    <TableCell>{embedding.baseUrl || 'Default'}</TableCell>
                    <TableCell>{embedding.dimensions}</TableCell>
                    <TableCell>
                      <IconButton size="small" onClick={() => handleEditEmbedding(embedding)}>
                        <EditIcon fontSize="small" />
                      </IconButton>
                      <IconButton
                        size="small"
                        onClick={() => handleDeleteEmbedding(embedding.id)}
                        color="error"
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </Box>
      </Paper>

      {/* Model Edit Dialog */}
      <Dialog
        open={modelDialogOpen}
        onClose={() => setModelDialogOpen(false)}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>
          {editingModel?.id && config?.modelRegistry?.some((m) => m.id === editingModel.id)
            ? 'Edit Model'
            : 'Add Model'}
        </DialogTitle>
        <DialogContent>
          <Box display="flex" flexDirection="column" gap={2} mt={1}>
            <TextField
              label="ID"
              value={editingModel?.id || ''}
              onChange={(e) => {
                setEditingModel(editingModel ? { ...editingModel, id: e.target.value } : null);
                setModelTouched(true);
              }}
              disabled={!!config?.modelRegistry?.some((m) => m.id === editingModel?.id)}
              fullWidth
              size="small"
              helperText={fieldErrors.id || 'Unique identifier (e.g., openai-gpt4o-mini)'}
              error={!!fieldErrors.id}
            />
            <TextField
              label="Display Name"
              value={editingModel?.displayName || ''}
              onChange={(e) => {
                setEditingModel(
                  editingModel ? { ...editingModel, displayName: e.target.value } : null
                );
                setModelTouched(true);
              }}
              fullWidth
              size="small"
              helperText={fieldErrors.displayName || 'Human-readable name'}
              error={!!fieldErrors.displayName}
            />
            <TextField
              label="Model ID"
              value={editingModel?.modelId || ''}
              onChange={(e) => {
                setEditingModel(editingModel ? { ...editingModel, modelId: e.target.value } : null);
                setModelTouched(true);
              }}
              fullWidth
              size="small"
              placeholder="e.g., gpt-4o-mini"
              helperText={fieldErrors.modelId || 'OpenAI model identifier'}
              error={!!fieldErrors.modelId}
            />
            <TextField
              label="URL"
              value={editingModel?.url || ''}
              onChange={(e) => {
                setEditingModel(editingModel ? { ...editingModel, url: e.target.value } : null);
                setModelTouched(true);
              }}
              fullWidth
              size="small"
              placeholder="https://api.openai.com/v1/chat/completions"
              helperText={fieldErrors.url || 'Full endpoint URL'}
              error={!!fieldErrors.url}
            />
            <TextField
              label="Headers (JSON)"
              value={JSON.stringify(editingModel?.headers || {}, null, 2)}
              onChange={(e) => {
                try {
                  const headers = JSON.parse(e.target.value);
                  setEditingModel(editingModel ? { ...editingModel, headers } : null);
                  setModelTouched(true);
                } catch {
                  // Invalid JSON - let user continue typing
                }
              }}
              fullWidth
              multiline
              rows={3}
              size="small"
              placeholder='{"Authorization": "Bearer sk-..."}'
              helperText="HTTP headers as JSON object"
            />
            <TextField
              label="Request Template (JSON)"
              value={editingModel?.requestTemplate || ''}
              onChange={(e) => {
                setEditingModel(
                  editingModel ? { ...editingModel, requestTemplate: e.target.value } : null
                );
                setModelTouched(true);
              }}
              fullWidth
              multiline
              rows={4}
              size="small"
              placeholder='{"model": "{MODEL_ID}", "messages": {MESSAGES}}'
              helperText={
                fieldErrors.requestTemplate || 'Template with {MODEL_ID}, {MESSAGES} placeholders'
              }
              error={!!fieldErrors.requestTemplate}
            />
            <TextField
              label="Response Mapping (JSON, optional)"
              value={editingModel?.responseMapping || ''}
              onChange={(e) => {
                setEditingModel(
                  editingModel ? { ...editingModel, responseMapping: e.target.value } : null
                );
                setModelTouched(true);
              }}
              fullWidth
              multiline
              rows={3}
              size="small"
              placeholder='{"contentPath": "choices[0].message.content"}'
              helperText={fieldErrors.responseMapping || 'JSON path mappings for response fields'}
              error={!!fieldErrors.responseMapping}
            />
            <TextField
              label="Default Parameters (JSON)"
              value={editingModel?.defaultParams || '{}'}
              onChange={(e) => {
                setEditingModel(
                  editingModel ? { ...editingModel, defaultParams: e.target.value } : null
                );
                setModelTouched(true);
              }}
              fullWidth
              multiline
              rows={3}
              size="small"
              placeholder="{}"
              helperText={
                fieldErrors.defaultParams || 'Model-specific parameters (e.g., temperature, top_p)'
              }
              error={!!fieldErrors.defaultParams}
            />

            {editingModel?.id &&
              config?.modelRegistry?.some((m) => m.id === editingModel.id) &&
              !modelTouched && (
                <Box>
                  <Button
                    variant="outlined"
                    onClick={handleTestModelInDialog}
                    disabled={testingModel}
                    fullWidth
                  >
                    {testingModel ? 'Testing...' : 'Test Model'}
                  </Button>
                  {testResult && (
                    <Alert severity={testResult.success ? 'success' : 'error'} sx={{ mt: 1 }}>
                      {testResult.message}
                    </Alert>
                  )}
                </Box>
              )}
          </Box>
        </DialogContent>
        <DialogActions>
          {editingModel?.id && config?.modelRegistry?.some((m) => m.id === editingModel.id) && (
            <Button
              onClick={() => {
                handleDeleteModel(editingModel.id);
                setModelDialogOpen(false);
              }}
              color="error"
              disabled={saving}
            >
              Delete
            </Button>
          )}
          <Box sx={{ flex: 1 }} />
          <Button onClick={() => setModelDialogOpen(false)}>Close</Button>
          <Button
            onClick={handleSaveModel}
            variant="contained"
            disabled={saving || !editingModel?.id || !editingModel?.modelId}
          >
            {saving ? 'Saving...' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Snackbar for Success/Error Messages */}
      <Snackbar
        open={!!success}
        autoHideDuration={3000}
        onClose={() => setSuccess(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="success" onClose={() => setSuccess(null)}>
          {success}
        </Alert>
      </Snackbar>

      <Snackbar
        open={!!error}
        autoHideDuration={5000}
        onClose={() => setError(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="error" onClose={() => setError(null)}>
          {error}
        </Alert>
      </Snackbar>

      {/* Embedding Edit Dialog */}
      <Dialog
        open={embeddingDialogOpen}
        onClose={() => setEmbeddingDialogOpen(false)}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>
          {editingEmbedding?.id &&
          config?.embeddingRegistry?.some((e) => e.id === editingEmbedding.id)
            ? 'Edit Embedding Model'
            : 'Add Embedding Model'}
        </DialogTitle>
        <DialogContent>
          <Box display="flex" flexDirection="column" gap={2} mt={1}>
            <TextField
              label="ID"
              value={editingEmbedding?.id || ''}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, id: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              disabled={!!config?.embeddingRegistry?.some((e) => e.id === editingEmbedding?.id)}
              fullWidth
              size="small"
              helperText="Unique identifier (e.g., openai-text-embedding-3-small)"
            />
            <TextField
              label="Display Name"
              value={editingEmbedding?.displayName || ''}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, displayName: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
            />
            <TextField
              label="Model ID"
              value={editingEmbedding?.modelId || ''}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, modelId: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
              placeholder="e.g., text-embedding-3-small"
            />
            <TextField
              label="URL"
              value={editingEmbedding?.url || ''}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, url: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
              placeholder="https://api.openai.com/v1/embeddings"
              helperText="Full endpoint URL"
            />
            <TextField
              label="Headers (JSON)"
              value={JSON.stringify(editingEmbedding?.headers || {}, null, 2)}
              onChange={(e) => {
                try {
                  const headers = JSON.parse(e.target.value);
                  setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, headers } : null);
                  setEmbeddingTouched(true);
                } catch {
                  // Invalid JSON - let user continue typing
                }
              }}
              fullWidth
              multiline
              rows={3}
              size="small"
              placeholder='{"Authorization": "Bearer sk-..."}'
              helperText="HTTP headers as JSON object"
            />
            <TextField
              label="Request Template (JSON)"
              value={editingEmbedding?.requestTemplate || ''}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, requestTemplate: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              multiline
              rows={3}
              size="small"
              placeholder='{"model": "{MODEL_ID}", "input": "{INPUT}"}'
              helperText="Template with {MODEL_ID}, {INPUT} placeholders"
            />
            <TextField
              label="Response Mapping (JSON)"
              value={editingEmbedding?.responseMapping || ''}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, responseMapping: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              multiline
              rows={2}
              size="small"
              placeholder='{"embedding": "$.data[0].embedding"}'
              helperText="JSON path mappings for response fields"
            />
            <TextField
              label="Default Parameters (JSON)"
              value={editingEmbedding?.defaultParams || '{}'}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding ? { ...editingEmbedding, defaultParams: e.target.value } : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              multiline
              rows={2}
              size="small"
              placeholder="{}"
              helperText="Model-specific parameters"
            />
            <TextField
              label="Dimensions"
              value={editingEmbedding?.dimensions || 1536}
              onChange={(e) => {
                setEditingEmbedding(
                  editingEmbedding
                    ? { ...editingEmbedding, dimensions: Number.parseInt(e.target.value) || 1536 }
                    : null
                );
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
              type="number"
              helperText="Vector dimensions (e.g., 1536 for ada-002)"
            />

            {editingEmbedding?.id &&
              config?.embeddingRegistry?.some((e) => e.id === editingEmbedding.id) &&
              !embeddingTouched && (
                <Box>
                  <Button
                    variant="outlined"
                    onClick={handleTestEmbeddingInDialog}
                    disabled={testingEmbedding}
                    fullWidth
                  >
                    {testingEmbedding ? 'Testing...' : 'Test Embedding'}
                  </Button>
                  {embeddingTestResult && (
                    <Alert
                      severity={embeddingTestResult.success ? 'success' : 'error'}
                      sx={{ mt: 1 }}
                    >
                      {embeddingTestResult.message}
                    </Alert>
                  )}
                </Box>
              )}
          </Box>
        </DialogContent>
        <DialogActions>
          {editingEmbedding?.id &&
            config?.embeddingRegistry?.some((e) => e.id === editingEmbedding.id) && (
              <Button
                onClick={() => {
                  handleDeleteEmbedding(editingEmbedding.id);
                  setEmbeddingDialogOpen(false);
                }}
                color="error"
                disabled={saving}
              >
                Delete
              </Button>
            )}
          <Box sx={{ flex: 1 }} />
          <Button onClick={() => setEmbeddingDialogOpen(false)}>Close</Button>
          <Button
            onClick={handleSaveEmbedding}
            variant="contained"
            disabled={saving || !editingEmbedding?.id || !editingEmbedding?.modelId}
          >
            {saving ? 'Saving...' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
