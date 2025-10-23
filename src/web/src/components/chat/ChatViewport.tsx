import React, { useEffect, useRef } from 'react';
import { Box } from '@mui/material';
import { MessageBubble } from './MessageBubble';

export interface ChatMessageItem { id: string; role: 'user' | 'assistant'; content: string; streaming?: boolean; }

interface Props { messages: ChatMessageItem[]; }

export const ChatViewport: React.FC<Props> = ({ messages }) => {
  const endRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages]);
  return (
    <Box sx={{ flex: 1, overflowY: 'auto', py: 4 }}>
      {messages.map(m => <MessageBubble key={m.id} role={m.role} content={m.content} streaming={m.streaming} />)}
      <div ref={endRef} />
    </Box>
  );
};
