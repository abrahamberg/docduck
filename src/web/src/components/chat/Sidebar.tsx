import React from 'react';
import { Box, Button, List, ListItemButton, ListItemText, Typography } from '@mui/material';

export interface ConversationSummary { id: string; title: string; createdAt: number; }

interface Props { conversations: ConversationSummary[]; onNew: () => void; onOpen: (id: string) => void; activeId?: string; }

export const Sidebar: React.FC<Props> = ({ conversations, onNew, onOpen, activeId }) => {
  return (
    <Box sx={{ width: 260, display: 'flex', flexDirection: 'column', borderRight: '1px solid rgba(255,255,255,0.08)', bgcolor: '#202123' }}>
      <Box sx={{ p: 2 }}>
        <Button fullWidth variant="outlined" onClick={onNew} sx={{ borderRadius: 2 }}>New chat</Button>
      </Box>
      <List dense sx={{ flex: 1, overflowY: 'auto', py: 0 }}>
        {conversations.map(c => (
          <ListItemButton key={c.id} selected={c.id === activeId} onClick={() => onOpen(c.id)} sx={{ borderRadius: 2, mx: 1, mb: 0.5 }}>
            <ListItemText
              primary={<Typography variant="body2" sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{c.title}</Typography>}
            />
          </ListItemButton>
        ))}
      </List>
    </Box>
  );
};
