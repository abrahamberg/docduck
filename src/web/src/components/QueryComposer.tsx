import React, { useRef, useEffect, forwardRef, useImperativeHandle } from 'react';
import { TextField, Box } from '@mui/material';

interface Props { value: string; onChange: (v: string) => void; onSubmit: () => void; }
export interface QueryComposerHandle { focus: () => void; }

export const QueryComposer = forwardRef<QueryComposerHandle, Props>(({ value, onChange, onSubmit }, ref) => {
  const inputRef = useRef<HTMLTextAreaElement | null>(null);
  useEffect(() => { inputRef.current?.focus(); }, []);
  useImperativeHandle(ref, () => ({ focus: () => inputRef.current?.focus() }), []);
  return (
    <Box sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider' }}>
      <TextField
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder="Ask anything about your indexed content"
        multiline
        minRows={1}
        maxRows={6}
        fullWidth
        inputRef={inputRef}
        onKeyDown={e => { if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') { e.preventDefault(); onSubmit(); } }}
      />
    </Box>
  );
});
