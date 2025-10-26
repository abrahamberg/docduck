import React, { useEffect, useState, useMemo } from 'react';
import { getProviders, getHealth } from './api';
import { ProviderInfo, HealthStatus } from './types';
import { EnvironmentBanner } from './components/EnvironmentBanner';
import { ThemeProvider } from '@mui/material/styles';
import { getTheme } from './theme';
import { Box, Button, Autocomplete, TextField, IconButton, useMediaQuery } from '@mui/material';
import { Ask } from './components/Ask';
import Brightness4Icon from '@mui/icons-material/Brightness4';
import Brightness7Icon from '@mui/icons-material/Brightness7';

export const App: React.FC = () => {
  const prefersDarkMode = useMediaQuery('(prefers-color-scheme: dark)');
  const [themeMode, setThemeMode] = useState<'light' | 'dark'>(prefersDarkMode ? 'dark' : 'light');
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [selectedProviders, setSelectedProviders] = useState<string[]>([]);
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [healthLoading, setHealthLoading] = useState(false);

  const theme = useMemo(() => getTheme(themeMode), [themeMode]);

  const toggleTheme = () => {
    setThemeMode((prev) => (prev === 'dark' ? 'light' : 'dark'));
  };

  useEffect(() => {
    (async () => {
      try {
        setLoadingProviders(true);
        const p = await getProviders();
        setProviders(p);
      } catch (e: any) {
        console.error('Failed to load providers:', e);
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

  const providerOptions = providers.map((p) => ({
    label: `${p.providerName} (${p.providerType})`,
    value: p.providerName,
  }));

  return (
    <ThemeProvider theme={theme}>
      <Box
        sx={{
          minHeight: '100vh',
          bgcolor: 'background.default',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {/* Single top bar with environment status and controls */}
        <Box
          sx={{
            position: 'sticky',
            top: 0,
            zIndex: 1100,
            bgcolor: 'background.default',
            borderBottom: (theme) => `1px solid ${theme.palette.divider}`,
            px: 2,
            py: 1.5,
          }}
        >
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 2,
              flexWrap: 'wrap',
            }}
          >
            {/* Left side - Environment status */}
            <Box sx={{ flex: '1 1 auto', minWidth: 200 }}>
              <EnvironmentBanner health={health} loading={healthLoading} />
            </Box>

            {/* Right side - Provider selector, Theme toggle, and Admin button */}
            <Box
              sx={{
                display: 'flex',
                gap: 1.5,
                alignItems: 'center',
                flexWrap: { xs: 'wrap', sm: 'nowrap' },
                justifyContent: 'flex-end',
              }}
            >
              <Autocomplete
                multiple
                disableCloseOnSelect
                size="small"
                options={providerOptions}
                loading={loadingProviders}
                value={providerOptions.filter((o) => selectedProviders.includes(o.value))}
                onChange={(_, newValue) => setSelectedProviders(newValue.map((v) => v.value))}
                sx={{
                  minWidth: { xs: 180, sm: 220 },
                  maxWidth: { xs: 280, sm: 320 },
                }}
                getOptionLabel={(o) => o.label}
                renderInput={(params) => (
                  <TextField
                    {...params}
                    label="Providers"
                    placeholder={providerOptions.length === 0 ? 'No providers' : 'Select providers'}
                  />
                )}
              />
              <IconButton
                onClick={toggleTheme}
                size="small"
                aria-label="toggle theme"
                sx={{
                  '&:hover': {
                    bgcolor: 'action.hover',
                  },
                }}
              >
                {themeMode === 'dark' ? (
                  <Brightness7Icon fontSize="small" />
                ) : (
                  <Brightness4Icon fontSize="small" />
                )}
              </IconButton>
              <Button
                color="primary"
                variant="outlined"
                size="small"
                href="/admin/login"
                sx={{
                  whiteSpace: 'nowrap',
                }}
              >
                Admin
              </Button>
            </Box>
          </Box>
        </Box>

        {/* Main content area */}
        <Box sx={{ flex: 1 }}>
          <Ask providerNames={selectedProviders.length > 0 ? selectedProviders : undefined} />
        </Box>
      </Box>
    </ThemeProvider>
  );
};
