import React from 'react';
import { Source } from '../types';
import { Accordion, AccordionSummary, AccordionDetails, Typography, Chip } from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';

interface Props {
  sources: Source[];
}

export const SourceAccordion: React.FC<Props> = ({ sources }) => {
  if (!sources || sources.length === 0) return null;
  return (
    <div>
      {sources.map((s, i) => (
        <Accordion key={s.docId + ':' + s.chunkNum} disableGutters>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="body2" sx={{ flex: 1, fontWeight: 500 }}>
              Source {i + 1}: {s.filename}
            </Typography>
            {s.providerName && <Chip size="small" label={s.providerName} sx={{ ml: 1 }} />}
          </AccordionSummary>
          <AccordionDetails>
            <Typography variant="caption" sx={{ display: 'block', mb: 1, opacity: 0.7 }}>
              Distance: {s.distance.toFixed(3)}
            </Typography>
            <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
              {s.text}
            </Typography>
          </AccordionDetails>
        </Accordion>
      ))}
    </div>
  );
};
