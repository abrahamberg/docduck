import React, { useEffect, useState } from 'react';
import { getProviders, getHealth } from './api';
import { ProviderInfo, HealthStatus } from './types';
import { ProviderFilter } from './components/ProviderFilter';
import { Chat } from './components/Chat';
import { Ask } from './components/Ask';
import { EnvironmentBanner } from './components/EnvironmentBanner';
import { ThemeProvider } from '@mui/material/styles';
import { theme } from './theme';
import { AppBar, Toolbar, Typography, Tabs, Tab, Container, Box, Paper } from '@mui/material';

export const App: React.FC = () => {
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [pf, setPf] = useState<{ providerType?: string; providerName?: string }>({});
  const [tab, setTab] = useState<'chat' | 'ask'>('chat');
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
            <Tabs value={tab} onChange={(_, v) => setTab(v)} textColor="primary" indicatorColor="primary">
              <Tab value="chat" label="Chat" />
              <Tab value="ask" label="Ask" />
            </Tabs>
          </Toolbar>
        </AppBar>
        <Container maxWidth="lg" sx={{ py: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <EnvironmentBanner health={health} loading={healthLoading} />
          <Paper variant="outlined" sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
            {loadingProviders ? <Typography variant="caption" sx={{ opacity: 0.7 }}>Loading providers…</Typography> : <ProviderFilter providers={providers} value={pf} onChange={setPf} />}
            {error && <Typography color="error" variant="caption">{error}</Typography>}
          </Paper>
          <Box sx={{ flex: 1, minHeight: 0 }}>
            {tab === 'chat' ? (
              <Chat providerType={pf.providerType} providerName={pf.providerName} />
            ) : (
              <Ask providerType={pf.providerType} providerName={pf.providerName} />
            )}
          </Box>
        </Container>
      </Box>
    </ThemeProvider>
  );
};
