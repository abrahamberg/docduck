import React, { useState, useCallback, useRef, useEffect } from 'react';
import { QueryResponse, DocumentResult, ChatStreamUpdate } from '../types';
import { postQuery, postDocSearch, postChatStream } from '../api';
import { SourceList } from './SourceList';
import { DocSearchResults } from './DocSearchResults';
import { Box, Stack, TextField, Button, Card, Typography, CircularProgress, Switch, FormControlLabel, Slider, IconButton, Popover, Divider } from '@mui/material';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import SearchIcon from '@mui/icons-material/Search';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';

interface Props {
  providerNames?: string[];
  topK?: number;
  onInteraction?: () => void;
}

export const Ask: React.FC<Props> = ({ providerNames, topK, onInteraction }) => {
  const [question, setQuestion] = useState('');
  const [messages, setMessages] = useState<Array<{ role: 'user' | 'assistant'; content: string; id: string }>>([]);
  const [response, setResponse] = useState<QueryResponse | null>(null);
  const [streamingAnswer, setStreamingAnswer] = useState<string>('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [docResults, setDocResults] = useState<DocumentResult[] | null>(null);
  const [streamMode, setStreamMode] = useState(true);
  const [searchDepth, setSearchDepth] = useState<number>(3);

  // Mode detection: landing (search-engine style) vs chat (conversation started)
  const isLandingMode = messages.length === 0 && !streamingAnswer && !response && !docResults;

  async function submit() {
    if (!question.trim()) return;
    onInteraction?.();
    setLoading(true);
    setError(null);
    setResponse(null);
    setStreamingAnswer('');
    setDocResults(null);
    try {
      const singleProviderName = providerNames && providerNames.length === 1 ? providerNames[0] : undefined;
      const userMessage = { role: 'user' as const, content: question, id: `user-${Date.now()}` };
      if (streamMode) {
        setMessages(prev => [...prev, userMessage]);
        setQuestion(''); // Clear immediately for better UX
        await postChatStream({ message: question, history: messages.map(m => ({ role: m.role, content: m.content })), topK, providerNames,
          // @ts-expect-error backward compat until backend fully migrated
          providerName: singleProviderName,
          searchDepth }, handleStreamUpdate);
      } else {
        setMessages(prev => [...prev, userMessage]);
        const resp = await postQuery({ question, providerNames, topK, searchDepth,
          // @ts-expect-error backward compat
          providerName: singleProviderName });
        setResponse(resp);
        setMessages(prev => [...prev, { role: 'assistant' as const, content: resp.answer, id: `assistant-${Date.now()}` }]);
        setQuestion('');
      }
    } catch (e: any) {
      setError(e.message || 'Error');
    } finally {
      setLoading(false);
    }
  }

  async function docSearch() {
    if (!question.trim()) return;
    onInteraction?.();
    setLoading(true);
    setError(null);
    setResponse(null);
    setDocResults(null);
    setStreamingAnswer('');
    try {
      const singleProviderName = providerNames && providerNames.length === 1 ? providerNames[0] : undefined;
      const data = await postDocSearch({ question, providerNames, topK, searchDepth,
        // @ts-expect-error backward compat
        providerName: singleProviderName });
      setDocResults(data.results);
      setQuestion('');
    } catch (e: any) {
      setError(e.message || 'Error');
    } finally {
      setLoading(false);
    }
  }

  const handleStreamUpdate = useCallback((update: ChatStreamUpdate) => {
    if (update.type === 'step' && update.message) {
      setStreamingAnswer(prev => prev + update.message);
    } else if (update.type === 'final' && update.final) {
      const final = update.final;
      const qResp: QueryResponse = {
        answer: final.answer,
        sources: final.sources || [],
        tokensUsed: final.tokensUsed || 0,
      };
      setResponse(qResp);
      setMessages(prev => [...prev, { role: 'assistant', content: final.answer, id: `assistant-${Date.now()}` }]);
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
  }, [streamingAnswer, response, docResults, messages]);

  const [settingsAnchor, setSettingsAnchor] = useState<HTMLElement | null>(null);
  const openSettings = Boolean(settingsAnchor);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  const examplePrompts = [
    'Summarize the onboarding guide',
    'List key configuration steps',
    'What providers support incremental indexing?',
  ];

  function clearAll() {
    setQuestion('');
    setResponse(null);
    setDocResults(null);
    setStreamingAnswer('');
    setMessages([]);
    setError(null);
  }

  // LANDING MODE: Search-engine inspired layout
  if (isLandingMode) {
    return (
      <Box
        sx={{
          minHeight: '100vh',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          px: 3,
          pb: 8,
        }}
      >
        {/* Logo */}
        <Box sx={{ mb: 6, textAlign: 'center' }}>
          <Typography
            variant="h2"
            sx={{
              fontWeight: 700,
              letterSpacing: '-0.03em',
              background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
              mb: 1,
            }}
          >
            DocDuck
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Ask anything about your indexed documents
          </Typography>
        </Box>

        {/* Search field */}
        <Box sx={{ width: '100%', maxWidth: 680, mb: 3 }}>
          <TextField
            value={question}
            onChange={e => setQuestion(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Search your documents or ask a question..."
            fullWidth
            variant="outlined"
            autoFocus
            sx={{
              '& .MuiOutlinedInput-root': {
                borderRadius: 6,
                backgroundColor: 'background.paper',
                fontSize: '1.1rem',
                py: 0.5,
                '&:hover': {
                  boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
                },
                '&.Mui-focused': {
                  boxShadow: '0 4px 20px rgba(102,126,234,0.3)',
                },
              },
            }}
          />
        </Box>

        {/* Buttons under search field */}
        <Stack direction="row" spacing={2} sx={{ mb: 4 }}>
          <Button
            variant="contained"
            startIcon={<SearchIcon />}
            disabled={loading || !question.trim()}
            onClick={submit}
            sx={{ 
              borderRadius: 3,
              px: 4,
              py: 1.2,
              textTransform: 'none',
              fontSize: '0.95rem',
              fontWeight: 600,
            }}
          >
            Ask DocDuck
          </Button>
          <Button
            variant="outlined"
            startIcon={<DescriptionOutlinedIcon />}
            disabled={loading || !question.trim()}
            onClick={docSearch}
            sx={{ 
              borderRadius: 3,
              px: 4,
              py: 1.2,
              textTransform: 'none',
              fontSize: '0.95rem',
              fontWeight: 600,
              borderColor: 'divider',
              color: 'text.primary',
              bgcolor: 'background.paper',
              '&:hover': {
                borderColor: 'primary.main',
                bgcolor: 'action.hover',
              },
            }}
          >
            Doc Search
          </Button>
          <IconButton 
            size="small" 
            onClick={(e) => setSettingsAnchor(e.currentTarget)} 
            aria-label="settings"
            sx={{
              bgcolor: 'background.paper',
              '&:hover': {
                bgcolor: 'action.hover',
              },
            }}
          >
            <SettingsOutlinedIcon />
          </IconButton>
        </Stack>

        {loading && (
          <CircularProgress size={32} sx={{ mb: 2 }} />
        )}

        {error && (
          <Typography color="error" variant="body2" sx={{ mb: 2 }}>
            {error}
          </Typography>
        )}

        {/* Example prompts */}
        {!loading && (
          <Stack spacing={1.5} sx={{ maxWidth: 520, width: '100%' }}>
            <Typography 
              variant="caption" 
              sx={{ 
                textAlign: 'center', 
                mb: 0.5,
                color: 'text.secondary',
                fontWeight: 500,
              }}
            >
              Try asking:
            </Typography>
            {examplePrompts.map(p => (
              <Button
                key={p}
                variant="outlined"
                onClick={() => setQuestion(p)}
                sx={{
                  justifyContent: 'flex-start',
                  textTransform: 'none',
                  borderRadius: 2,
                  py: 1.2,
                  px: 2,
                  fontSize: '0.9rem',
                  color: 'text.primary',
                  borderColor: 'divider',
                  bgcolor: 'background.paper',
                  '&:hover': {
                    borderColor: 'primary.main',
                    bgcolor: 'action.hover',
                  },
                }}
              >
                {p}
              </Button>
            ))}
          </Stack>
        )}

        <Popover
          open={openSettings}
          anchorEl={settingsAnchor}
          onClose={() => setSettingsAnchor(null)}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
          transformOrigin={{ vertical: 'top', horizontal: 'center' }}
          slotProps={{ paper: { sx: { p: 2, width: 260, mt: 1 } } }}
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
              onChange={(_e, v) => { 
                if (Array.isArray(v)) return; 
                setSearchDepth(v); 
              }}
            />
          </Stack>
        </Popover>
      </Box>
    );
  }

  // CHAT MODE: Conversation layout with search at bottom
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      {/* Logo in top-left corner */}
      <Box
        sx={{
          position: 'fixed',
          top: 16,
          left: 16,
          zIndex: 1000,
        }}
      >
        <Typography
          variant="h6"
          sx={{
            fontWeight: 700,
            letterSpacing: '-0.02em',
            background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
            WebkitBackgroundClip: 'text',
            WebkitTextFillColor: 'transparent',
            cursor: 'pointer',
          }}
          onClick={clearAll}
        >
          DocDuck
        </Typography>
      </Box>

      {/* Messages area */}
      <Box
        ref={scrollRef}
        sx={{
          flex: 1,
          overflowY: 'auto',
          px: { xs: 2, sm: 4 },
          pt: 10,
          pb: 20,
        }}
      >
        <Stack spacing={3} sx={{ maxWidth: 960, mx: 'auto' }}>
          {/* Chat history */}
          {messages.map((m) => {
            const isAssistant = m.role === 'assistant';
            return (
              <Box key={m.id} sx={{ display: 'flex', justifyContent: isAssistant ? 'flex-start' : 'flex-end' }}>
                <Card
                  elevation={0}
                  variant="outlined"
                  sx={{
                    maxWidth: '75%',
                    px: 3,
                    py: 2,
                    bgcolor: isAssistant ? 'background.paper' : 'primary.dark',
                    color: isAssistant ? 'text.primary' : 'primary.contrastText',
                    borderRadius: 3,
                    borderColor: theme => isAssistant ? theme.palette.divider : theme.palette.primary.dark,
                  }}
                >
                  <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
                    {m.content}
                  </Typography>
                </Card>
              </Box>
            );
          })}

          {error && (
            <Typography color="error" variant="body2" sx={{ textAlign: 'center' }}>
              {error}
            </Typography>
          )}

          {streamingAnswer && !response && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
              <Card
                variant="outlined"
                sx={{
                  maxWidth: '75%',
                  px: 3,
                  py: 2,
                  bgcolor: 'background.paper',
                  borderRadius: 3,
                }}
              >
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
                  Thinking...
                </Typography>
                <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
                  {streamingAnswer}
                </Typography>
              </Card>
            </Box>
          )}

          {response && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
              <Box sx={{ maxWidth: '85%' }}>
                <Card
                  variant="outlined"
                  sx={{
                    px: 3,
                    py: 2,
                    bgcolor: 'background.paper',
                    borderRadius: 3,
                    mb: 2,
                  }}
                >
                  <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.6, mb: 2 }}>
                    {response.answer}
                  </Typography>
                  <SourceList sources={response.sources} />
                  <Typography variant="caption" sx={{ mt: 2, display: 'block', opacity: 0.6 }}>
                    Tokens used: {response.tokensUsed}
                  </Typography>
                </Card>
              </Box>
            </Box>
          )}

          {docResults && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
              <Box sx={{ maxWidth: '85%' }}>
                <Card
                  variant="outlined"
                  sx={{
                    px: 3,
                    py: 2,
                    bgcolor: 'background.paper',
                    borderRadius: 3,
                  }}
                >
                  <Typography variant="subtitle2" gutterBottom sx={{ opacity: 0.7 }}>
                    Document Search Results
                  </Typography>
                  <DocSearchResults results={docResults} />
                </Card>
              </Box>
            </Box>
          )}
        </Stack>
      </Box>

      {/* Search box at bottom (ChatGPT style) */}
      <Box
        sx={{
          position: 'fixed',
          bottom: 0,
          left: 0,
          right: 0,
          bgcolor: 'background.default',
          borderTop: theme => `1px solid ${theme.palette.divider}`,
          py: 2,
          px: { xs: 2, sm: 4 },
          zIndex: 100,
        }}
      >
        <Stack
          spacing={1}
          sx={{
            width: '100%',
            mx: 'auto',
            maxWidth: 960,
          }}
        >
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-end' }}>
            <TextField
              value={question}
              onChange={e => setQuestion(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Send a message..."
              multiline
              maxRows={4}
              fullWidth
              variant="outlined"
              size="small"
              sx={{
                '& .MuiOutlinedInput-root': {
                  borderRadius: 3,
                  backgroundColor: 'background.paper',
                }
              }}
            />
            <IconButton
              size="small"
              onClick={(e) => setSettingsAnchor(e.currentTarget)}
              aria-label="settings"
              sx={{ mb: 0.5 }}
            >
              <SettingsOutlinedIcon fontSize="small" />
            </IconButton>
          </Box>
          <Stack direction="row" spacing={1} sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
            <Stack direction="row" spacing={1}>
              <Button
                variant="contained"
                size="small"
                disabled={loading || !question.trim()}
                onClick={submit}
                sx={{ textTransform: 'none' }}
              >
                Ask
              </Button>
              <Button
                variant="outlined"
                size="small"
                disabled={loading || !question.trim()}
                onClick={docSearch}
                sx={{ textTransform: 'none' }}
              >
                Doc Search
              </Button>
              <Button
                variant="outlined"
                color="inherit"
                size="small"
                onClick={clearAll}
                sx={{ textTransform: 'none' }}
              >
                New Chat
              </Button>
            </Stack>
            {loading && <CircularProgress size={20} />}
          </Stack>
        </Stack>
      </Box>

      <Popover
        open={openSettings}
        anchorEl={settingsAnchor}
        onClose={() => setSettingsAnchor(null)}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        slotProps={{ paper: { sx: { p: 2, width: 260 } } }}
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
            onChange={(_e, v) => { 
              if (Array.isArray(v)) return; 
              setSearchDepth(v); 
            }}
          />
        </Stack>
      </Popover>
    </Box>
  );
};
