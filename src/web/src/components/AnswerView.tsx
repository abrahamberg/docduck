import React from 'react';
import { Box, Typography, LinearProgress } from '@mui/material';
import { QueryResponse } from '../types';
import { SourceAccordion } from './SourceAccordion';

interface Props { streamingText: string; response: QueryResponse | null; loading: boolean; tokens?: number; }

export const AnswerView: React.FC<Props> = ({ streamingText, response, loading, tokens }) => {
  return (
    <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', p: 2 }}>
      {loading && !streamingText && !response && <LinearProgress sx={{ mb: 2 }} />}
      {streamingText && !response && (
        <Box sx={{ mb: 2 }} aria-live="polite">
          <Typography variant="subtitle2" sx={{ mb: 1 }}>Answer (streaming)</Typography>
          <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>{streamingText}<span style={{ opacity: 0.5 }}>|</span></Typography>
        </Box>
      )}
      {response && (
        <Box sx={{ mb: 2 }}>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>Answer</Typography>
          <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', mb: 2 }}>{response.answer}</Typography>
          <SourceAccordion sources={response.sources} />
          <Typography variant="caption" sx={{ mt: 2, display: 'block', opacity: 0.7 }}>Tokens used: {tokens ?? response.tokensUsed}</Typography>
        </Box>
      )}
    </Box>
  );
};
