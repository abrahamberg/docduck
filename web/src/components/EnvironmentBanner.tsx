import React from 'react';
import { Alert, AlertTitle, Stack, Chip, LinearProgress } from '@mui/material';
import { HealthStatus } from '../types';

interface Props { health: HealthStatus | null; loading: boolean; }

export const EnvironmentBanner: React.FC<Props> = ({ health, loading }) => {
  if (loading) {
    return <LinearProgress />;
  }
  if (!health) return null;

  const warnings: string[] = [];
  if (!health.openAiKeyPresent) warnings.push('OpenAI key missing');
  if (!health.dbConnectionPresent) warnings.push('DB connection missing');
  if (health.documents === 0) warnings.push('No documents indexed');

  if (warnings.length === 0) {
    return (
      <Alert severity="success" variant="outlined" sx={{ alignItems: 'center' }}>
        <AlertTitle>Environment OK</AlertTitle>
        <Stack direction="row" spacing={1} flexWrap="wrap">
          <Chip color="success" size="small" label={`Chunks: ${health.chunks}`} />
          <Chip color="success" size="small" label={`Docs: ${health.documents}`} />
        </Stack>
      </Alert>
    );
  }

  return (
    <Alert severity="warning" variant="outlined">
      <AlertTitle>Environment Issues</AlertTitle>
      <Stack direction="row" spacing={1} flexWrap="wrap">
        {warnings.map(w => <Chip key={w} color="warning" size="small" label={w} />)}
      </Stack>
    </Alert>
  );
};
