import React from 'react';
import { Source } from '../types';
import { Accordion, AccordionSummary, AccordionDetails, Typography, Chip, Stack } from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';

export const SourceList: React.FC<{ sources: Source[] }> = ({ sources }) => {
  if (!sources.length) return null;
  return (
    <Stack spacing={1} sx={{ mt: 2 }}>
      <Typography variant="subtitle2" sx={{ opacity: 0.8 }}>Sources</Typography>
      {sources.map(s => (
        <Accordion key={`${s.docId}-${s.chunkNum}`} disableGutters variant="outlined" sx={{ bgcolor: 'background.paper' }}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
              <Chip size="small" label={s.filename} />
              <Chip size="small" color="primary" label={`chunk ${s.chunkNum}`} />
              <Chip size="small" variant="outlined" label={`dist ${s.distance.toFixed(3)}`} />
              {s.providerType && <Chip size="small" variant="outlined" label={s.providerType} />}
            </Stack>
          </AccordionSummary>
          <AccordionDetails>
            <Typography variant="body2" sx={{ mb: 1 }}>{s.text}</Typography>
            <Typography variant="caption" color="primary">{s.citation}</Typography>
          </AccordionDetails>
        </Accordion>
      ))}
    </Stack>
  );
};
