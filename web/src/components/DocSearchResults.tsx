import React from 'react';
import { DocumentResult } from '../types';
import { Accordion, AccordionSummary, AccordionDetails, Typography, Chip, Stack } from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';

export const DocSearchResults: React.FC<{ results: DocumentResult[] }> = ({ results }) => {
  if (!results || results.length === 0) return null;
  return (
    <Stack spacing={1} sx={{ mt: 2 }}>
      <Typography variant="subtitle2" sx={{ opacity: 0.8 }}>Documents</Typography>
      {results.map(r => (
        <Accordion key={r.docId} disableGutters variant="outlined" sx={{ bgcolor: 'background.paper' }}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
              <Chip size="small" label={r.filename} />
              <Chip size="small" variant="outlined" label={r.address} />
              <Chip size="small" variant="outlined" label={`dist ${r.distance.toFixed(3)}`} />
            </Stack>
          </AccordionSummary>
          <AccordionDetails>
            <Typography variant="body2" sx={{ mb: 1 }}>{r.text}</Typography>
            <Typography variant="caption" color="primary">{r.address}</Typography>
            <Typography variant="caption" sx={{ display: 'block', mt: 1 }}>{r.providerType ? `${r.providerType}/${r.providerName}` : ''}</Typography>
          </AccordionDetails>
        </Accordion>
      ))}
    </Stack>
  );
};
