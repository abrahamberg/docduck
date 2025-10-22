import React, { useState, useCallback } from 'react';
import { QueryResponse, DocumentResult, ChatStreamUpdate, ChatResponse } from '../types';
import { postQuery, postDocSearch, postChatStream } from '../api';
import { SourceList } from './SourceList';
import { DocSearchResults } from './DocSearchResults';
import { Box, Stack, TextField, Button, Card, CardContent, Typography, CircularProgress, Tooltip, Switch, FormControlLabel } from '@mui/material';

interface Props {
  providerType?: string;
  providerName?: string;
  topK?: number;
  searchDepth?: number;
}

export const Ask: React.FC<Props> = ({ providerType, providerName, topK, searchDepth }) => {
  const [question, setQuestion] = useState('');
  const [response, setResponse] = useState<QueryResponse | null>(null);
  const [streamingAnswer, setStreamingAnswer] = useState<string>('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [docResults, setDocResults] = useState<DocumentResult[] | null>(null);
  const [streamMode, setStreamMode] = useState(false);
  // searchDepth provided by App via prop
  const depth = searchDepth ?? 3;

  async function submit() {
    if (!question.trim()) return;
    setLoading(true);
    setError(null);
    setResponse(null);
    setStreamingAnswer('');
      try {
      if (streamMode) {
        // Use the streaming chat endpoint with empty history to stream a single-answer flow
        await postChatStream({ message: question, history: [], topK, providerType, providerName, searchDepth: depth }, handleStreamUpdate);
      } else {
        const resp = await postQuery({ question, providerType, providerName, topK, searchDepth: depth });
        setResponse(resp);
      }
    } catch (e: any) {
      setError(e.message || 'Error');
    } finally {
      setLoading(false);
    }
  }

  async function docSearch() {
    if (!question.trim()) return;
    setLoading(true);
    setError(null);
    setResponse(null);
    setDocResults(null);
    try {
      const data = await postDocSearch({ question, providerType, providerName, topK, searchDepth: depth });
      setDocResults(data.results);
    } catch (e: any) {
      setError(e.message || 'Error');
    } finally {
      setLoading(false);
    }
  }

  const handleStreamUpdate = useCallback((update: ChatStreamUpdate) => {
    if (update.type === 'step' && update.message) {
      // append partial messages
      setStreamingAnswer(prev => prev + update.message);
    } else if (update.type === 'final' && update.final) {
      const final = update.final as ChatResponse;
      const qResp: QueryResponse = {
        answer: final.answer,
        sources: final.sources || [],
        tokensUsed: final.tokensUsed || 0,
      };
      setResponse(qResp);
      // Don't set docResults here - sources already shown in Answer section
      // docResults is only for explicit "Doc Search" button clicks
    } else if (update.type === 'error' && update.message) {
      setError(update.message);
    }
  }, []);

  return (
    <Stack sx={{ height: '100%' }}>
      <Box sx={{ p: 2, flex: 1, overflowY: 'auto' }}>
        <Stack spacing={2}>
          <TextField
            value={question}
            onChange={e => setQuestion(e.target.value)}
            placeholder="Type a question about your indexed documents…"
            multiline
            minRows={3}
            maxRows={8}
            fullWidth
          />
          {/* Search depth controlled globally in the App header */}
          <Stack direction="row" spacing={1} alignItems="center">
            <FormControlLabel
              control={<Switch size="small" checked={streamMode} onChange={(_, v) => setStreamMode(v)} disabled={loading} />}
              label="Show thinking"
            />
            
          </Stack>
          <Stack direction="row" spacing={1}>
            <Tooltip title="Ask">
              <span>
                <Button variant="contained" disabled={loading || !question.trim()} onClick={submit}>Ask</Button>
              </span>
            </Tooltip>
            <Tooltip title="Clear">
              <span>
                <Button variant="outlined" color="inherit" disabled={loading && !response} onClick={() => { setQuestion(''); setResponse(null); }}>Clear</Button>
              </span>
            </Tooltip>
            <Tooltip title="Document search (top 5)">
              <span>
                <Button variant="text" disabled={loading || !question.trim()} onClick={docSearch}>Doc Search</Button>
              </span>
            </Tooltip>
            {loading && <CircularProgress size={24} />}
          </Stack>
          {error && <Typography color="error" variant="body2">{error}</Typography>}
          {streamingAnswer && !response && (
            <Card variant="outlined">
              <CardContent>
                <Typography variant="h6" gutterBottom>Answer (streaming)</Typography>
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', mb: 2 }}>{streamingAnswer}</Typography>
              </CardContent>
            </Card>
          )}
          {response && (
            <Card variant="outlined">
              <CardContent>
                <Typography variant="h6" gutterBottom>Answer</Typography>
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', mb: 2 }}>{response.answer}</Typography>
                <SourceList sources={response.sources} />
                <Typography variant="caption" sx={{ mt: 2, display: 'block', opacity: 0.7 }}>Tokens used: {response.tokensUsed}</Typography>
              </CardContent>
            </Card>
          )}
          {docResults && (
            <Card variant="outlined" sx={{ mt: 2 }}>
              <CardContent>
                <Typography variant="h6" gutterBottom>Document Search Results</Typography>
                <DocSearchResults results={docResults} />
              </CardContent>
            </Card>
          )}
        </Stack>
      </Box>
    </Stack>
  );
};
