import React, { useState, useRef, useEffect, useCallback } from 'react';
import { ChatMessage, ChatResponse, ChatStreamUpdate } from '../types';
import { postChat, postChatStream } from '../api';
import { SourceList } from './SourceList';
import { DocSearchResults } from './DocSearchResults';
import { Box, TextField, IconButton, Paper, Typography, Stack, CircularProgress, Tooltip, Switch, FormControlLabel } from '@mui/material';
import SendIcon from '@mui/icons-material/Send';
import RestartAltIcon from '@mui/icons-material/RestartAlt';

interface Props {
  providerType?: string;
  providerName?: string;
  topK?: number;
}

export const Chat: React.FC<Props> = ({ providerType, providerName, topK }) => {
  const [history, setHistory] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [lastResponse, setLastResponse] = useState<ChatResponse | null>(null);
  const [streamMode, setStreamMode] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  const handleStreamUpdate = useCallback((update: ChatStreamUpdate) => {
    if (update.type === 'step' && update.message) {
      setHistory(prev => [...prev, { role: 'assistant', content: update.message! }]);
    } else if (update.type === 'final' && update.final) {
      setLastResponse(update.final);
      setHistory([...update.final.history]);
    } else if (update.type === 'error' && update.message) {
      setError(update.message);
    }
  }, []);

  async function send() {
    if (!input.trim() || loading) return;
    setLoading(true);
    setError(null);
    const newHistory: ChatMessage[] = [...history, { role: 'user', content: input }];
    setHistory(newHistory);
    setLastResponse(null);
    setInput('');
    const request = {
      message: newHistory[newHistory.length - 1].content,
      history: newHistory.slice(0, -1),
      topK,
      providerType,
      providerName,
      streamSteps: streamMode,
    };

    try {
      if (streamMode) {
        await postChatStream(request, handleStreamUpdate);
      } else {
        const resp = await postChat(request);
        setLastResponse(resp);
        setHistory([...resp.history]);
      }
    } catch (e: any) {
      setError(e.message || 'Error');
    } finally {
      setLoading(false);
    }
  }

  function resetConversation() {
    setHistory([]);
    setLastResponse(null);
    setError(null);
  }

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [history, loading]);

  return (
    <Stack sx={{ height: '100%', width: '100%' }}>
      <Box ref={scrollRef} sx={{ flex: 1, overflowY: 'auto', p: 2 }}>
        <Stack spacing={2}>
          {history.map((m, i) => (
            <Box key={i} sx={{ display: 'flex', justifyContent: m.role === 'user' ? 'flex-end' : 'flex-start' }}>
              <Paper elevation={0} sx={{ p: 1.5, maxWidth: '75%', bgcolor: m.role === 'user' ? 'primary.dark' : 'background.paper', border: theme => `1px solid ${theme.palette.divider}` }}>
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>{m.content}</Typography>
              </Paper>
            </Box>
          ))}
          {loading && <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}><CircularProgress size={20} /><Typography variant="caption">Generating…</Typography></Box>}
          {error && <Typography color="error" variant="body2">{error}</Typography>}
          {lastResponse && !loading && (
            <>
              <DocSearchResults results={lastResponse.files} />
              <SourceList sources={lastResponse.sources} />
            </>
          )}
        </Stack>
      </Box>
      <Stack direction="row" spacing={1} sx={{ p: 1.5, borderTop: theme => `1px solid ${theme.palette.divider}` }}>
        <TextField
          value={input}
          onChange={e => setInput(e.target.value)}
          placeholder="Ask something about your documents…"
          multiline
          maxRows={6}
          fullWidth
          size="small"
        />
        <FormControlLabel
          control={
            <Switch
              size="small"
              checked={streamMode}
              onChange={(_, checked) => setStreamMode(checked)}
              disabled={loading}
            />
          }
          label="Show thinking"
          sx={{ mr: 1 }}
        />
        <Tooltip title="Reset conversation">
          <span>
            <IconButton disabled={loading || history.length === 0} onClick={resetConversation} color="warning">
              <RestartAltIcon />
            </IconButton>
          </span>
        </Tooltip>
        <Tooltip title="Send">
          <span>
            <IconButton onClick={send} disabled={loading || !input.trim()} color="primary">
              {loading ? <CircularProgress size={20} /> : <SendIcon />}
            </IconButton>
          </span>
        </Tooltip>
      </Stack>
    </Stack>
  );
};
