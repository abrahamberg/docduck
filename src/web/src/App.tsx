import React, { useEffect, useState } from 'react';
import { getProviders, getHealth } from './api';
import { ProviderInfo, HealthStatus } from './types';
import { ProviderFilter } from './components/ProviderFilter';
import { EnvironmentBanner } from './components/EnvironmentBanner';
import { ThemeProvider } from '@mui/material/styles';
import { theme } from './theme';
import { AppBar, Toolbar, Typography, Container, Box, Paper, Button, Slider } from '@mui/material';
import { Ask } from './components/Ask';

export const App: React.FC = () => {
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [pf, setPf] = useState<{ providerType?: string; providerName?: string }>({});
  const [searchDepth, setSearchDepth] = useState<number>(3);
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

  return (
    <ThemeProvider theme={theme}>
      <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh', bgcolor: 'background.default' }}>
        <AppBar position="static" color="transparent" elevation={0} sx={{ borderBottom: theme => `1px solid ${theme.palette.divider}` }}>
          <Toolbar>
            <Typography variant="h6" sx={{ flexGrow: 1, fontWeight: 600 }}>DocDuck</Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', width: 360, gap: 2 }}>
              <Typography variant="caption" sx={{ mr: 1 }}>Depth: {searchDepth}</Typography>
              <Slider
                size="small"
                min={1}
                max={5}
                step={1}
                marks
                value={searchDepth}
                onChange={(_e, v) => { if (Array.isArray(v)) return; setSearchDepth(v); }}
                sx={{ width: 220 }}
              />
            </Box>
            <Button color="primary" variant="outlined" sx={{ ml: 2 }} href="/admin/login">
              Admin
            </Button>
          </Toolbar>
        </AppBar>
        <Container maxWidth="lg" sx={{ py: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <EnvironmentBanner health={health} loading={healthLoading} />
          <Paper variant="outlined" sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
            {loadingProviders ? <Typography variant="caption" sx={{ opacity: 0.7 }}>Loading providers…</Typography> : <ProviderFilter providers={providers} value={pf} onChange={setPf} />}
            {error && <Typography color="error" variant="caption">{error}</Typography>}
          </Paper>
          <Box sx={{ flex: 1, minHeight: 0 }}>
            {/* Unified search/interaction area. We keep the Ask/Chat components in the codebase, but the UI now exposes a single searchDepth control.
                Components that need the searchDepth should read it from local state via props. For now, render the Ask panel as the primary interaction surface
                and pass searchDepth through provider props in the child components that still accept it. */}
            <Paper variant="outlined" sx={{ height: '100%', p: 2 }}>
              <Typography variant="body2" sx={{ mb: 1 }}>Use the slider in the header to adjust search depth (1-5) for queries.</Typography>
              <Box sx={{ height: 'calc(100% - 40px)' }}>
                <Ask providerType={pf.providerType} providerName={pf.providerName} searchDepth={searchDepth} />
              </Box>
            </Paper>
          </Box>
        </Container>
      </Box>
    </ThemeProvider>
  );
};
