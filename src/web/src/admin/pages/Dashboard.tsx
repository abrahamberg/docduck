import React from 'react';
import { Typography, Grid, Paper, Box } from '@mui/material';

export const Dashboard: React.FC = () => {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Typography variant="h5" sx={{ fontWeight: 600 }}>
        Welcome to DocDuck Admin
      </Typography>
      <Grid container spacing={3}>
        <Grid item xs={12} md={6}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6">Quick Links</Typography>
            <ul>
              <li>Manage provider integrations</li>
              <li>OpenAI configuration</li>
              <li>User accounts</li>
            </ul>
          </Paper>
        </Grid>
        <Grid item xs={12} md={6}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6">Security Tips</Typography>
            <ul>
              <li>Change the default admin password immediately.</li>
              <li>Grant admin privileges only to trusted teammates.</li>
            </ul>
          </Paper>
        </Grid>
      </Grid>
    </Box>
  );
};
