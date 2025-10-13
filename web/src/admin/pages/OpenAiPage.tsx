import React, { useEffect, useState } from 'react';
import { Alert, Box, Button, CircularProgress, FormControlLabel, Paper, Switch, TextField, Typography } from '@mui/material';
import { getOpenAiSettings, updateOpenAiSettings } from '../api';

const defaultState = {
  enabled: true,
  apiKey: '',
  baseUrl: 'https://api.openai.com/v1/',
  chatModel: 'gpt-4o-mini',
  chatModelSmall: 'gpt-4o-mini',
  chatModelLarge: 'gpt-4o-mini',
  embedModel: 'text-embedding-3-small',
  embedBatchSize: 16,
  maxTokens: 1000,
  temperature: 0.7,
  refineSystemPrompt: '',
};

export const OpenAiPage: React.FC = () => {
  const [state, setState] = useState(defaultState);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setLoading(true);
        const response = await getOpenAiSettings();
        setState(prev => ({ ...prev, ...response.settings }));
      } catch (err: any) {
        setError(err.message || 'Failed to load OpenAI settings');
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const handleChange = <K extends keyof typeof state>(key: K) => (event: React.ChangeEvent<HTMLInputElement>) => {
    const value = event.target.type === 'number' ? Number(event.target.value) : event.target.value;
    setState(prev => ({ ...prev, [key]: value }));
  };

  const handleToggle = (event: React.ChangeEvent<HTMLInputElement>) => {
    setState(prev => ({ ...prev, enabled: event.target.checked }));
  };

  const handleSave = async () => {
    try {
      setSaving(true);
      setError(null);
      setSuccess(null);
      await updateOpenAiSettings(state);
      setSuccess('Settings saved successfully.');
    } catch (err: any) {
      setError(err.message || 'Failed to save settings');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <CircularProgress />;
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Typography variant="h5" sx={{ fontWeight: 600 }}>OpenAI Settings</Typography>
      {error && <Alert severity="error">{error}</Alert>}
      {success && <Alert severity="success">{success}</Alert>}
      <Paper sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <FormControlLabel control={<Switch checked={state.enabled} onChange={handleToggle} />} label="Enabled" />
        <TextField label="API Key" type="password" value={state.apiKey} onChange={handleChange('apiKey')} />
        <TextField label="Base URL" value={state.baseUrl} onChange={handleChange('baseUrl')} />
        <TextField label="Chat Model" value={state.chatModel} onChange={handleChange('chatModel')} />
        <TextField label="Chat Model (Small)" value={state.chatModelSmall} onChange={handleChange('chatModelSmall')} />
        <TextField label="Chat Model (Large)" value={state.chatModelLarge} onChange={handleChange('chatModelLarge')} />
        <TextField label="Embedding Model" value={state.embedModel} onChange={handleChange('embedModel')} />
        <TextField label="Embedding Batch Size" type="number" value={state.embedBatchSize} onChange={handleChange('embedBatchSize')} />
        <TextField label="Max Tokens" type="number" value={state.maxTokens} onChange={handleChange('maxTokens')} />
        <TextField label="Temperature" type="number" inputProps={{ step: 0.1 }} value={state.temperature} onChange={handleChange('temperature')} />
        <TextField
          label="Refine System Prompt"
          value={state.refineSystemPrompt}
          onChange={handleChange('refineSystemPrompt')}
          multiline
          minRows={3}
        />
        <Button variant="contained" onClick={handleSave} disabled={saving}>
          {saving ? 'Saving…' : 'Save Changes'}
        </Button>
      </Paper>
    </Box>
  );
};
