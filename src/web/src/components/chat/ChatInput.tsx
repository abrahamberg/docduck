import React, { useRef, useEffect } from 'react';
import { Box, TextField, IconButton, Tooltip } from '@mui/material';
import SendIcon from '@mui/icons-material/Send';
import StopIcon from '@mui/icons-material/Stop';

interface Props {
  value: string;
  onChange: (v: string) => void;
  onSubmit: () => void;
  disabled?: boolean;
  streaming?: boolean;
  onStop?: () => void;
}

export const ChatInput: React.FC<Props> = ({ value, onChange, onSubmit, disabled, streaming, onStop }) => {
  const ref = useRef<HTMLInputElement | null>(null);
  useEffect(() => { ref.current?.focus(); }, []);
  return (
    <Box sx={{ borderTop: '1px solid rgba(255,255,255,0.08)', p: 2, bgcolor: 'rgba(0,0,0,0.4)' }}>
      <Box sx={{ display: 'flex', maxWidth: 900, mx: 'auto', gap: 1 }}>
        <TextField
          fullWidth
          placeholder="Ask anything"
          value={value}
          onChange={e => onChange(e.target.value)}
          inputRef={ref}
          onKeyDown={e => { if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') { e.preventDefault(); onSubmit(); } }}
          sx={{
            '& .MuiOutlinedInput-root': {
              bgcolor: '#40414f',
              borderRadius: 999,
              paddingLeft: 2,
              transition: 'background-color 0.15s',
              '& fieldset': { border: '1px solid rgba(255,255,255,0.15)' },
              '&:hover fieldset': { borderColor: 'rgba(255,255,255,0.30)' },
              '&.Mui-focused fieldset': { borderColor: 'rgba(255,255,255,0.40)' },
            }
          }}
        />
        {streaming ? (
          <Tooltip title="Stop streaming">
            <span>
              <IconButton color="error" onClick={onStop} disabled={disabled} sx={{ bgcolor: 'rgba(255,255,255,0.08)', borderRadius: 2 }}>
                <StopIcon />
              </IconButton>
            </span>
          </Tooltip>
        ) : (
          <Tooltip title="Send (Ctrl+Enter)">
            <span>
              <IconButton color="primary" onClick={onSubmit} disabled={disabled || !value.trim()} sx={{ bgcolor: 'rgba(255,255,255,0.08)', borderRadius: 2 }}>
                <SendIcon />
              </IconButton>
            </span>
          </Tooltip>
        )}
      </Box>
    </Box>
  );
};
