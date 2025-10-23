import React, { useEffect, useState } from 'react';
import { getProviders, getHealth } from './api';
import { ProviderInfo, HealthStatus } from './types';
import { EnvironmentBanner } from './components/EnvironmentBanner';
import { ThemeProvider } from '@mui/material/styles';
import { theme } from './theme';
import { AppBar, Toolbar, Typography, Container, Box, Paper, Button, Autocomplete, TextField } from '@mui/material';
import { Ask } from './components/Ask';

export const App: React.FC = () => {
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [selectedProviders, setSelectedProviders] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [healthLoading, setHealthLoading] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setLoadingProviders(true);
        const p = await getProviders();
        setProviders(p);
      } catch (e: any) {
        setError(e.message || 'Failed to load providers');
      } finally {
        setLoadingProviders(false);
      }
    })();
  }, []);

  useEffect(() => {
    (async () => {
      try {
        setHealthLoading(true);
        const h = await getHealth();
        setHealth(h);
      } catch {
        // ignore health errors, banner will not show
      } finally {
        setHealthLoading(false);
      }
    })();
  }, []);

  const providerOptions = providers.map(p => ({
    label: `${p.providerName} (${p.providerType})`,
    value: p.providerName,
  }));

  return (
    <ThemeProvider theme={theme}>
      <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh', bgcolor: 'background.default' }}>
        <AppBar
          position="static"
          elevation={0}
          sx={{
            bgcolor: 'background.paper',
            borderBottom: theme => `1px solid ${theme.palette.divider}`,
            backdropFilter: 'blur(12px)',
          }}
        >
          <Toolbar sx={{ gap: 3, flexWrap: 'wrap' }}>
            <Typography
              variant="h5"
              sx={{ fontWeight: 700, letterSpacing: '-0.02em', flexGrow: 1, display: 'flex', alignItems: 'center' }}
            >
              DocDuck
            </Typography>
            <Autocomplete
              multiple
              disableCloseOnSelect
              size="small"
              options={providerOptions}
              loading={loadingProviders}
              value={providerOptions.filter(o => selectedProviders.includes(o.value))}
              onChange={(_, newValue) => setSelectedProviders(newValue.map(v => v.value))}
              sx={{ minWidth: 280, flex: 1, maxWidth: 420 }}
              getOptionLabel={o => o.label}
              renderInput={(params) => (
                <TextField
                  {...params}
                  label="Providers"
                  placeholder={providerOptions.length === 0 ? 'No providers' : 'Select providers'}
                />
              )}
            />
            <Button color="primary" variant="outlined" size="small" href="/admin/login">
              Admin
            </Button>
          </Toolbar>
        </AppBar>
        <Container
          maxWidth={false}
          sx={{
            flex: 1,
            minHeight: 0,
            display: 'flex',
            flexDirection: 'column',
            px: { xs: 2, sm: 3, md: 4 },
            py: { xs: 2, sm: 3 },
          }}
        >
          <EnvironmentBanner health={health} loading={healthLoading} />
          <Box
            sx={{
              flex: 1,
              minHeight: 0,
              display: 'flex',
              alignItems: 'stretch',
              justifyContent: 'center',
              my: 2,
            }}
          >
            <Paper
              elevation={2}
              sx={{
                width: '100%',
                maxWidth: 1200,
                display: 'flex',
                flexDirection: 'column',
                overflow: 'hidden',
                border: theme => `1px solid ${theme.palette.divider}`,
              }}
            >
          
              <Box sx={{ flex: 1, minHeight: 0, display: 'flex' }}>
                <Ask providerNames={selectedProviders.length > 0 ? selectedProviders : undefined} />
              </Box>
            </Paper>
          </Box>
        </Container>
      </Box>
    </ThemeProvider>
  );
};
