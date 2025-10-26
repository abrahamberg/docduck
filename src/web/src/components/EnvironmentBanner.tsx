import React from 'react';
import { Stack, Chip, LinearProgress, Typography, Box } from '@mui/material';
import { HealthStatus } from '../types';

interface Props {
  health: HealthStatus | null;
  loading: boolean;
}

export const EnvironmentBanner: React.FC<Props> = ({ health, loading }) => {
  if (loading) {
    return <LinearProgress sx={{ width: '100%', maxWidth: 300 }} />;
  }
  if (!health) return null;

  const warnings: string[] = [];
  if (!health.aiKeyPresent) warnings.push('AI API key missing');
  if (!health.dbConnectionPresent) warnings.push('DB connection missing');
  if (health.documents === 0) warnings.push('No documents indexed');

  if (warnings.length === 0) {
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <Typography variant="body2" sx={{ fontWeight: 600, color: 'success.main' }}>
          Environment OK
        </Typography>
        <Stack direction="row" spacing={1} flexWrap="wrap">
          <Chip color="success" size="small" label={`Chunks: ${health.chunks}`} />
          <Chip color="success" size="small" label={`Docs: ${health.documents}`} />
        </Stack>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
      <Typography variant="body2" sx={{ fontWeight: 600, color: 'warning.main' }}>
        Environment Issues
      </Typography>
      <Stack direction="row" spacing={1} flexWrap="wrap">
        {warnings.map((w) => (
          <Chip key={w} color="warning" size="small" label={w} />
        ))}
      </Stack>
    </Box>
  );
};
