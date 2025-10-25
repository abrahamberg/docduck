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
  InputAdornment,
} from '@mui/material';
import { Refresh as RefreshIcon, Edit as EditIcon, Add as AddIcon, Delete as DeleteIcon, Visibility, VisibilityOff } from '@mui/icons-material';
import { getAiConfiguration, updateAiConfiguration, testModel, testEmbedding } from '../api';
import type { AiConfigurationDto, AiModelAssignmentDto, AiEmbeddingModelAssignmentDto } from '../types';

interface ModelFormData {
  id: string;
  displayName: string;
  modelId: string;
  baseUrl: string;
  apiKey: string;
}

interface EmbeddingFormData {
  id: string;
  displayName: string;
  modelId: string;
  baseUrl: string;
  apiKey: string;
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
  const [originalModel, setOriginalModel] = useState<ModelFormData | null>(null);
  const [modelTouched, setModelTouched] = useState(false);
  const [modelApiKeyChanged, setModelApiKeyChanged] = useState(false);
  const [showModelApiKey, setShowModelApiKey] = useState(false);
  const [testingModel, setTestingModel] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  // Embedding edit dialog state
  const [embeddingDialogOpen, setEmbeddingDialogOpen] = useState(false);
  const [editingEmbedding, setEditingEmbedding] = useState<EmbeddingFormData | null>(null);
  const [originalEmbedding, setOriginalEmbedding] = useState<EmbeddingFormData | null>(null);
  const [embeddingTouched, setEmbeddingTouched] = useState(false);
  const [embeddingApiKeyChanged, setEmbeddingApiKeyChanged] = useState(false);
  const [showEmbeddingApiKey, setShowEmbeddingApiKey] = useState(false);
  const [testingEmbedding, setTestingEmbedding] = useState(false);
  const [embeddingTestResult, setEmbeddingTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [embeddingFieldErrors, setEmbeddingFieldErrors] = useState<Record<string, string>>({});

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
      baseUrl: '',
      apiKey: '',
    };
    setEditingModel(newModel);
    setOriginalModel(newModel);
    setModelTouched(false);
    setModelApiKeyChanged(false);
    setShowModelApiKey(false);
    setTestResult(null);
    setFieldErrors({});
    setModelDialogOpen(true);
  };

  const handleEditModel = (model: AiModelAssignmentDto) => {
    const modelData = {
      id: model.id,
      displayName: model.displayName || '',
      modelId: model.modelId || '',
      baseUrl: model.baseUrl || '',
      apiKey: model.apiKey || '',
    };
    setEditingModel(modelData);
    setOriginalModel(modelData);
    setModelTouched(false);
    setModelApiKeyChanged(false);
    setShowModelApiKey(false);
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
    if (editingModel?.baseUrl && !editingModel.baseUrl.match(/^https?:\/\/.+/)) {
      errors.baseUrl = 'Invalid URL format';
    }
    
    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSaveModel = async () => {
    if (!config || !editingModel || !validateModelForm()) return;

    console.log('Saving model:', editingModel);

    const isNew = !config.modelRegistry?.some(m => m.id === editingModel.id);
    
    // Prepare model data - only include apiKey if it was actually changed
    const modelData: any = {
      id: editingModel.id,
      displayName: editingModel.displayName,
      modelId: editingModel.modelId,
      baseUrl: editingModel.baseUrl,
    };
    
    // Only include API key if it was changed (not masked value)
    if (modelApiKeyChanged || isNew) {
      modelData.apiKey = editingModel.apiKey;
    }
    
    let updatedConfig: AiConfigurationDto;
    
    if (isNew) {
      // Add new model
      updatedConfig = {
        ...config,
        modelRegistry: [
          ...(config.modelRegistry || []),
          {
            ...modelData,
            apiKey: editingModel.apiKey, // Always include for new models
            enabled: true,
            testStatus: 0, // Untested
            lastTestedAt: undefined,
            lastTestMessage: undefined,
            maxContextTokens: 128000,
            maxOutputTokens: 16000,
            supportsFunctionCalling: true,
            costFactor: 1.0,
            customHeaders: {},
            timeoutSeconds: 120,
          },
        ],
      };
    } else {
      // Update existing model - preserve existing values if not changed
      updatedConfig = {
        ...config,
        modelRegistry: config.modelRegistry?.map(m =>
          m.id === editingModel.id
            ? {
                ...m, // Keep existing fields
                ...modelData, // Override with changed fields
                // If API key wasn't changed, preserve the existing one
                ...(modelApiKeyChanged ? { apiKey: editingModel.apiKey } : {}),
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
      
      // Update original model and reset changed flags - stay in dialog for testing
      setOriginalModel({ ...editingModel });
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
      setTestResult(result);
      
      // Update config with test result AND save to database
      const updatedConfig = {
        ...config,
        modelRegistry: config.modelRegistry?.map(m =>
          m.id === editingModel.id
            ? { 
                ...m, 
                testStatus: result.success ? 1 : 2,
                lastTestedAt: new Date().toISOString(),
                lastTestMessage: result.message 
              }
            : m
        ),
      };
      
      setConfig(updatedConfig);
      
      // Save to database
      await updateAiConfiguration(updatedConfig);
      setSuccess(result.success ? 'Model tested successfully - status saved' : 'Test failed - status saved');
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
      setEmbeddingTestResult(result);
      
      // Update config with test result AND save to database
      const updatedConfig = {
        ...config,
        embeddingRegistry: config.embeddingRegistry?.map(e =>
          e.id === editingEmbedding.id
            ? { 
                ...e, 
                testStatus: result.success ? 1 : 2,
                lastTestedAt: new Date().toISOString(),
                lastTestMessage: result.message 
              }
            : e
        ),
      };
      
      setConfig(updatedConfig);
      
      // Save to database
      await updateAiConfiguration(updatedConfig);
      setSuccess(result.success ? 'Embedding tested successfully - status saved' : 'Test failed - status saved');
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
        modelRegistry: config.modelRegistry?.filter(m => m.id !== modelId),
        // Clear tier assignments if this model was assigned
        microModelId: config.microModelId === modelId ? null : config.microModelId,
        miniModelId: config.miniModelId === modelId ? null : config.miniModelId,
        fullModelId: config.fullModelId === modelId ? null : config.fullModelId,
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
        embeddingRegistry: config.embeddingRegistry?.filter(e => e.id !== embeddingId),
        // Clear active embedding if this was it
        activeEmbeddingModelId: config.activeEmbeddingModelId === embeddingId ? null : config.activeEmbeddingModelId,
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
      baseUrl: '',
      apiKey: '',
      dimensions: 1536,
    });
    setOriginalEmbedding({
      id: '',
      displayName: '',
      modelId: '',
      baseUrl: '',
      apiKey: '',
      dimensions: 1536,
    });
    setEmbeddingTouched(false);
    setEmbeddingApiKeyChanged(false);
    setShowEmbeddingApiKey(false);
    setEmbeddingTestResult(null);
    setEmbeddingFieldErrors({});
    setEmbeddingDialogOpen(true);
  };

  const handleEditEmbedding = (embedding: AiEmbeddingModelAssignmentDto) => {
    const embeddingData = {
      id: embedding.id,
      displayName: embedding.displayName || '',
      modelId: embedding.modelId || '',
      baseUrl: embedding.baseUrl || '',
      apiKey: embedding.apiKey || '',
      dimensions: embedding.dimensions || 1536,
    };
    setEditingEmbedding(embeddingData);
    setOriginalEmbedding(embeddingData);
    setEmbeddingTouched(false);
    setEmbeddingApiKeyChanged(false);
    setShowEmbeddingApiKey(false);
    setEmbeddingTestResult(null);
    setEmbeddingFieldErrors({});
    setEmbeddingDialogOpen(true);
  };

  const handleSaveEmbedding = async () => {
    if (!config || !editingEmbedding) return;

    const isNew = !config.embeddingRegistry?.some(e => e.id === editingEmbedding.id);
    
    // Prepare embedding data - only include apiKey if it was actually changed
    const embeddingData: any = {
      id: editingEmbedding.id,
      displayName: editingEmbedding.displayName,
      modelId: editingEmbedding.modelId,
      baseUrl: editingEmbedding.baseUrl,
      dimensions: editingEmbedding.dimensions,
    };
    
    // Only include API key if it was changed (not masked value)
    if (embeddingApiKeyChanged || isNew) {
      embeddingData.apiKey = editingEmbedding.apiKey;
    }
    
    let updatedConfig: AiConfigurationDto;
    
    if (isNew) {
      updatedConfig = {
        ...config,
        embeddingRegistry: [
          ...(config.embeddingRegistry || []),
          {
            ...embeddingData,
            apiKey: editingEmbedding.apiKey, // Always include for new embeddings
            enabled: true,
            testStatus: 0,
            lastTestedAt: undefined,
            lastTestMessage: undefined,
            customHeaders: {},
            timeoutSeconds: 120,
          },
        ],
      };
    } else {
      updatedConfig = {
        ...config,
        embeddingRegistry: config.embeddingRegistry?.map(e =>
          e.id === editingEmbedding.id
            ? {
                ...e, // Keep existing fields
                ...embeddingData, // Override with changed fields
                // If API key wasn't changed, preserve the existing one
                ...(embeddingApiKeyChanged ? { apiKey: editingEmbedding.apiKey } : {}),
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
      setSuccess(isNew ? 'Embedding model added successfully' : 'Embedding model updated successfully');
      
      // Update original and reset changed flags - stay in dialog for testing
      setOriginalEmbedding({ ...editingEmbedding });
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
                onChange={(e) => setConfig({ ...config, microModelId: e.target.value || undefined })}
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
                onChange={(e) => setConfig({ ...config, activeEmbeddingModelId: e.target.value || undefined })}
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
                      <IconButton size="small" onClick={() => handleDeleteModel(model.id)} color="error">
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
                      <IconButton size="small" onClick={() => handleDeleteEmbedding(embedding.id)} color="error">
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
      <Dialog open={modelDialogOpen} onClose={() => setModelDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>{editingModel?.id && config?.modelRegistry?.some(m => m.id === editingModel.id) ? 'Edit Model' : 'Add Model'}</DialogTitle>
        <DialogContent>
          <Box display="flex" flexDirection="column" gap={2} mt={1}>
            <TextField
              label="ID"
              value={editingModel?.id || ''}
              onChange={(e) => { setEditingModel(editingModel ? { ...editingModel, id: e.target.value } : null); setModelTouched(true); }}
              disabled={!!config?.modelRegistry?.some(m => m.id === editingModel?.id)}
              fullWidth
              size="small"
              helperText={fieldErrors.id || "Unique identifier (e.g., openai-gpt4o-mini)"}
              error={!!fieldErrors.id}
            />
            <TextField
              label="Display Name"
              value={editingModel?.displayName || ''}
              onChange={(e) => { setEditingModel(editingModel ? { ...editingModel, displayName: e.target.value } : null); setModelTouched(true); }}
              fullWidth
              size="small"
              helperText={fieldErrors.displayName || "Human-readable name"}
              error={!!fieldErrors.displayName}
            />
            <TextField
              label="Model ID"
              value={editingModel?.modelId || ''}
              onChange={(e) => { setEditingModel(editingModel ? { ...editingModel, modelId: e.target.value } : null); setModelTouched(true); }}
              fullWidth
              size="small"
              placeholder="e.g., gpt-4o-mini"
              helperText={fieldErrors.modelId || "OpenAI model identifier"}
              error={!!fieldErrors.modelId}
            />
            <TextField
              label="Base URL"
              value={editingModel?.baseUrl || ''}
              onChange={(e) => { setEditingModel(editingModel ? { ...editingModel, baseUrl: e.target.value } : null); setModelTouched(true); }}
              fullWidth
              size="small"
              placeholder="https://api.openai.com/v1"
              helperText={fieldErrors.baseUrl || "Leave empty for OpenAI default"}
              error={!!fieldErrors.baseUrl}
            />
            <TextField
              label="API Key"
              value={editingModel?.apiKey || ''}
              onChange={(e) => { 
                setEditingModel(editingModel ? { ...editingModel, apiKey: e.target.value } : null); 
                setModelTouched(true);
                setModelApiKeyChanged(true);
              }}
              fullWidth
              size="small"
              type={showModelApiKey ? "text" : "password"}
              placeholder="sk-..."
              helperText={editingModel?.apiKey?.includes('...') ? "Masked - change only if updating key" : "Leave empty to use default from config"}
              InputProps={{
                endAdornment: (
                  <IconButton
                    size="small"
                    onClick={() => setShowModelApiKey(!showModelApiKey)}
                    edge="end"
                  >
                    {showModelApiKey ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                ),
              }}
            />
            
            {editingModel?.id && config?.modelRegistry?.some(m => m.id === editingModel.id) && !modelTouched && (
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
          {editingModel?.id && config?.modelRegistry?.some(m => m.id === editingModel.id) && (
            <Button 
              onClick={() => { handleDeleteModel(editingModel.id); setModelDialogOpen(false); }} 
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
        <Alert severity="success" onClose={() => setSuccess(null)}>{success}</Alert>
      </Snackbar>
      
      <Snackbar 
        open={!!error} 
        autoHideDuration={5000} 
        onClose={() => setError(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>
      </Snackbar>

      {/* Embedding Edit Dialog */}
      <Dialog open={embeddingDialogOpen} onClose={() => setEmbeddingDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>{editingEmbedding?.id && config?.embeddingRegistry?.some(e => e.id === editingEmbedding.id) ? 'Edit Embedding Model' : 'Add Embedding Model'}</DialogTitle>
        <DialogContent>
          <Box display="flex" flexDirection="column" gap={2} mt={1}>
            <TextField
              label="ID"
              value={editingEmbedding?.id || ''}
              onChange={(e) => { 
                setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, id: e.target.value } : null); 
                setEmbeddingTouched(true);
              }}
              disabled={!!config?.embeddingRegistry?.some(e => e.id === editingEmbedding?.id)}
              fullWidth
              size="small"
              helperText="Unique identifier (e.g., openai-text-embedding-3-small)"
            />
            <TextField
              label="Display Name"
              value={editingEmbedding?.displayName || ''}
              onChange={(e) => { 
                setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, displayName: e.target.value } : null); 
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
            />
            <TextField
              label="Model ID"
              value={editingEmbedding?.modelId || ''}
              onChange={(e) => { 
                setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, modelId: e.target.value } : null); 
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
              placeholder="e.g., text-embedding-3-small"
            />
            <TextField
              label="Base URL"
              value={editingEmbedding?.baseUrl || ''}
              onChange={(e) => { 
                setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, baseUrl: e.target.value } : null); 
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
              placeholder="https://api.openai.com/v1"
              helperText="Leave empty for OpenAI default"
            />
            <TextField
              label="API Key"
              value={editingEmbedding?.apiKey || ''}
              onChange={(e) => { 
                setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, apiKey: e.target.value } : null); 
                setEmbeddingTouched(true);
                setEmbeddingApiKeyChanged(true);
              }}
              fullWidth
              size="small"
              type={showEmbeddingApiKey ? "text" : "password"}
              placeholder="sk-..."
              helperText={editingEmbedding?.apiKey?.includes('...') ? "Masked - change only if updating key" : "Leave empty to use default from config"}
              InputProps={{
                endAdornment: (
                  <IconButton
                    size="small"
                    onClick={() => setShowEmbeddingApiKey(!showEmbeddingApiKey)}
                    edge="end"
                  >
                    {showEmbeddingApiKey ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                ),
              }}
            />
            <TextField
              label="Dimensions"
              value={editingEmbedding?.dimensions || 1536}
              onChange={(e) => { 
                setEditingEmbedding(editingEmbedding ? { ...editingEmbedding, dimensions: parseInt(e.target.value) || 1536 } : null); 
                setEmbeddingTouched(true);
              }}
              fullWidth
              size="small"
              type="number"
              helperText="Vector dimensions (e.g., 1536 for ada-002)"
            />
            
            {editingEmbedding?.id && config?.embeddingRegistry?.some(e => e.id === editingEmbedding.id) && !embeddingTouched && (
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
                  <Alert severity={embeddingTestResult.success ? 'success' : 'error'} sx={{ mt: 1 }}>
                    {embeddingTestResult.message}
                  </Alert>
                )}
              </Box>
            )}
          </Box>
        </DialogContent>
        <DialogActions>
          {editingEmbedding?.id && config?.embeddingRegistry?.some(e => e.id === editingEmbedding.id) && (
            <Button 
              onClick={() => { handleDeleteEmbedding(editingEmbedding.id); setEmbeddingDialogOpen(false); }} 
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
