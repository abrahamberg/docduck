import React, { useState, useCallback, useRef, useEffect } from 'react';
import { QueryResponse, DocumentResult, ChatStreamUpdate, ChatResponse } from '../types';
import { postQuery, postDocSearch, postChatStream } from '../api';
import { SourceList } from './SourceList';
import { DocSearchResults } from './DocSearchResults';
import { Box, Stack, TextField, Button, Card, CardContent, Typography, CircularProgress, Tooltip, Switch, FormControlLabel, Slider, IconButton, Popover, Divider } from '@mui/material';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';

interface Props {
  providerNames?: string[];
  topK?: number;
}

export const Ask: React.FC<Props> = ({ providerNames, topK }) => {
  const [question, setQuestion] = useState('');
  const [messages, setMessages] = useState<Array<{ role: 'user' | 'assistant'; content: string }>>([]);
  const [response, setResponse] = useState<QueryResponse | null>(null);
  const [streamingAnswer, setStreamingAnswer] = useState<string>('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [docResults, setDocResults] = useState<DocumentResult[] | null>(null);
  const [streamMode, setStreamMode] = useState(true);
  const [searchDepth, setSearchDepth] = useState<number>(3);

  async function submit() {
    if (!question.trim()) return;
    setLoading(true);
    setError(null);
    setResponse(null);
    setStreamingAnswer('');
      try {
      const singleProviderName = providerNames && providerNames.length === 1 ? providerNames[0] : undefined;
      if (streamMode) {
        // Streaming chat; backend may still accept legacy providerName for single selection
        setMessages(prev => [...prev, { role: 'user', content: question }]);
        await postChatStream({ message: question, history: messages, topK, providerNames, // new multi
          // @ts-expect-error backward compat until backend fully migrated
          providerName: singleProviderName,
          searchDepth }, handleStreamUpdate);
      } else {
        setMessages(prev => [...prev, { role: 'user', content: question }]);
        const resp = await postQuery({ question, providerNames, topK, searchDepth, 
          // @ts-expect-error backward compat
          providerName: singleProviderName });
        setResponse(resp);
        setMessages(prev => [...prev, { role: 'assistant', content: resp.answer }]);
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
      const singleProviderName = providerNames && providerNames.length === 1 ? providerNames[0] : undefined;
      const data = await postDocSearch({ question, providerNames, topK, searchDepth, 
        // @ts-expect-error backward compat
        providerName: singleProviderName });
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
      setMessages(prev => [...prev, { role: 'assistant', content: final.answer }]);
      // Don't set docResults here - sources already shown in Answer section
      // docResults is only for explicit "Doc Search" button clicks
    } else if (update.type === 'error' && update.message) {
      setError(update.message);
    }
  }, []);

  // Auto scroll to bottom when streaming or new answer
  const scrollRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [streamingAnswer, response, docResults]);

  const [settingsAnchor, setSettingsAnchor] = useState<HTMLElement | null>(null);
  const openSettings = Boolean(settingsAnchor);

  const examplePrompts = [
    'Summarize the onboarding guide',
    'List key configuration steps',
    'What providers support incremental indexing?',
  ];

  const showEmptyHero = messages.length === 0 && !streamingAnswer && !response && !docResults && !loading;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      {/* Messages area */}
      <Box ref={scrollRef} sx={{ flex: 1, overflowY: 'auto', px: { xs: 2, sm: 4 }, py: 3 }}>
        <Stack spacing={3} sx={{ maxWidth: 960, mx: 'auto', pb: 10 }}>
          {showEmptyHero && (
            <Box sx={{ textAlign: 'center', mt: { xs: 4, sm: 8 } }}>
              <Typography variant="h4" sx={{ fontWeight: 600, letterSpacing: '-0.5px', mb: 2 }}>Hi there 👋</Typography>
              <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
                Ask DocDuck anything about your indexed documents.
              </Typography>
              <Stack spacing={1} sx={{ maxWidth: 500, mx: 'auto' }}>
                {examplePrompts.map(p => (
                  <Button key={p} variant="outlined" color="inherit" onClick={() => setQuestion(p)} sx={{ justifyContent: 'flex-start', textTransform: 'none' }}>
                    {p}
                  </Button>
                ))}
              </Stack>
            </Box>
          )}
          {/* Chat history */}
          {messages.map((m, i) => {
            const isAssistant = m.role === 'assistant';
            return (
              <Box key={i} sx={{ display: 'flex', justifyContent: isAssistant ? 'center' : 'flex-end', px: 1 }}>
                <Card
                  elevation={0}
                  variant="outlined"
                  sx={{
                    width: '100%',
                    maxWidth: isAssistant ? 760 : 520,
                    px: 3,
                    py: 2,
                    bgcolor: isAssistant ? 'background.paper' : 'primary.dark',
                    color: isAssistant ? 'text.primary' : 'primary.contrastText',
                    borderRadius: 3,
                    borderColor: theme => isAssistant ? theme.palette.divider : theme.palette.primary.dark,
                  }}
                >
                  <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.5 }}>{m.content}</Typography>
                </Card>
              </Box>
            );
          })}
          {error && <Typography color="error" variant="body2">{error}</Typography>}
          {streamingAnswer && !response && (
            <Card variant="outlined" sx={{ bgcolor: 'background.paper' }}>
              <CardContent>
                <Typography variant="subtitle2" gutterBottom sx={{ opacity: 0.7 }}>Assistant (thinking)</Typography>
                <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>{streamingAnswer}</Typography>
              </CardContent>
            </Card>
          )}
          {response && (
            <Card variant="outlined" sx={{ bgcolor: 'background.paper' }}>
              <CardContent>
                <Typography variant="subtitle2" gutterBottom sx={{ opacity: 0.7 }}>Assistant</Typography>
                <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', mb: 2 }}>{response.answer}</Typography>
                <SourceList sources={response.sources} />
                <Typography variant="caption" sx={{ mt: 2, display: 'block', opacity: 0.6 }}>Tokens used: {response.tokensUsed}</Typography>
              </CardContent>
            </Card>
          )}
          {docResults && (
            <Card variant="outlined" sx={{ bgcolor: 'background.paper' }}>
              <CardContent>
                <Typography variant="subtitle2" gutterBottom sx={{ opacity: 0.7 }}>Document Search Results</Typography>
                <DocSearchResults results={docResults} />
              </CardContent>
            </Card>
          )}
        </Stack>
      </Box>
      {/* Composer */}
      <Box sx={{ position: 'relative' }}>
        <Box sx={{ position: 'absolute', inset: 0, pointerEvents: 'none', background: 'linear-gradient(to top, rgba(15,17,21,0.9), rgba(15,17,21,0))' }} />
      </Box>
      <Box sx={{ position: 'sticky', bottom: 0, px: { xs: 2, sm: 4 }, pb: 2 }}>
        <Stack
          spacing={1}
          sx={{
            width: '100%',
            mx: 'auto',
            maxWidth: { xs: 600, sm: 760, md: 840, lg: 960 },
            transition: 'max-width .25s ease',
          }}
        >
          <TextField
            value={question}
            onChange={e => setQuestion(e.target.value)}
            placeholder="Ask anything about your indexed documents…"
            multiline
            minRows={4}
            maxRows={12}
            fullWidth
            variant="outlined"
            sx={{ '& .MuiOutlinedInput-root': { borderRadius: 3, backgroundColor: 'background.paper' } }}
          />
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" sx={{ justifyContent: 'space-between' }}>
            <Stack direction="row" spacing={1} alignItems="center">
              <IconButton size="small" onClick={(e) => setSettingsAnchor(e.currentTarget)} aria-label="settings">
                <SettingsOutlinedIcon fontSize="small" />
              </IconButton>
            </Stack>
            <Stack direction="row" spacing={1} flexWrap="wrap" sx={{ alignItems: 'center' }}>
              <Button variant="contained" disabled={loading || !question.trim()} onClick={submit}>Ask</Button>
              <Button variant="outlined" disabled={loading || !question.trim()} onClick={docSearch}>Doc Search</Button>
              <Button variant="outlined" color="inherit" disabled={loading && !response} onClick={() => { setQuestion(''); setResponse(null); setDocResults(null); setStreamingAnswer(''); setMessages([]); }}>Clear</Button>
              {loading && <CircularProgress size={24} sx={{ ml: 1 }} />}
            </Stack>
          </Stack>
        </Stack>
      </Box>
      <Popover
        open={openSettings}
        anchorEl={settingsAnchor}
        onClose={() => setSettingsAnchor(null)}
        anchorOrigin={{ vertical: 'top', horizontal: 'left' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        PaperProps={{ sx: { p: 2, width: 260 } }}
      >
        <Stack spacing={1}>
          <FormControlLabel
            control={<Switch size="small" checked={streamMode} onChange={(_, v) => setStreamMode(v)} disabled={loading} />}
            label="Show thinking"
          />
          <Divider sx={{ my: 1 }} />
          <Typography variant="caption" sx={{ fontWeight: 600 }}>Search depth: {searchDepth}</Typography>
          <Slider
            size="small"
            min={1}
            max={5}
            step={1}
            marks
            value={searchDepth}
            onChange={(_e, v) => { if (Array.isArray(v)) return; setSearchDepth(v); }}
          />
        </Stack>
      </Popover>
    </Box>
  );
};
