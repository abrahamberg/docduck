import React, { useEffect, useState, useCallback } from 'react';
import { ThemeProvider } from '@mui/material/styles';
import { Box, Typography } from '@mui/material';
import StreamIcon from '@mui/icons-material/Polyline';
import LayersIcon from '@mui/icons-material/Layers';
import { theme } from './theme';
import { getProviders, getHealth, postChatStream } from './api';
import { ProviderInfo, HealthStatus, ChatStreamUpdate, ChatResponse } from './types';
import { Sidebar, ConversationSummary } from './components/chat/Sidebar';
import { ChatViewport, ChatMessageItem } from './components/chat/ChatViewport';
import { ChatInput } from './components/chat/ChatInput';

export const App: React.FC = () => {
  const [providers, setProviders] = useState<ProviderInfo[]>([]); // reserved for future provider filtering
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState<ChatMessageItem[]>([]);
  const [streaming, setStreaming] = useState(false);
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [activeConversationId, setActiveConversationId] = useState<string | undefined>();
  const streamMode = true; // always streaming for now
  const searchDepth = 3;   // static depth placeholder

  useEffect(() => {
    (async () => {
      const p = await getProviders().catch(() => []);
      setProviders(p);
      const raw = localStorage.getItem('dd:conversations');
      if (raw) {
        try { const parsed = JSON.parse(raw); if (Array.isArray(parsed)) setConversations(parsed); } catch {}
      }
    })();
  }, []);

  useEffect(() => { (async () => { const h = await getHealth().catch(() => null); setHealth(h); })(); }, []);

  useEffect(() => { localStorage.setItem('dd:conversations', JSON.stringify(conversations)); }, [conversations]);

  function newConversation(initialTitle?: string) {
    const id = crypto.randomUUID();
    const titleBase = initialTitle?.trim() || 'New chat';
    const conv: ConversationSummary = { id, title: titleBase.slice(0, 60), createdAt: Date.now() };
    setConversations(c => [conv, ...c]);
    setMessages([]);
    setActiveConversationId(id);
  }

  function openConversation(id: string) {
    setActiveConversationId(id);
    setMessages([]);
    setInput('');
  }

  const handleStreamUpdate = useCallback((update: ChatStreamUpdate) => {
    if (update.type === 'step' && update.message) {
      setMessages(m => {
        const last = m[m.length - 1];
        if (!last || last.role !== 'assistant' || !last.streaming) {
          return [...m, { id: crypto.randomUUID(), role: 'assistant', content: update.message || '', streaming: true }];
        }
        return [...m.slice(0, -1), { ...last, content: last.content + (update.message || '') }];
      });
    } else if (update.type === 'final' && update.final) {
      const final = update.final as ChatResponse;
      setMessages(m => {
        const last = m[m.length - 1];
        if (last && last.role === 'assistant' && last.streaming) {
          return [...m.slice(0, -1), { ...last, streaming: false, content: final.answer }];
        }
        return [...m, { id: crypto.randomUUID(), role: 'assistant', streaming: false, content: final.answer }];
      });
    } else if (update.type === 'error' && update.message) {
      setMessages(m => [...m, { id: crypto.randomUUID(), role: 'assistant', streaming: false, content: `Error: ${update.message}` }]);
    }
  }, []);

  async function send() {
    if (!input.trim() || streaming) return;
    const prompt = input.trim();
    setInput('');
    const userMsg: ChatMessageItem = { id: crypto.randomUUID(), role: 'user', content: prompt };
    setMessages(m => [...m, userMsg]);
    if (!activeConversationId) newConversation(prompt);
    setStreaming(true);
    try {
      await postChatStream({ message: prompt, history: [], providerNames: undefined, searchDepth }, handleStreamUpdate);
    } catch (e: any) {
      setMessages(m => [...m, { id: crypto.randomUUID(), role: 'assistant', content: `Error: ${e.message}` }]);
    } finally {
      setStreaming(false);
    }
  }

  function stopStreaming() { setStreaming(false); }

  const empty = messages.length === 0;

  return (
    <ThemeProvider theme={theme}>
      <Box sx={{ display: 'flex', height: '100vh', bgcolor: '#343541', color: 'rgba(255,255,255,0.95)' }}>
        <Sidebar conversations={conversations} onNew={() => newConversation()} onOpen={openConversation} activeId={activeConversationId} />
        <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
          {empty ? (
            <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', textAlign: 'center', px: 2 }}>
              <Typography variant="h5" sx={{ mb: 3 }}>What are you working on?</Typography>
              <Box sx={{ width: '100%', maxWidth: 900 }}>
                <ChatInput value={input} onChange={setInput} onSubmit={send} disabled={streaming} streaming={false} />
              </Box>
            </Box>
          ) : (
            <>
              <ChatViewport messages={messages} />
              <ChatInput value={input} onChange={setInput} onSubmit={send} disabled={streaming} streaming={streaming} onStop={stopStreaming} />
            </>
          )}
          <Box sx={{ position: 'absolute', top: 8, right: 12, display: 'flex', gap: 1 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, bgcolor: 'rgba(0,0,0,0.3)', px: 2, py: 1, borderRadius: 2, fontSize: '0.75rem' }}>
              <StreamIcon fontSize="small" sx={{ opacity: 0.7 }} /> {streamMode ? 'Streaming' : 'Final-only'}
              <LayersIcon fontSize="small" sx={{ opacity: 0.7 }} /> Depth {searchDepth}
              {health && <Typography variant="caption" sx={{ opacity: 0.7 }}>Docs {health.documents} • Chunks {health.chunks}</Typography>}
            </Box>
          </Box>
        </Box>
      </Box>
    </ThemeProvider>
  );
};
