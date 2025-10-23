import React from 'react';
import { Box, Typography } from '@mui/material';

interface Props { role: 'user' | 'assistant'; content: string; streaming?: boolean; }

export const MessageBubble: React.FC<Props> = ({ role, content, streaming }) => {
  const isUser = role === 'user';
  return (
    <Box
      sx={{
        display: 'flex',
        justifyContent: 'center',
        px: 2,
      }}
    >
      <Box
        sx={{
          width: '100%',
          maxWidth: 900,
          bgcolor: isUser ? 'rgba(255,255,255,0.05)' : 'rgba(255,255,255,0.03)',
          border: '1px solid rgba(255,255,255,0.06)',
          borderRadius: 2,
          p: 2,
          fontSize: '0.95rem',
          lineHeight: 1.5,
          position: 'relative'
        }}
      >
        <Typography component="div" sx={{ whiteSpace: 'pre-wrap' }}>{content}{streaming && <span style={{ opacity: 0.5 }}>▌</span>}</Typography>
      </Box>
    </Box>
  );
};
