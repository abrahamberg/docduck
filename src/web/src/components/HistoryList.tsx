import React from 'react';
import { List, ListItemButton, ListItemText, Typography, IconButton, Tooltip, Chip, Stack } from '@mui/material';
import ReplayIcon from '@mui/icons-material/Replay';

export interface HistoryItem {
  id: string;
  question: string;
  answerSnippet?: string;
  timestamp: number;
  providers: string[];
  latencyMs?: number;
  tokensUsed?: number;
}

interface Props {
  items: HistoryItem[];
  onSelect: (item: HistoryItem) => void;
  onRerun: (item: HistoryItem) => void;
}

export const HistoryList: React.FC<Props> = ({ items, onSelect, onRerun }) => {
  if (items.length === 0) {
    return <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>No history yet. Ask a question to see it here.</Typography>;
  }
  return (
    <List dense disablePadding sx={{ overflowY: 'auto', flex: 1 }}>
      {items.map(h => (
        <ListItemButton key={h.id} onClick={() => onSelect(h)} sx={{ alignItems: 'flex-start', py: 1 }}>
          <ListItemText
            primary={
              <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                <Typography variant="body2" sx={{ fontWeight: 500, flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{h.question}</Typography>
                {h.latencyMs != null && <Chip size="small" label={`${Math.round(h.latencyMs)}ms`} />}
                {h.tokensUsed != null && <Chip size="small" label={`${h.tokensUsed}t`} />}
              </Stack>
            }
            secondary={
              <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.5 }}>
                {h.answerSnippet && (
                  <Typography variant="caption" sx={{ flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{h.answerSnippet}</Typography>
                )}
                {h.providers.slice(0,3).map(p => <Chip key={p} size="small" label={p} />)}
                {h.providers.length > 3 && <Chip size="small" label={`+${h.providers.length-3}`} />}
                <Tooltip title="Rerun query">
                  <span>
                    <IconButton size="small" onClick={(e) => { e.stopPropagation(); onRerun(h); }} tabIndex={-1}>
                      <ReplayIcon fontSize="inherit" />
                    </IconButton>
                  </span>
                </Tooltip>
              </Stack>
            }
          />
        </ListItemButton>
      ))}
    </List>
  );
};
