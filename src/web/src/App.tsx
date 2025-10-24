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
    setThemeMode(prev => prev === 'dark' ? 'light' : 'dark');
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

  const providerOptions = providers.map(p => ({
    label: `${p.providerName} (${p.providerType})`,
    value: p.providerName,
  }));

  return (
    <ThemeProvider theme={theme}>
      <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
        <EnvironmentBanner health={health} loading={healthLoading} />
        
        {/* Theme toggle at top-left */}
        <Box
          sx={{
            position: 'fixed',
            top: 16,
            left: 16,
            zIndex: 1000,
          }}
        >
          <IconButton
            onClick={toggleTheme}
            sx={{
              bgcolor: 'background.paper',
              backdropFilter: 'blur(12px)',
              '&:hover': {
                bgcolor: 'action.hover',
              },
            }}
            aria-label="toggle theme"
          >
            {themeMode === 'dark' ? <Brightness7Icon /> : <Brightness4Icon />}
          </IconButton>
        </Box>

        {/* Top-right controls - Always visible */}
        <Box
          sx={{
            position: 'fixed',
            top: 16,
            right: 16,
            zIndex: 1000,
            display: 'flex',
            gap: 2,
            alignItems: 'center',
          }}
        >
          <Autocomplete
            multiple
            disableCloseOnSelect
            size="small"
            options={providerOptions}
            loading={loadingProviders}
            value={providerOptions.filter(o => selectedProviders.includes(o.value))}
            onChange={(_, newValue) => setSelectedProviders(newValue.map(v => v.value))}
            sx={{ 
              minWidth: 220, 
              maxWidth: 320,
              '& .MuiOutlinedInput-root': {
                bgcolor: 'background.paper',
                backdropFilter: 'blur(12px)',
              }
            }}
            getOptionLabel={o => o.label}
            renderInput={(params) => (
              <TextField
                {...params}
                label="Providers"
                placeholder={providerOptions.length === 0 ? 'No providers' : 'Select providers'}
              />
            )}
          />
          <Button 
            color="primary" 
            variant="outlined" 
            size="small" 
            href="/admin/login"
            sx={{ 
              bgcolor: 'background.paper',
              backdropFilter: 'blur(12px)',
            }}
          >
            Admin
          </Button>
        </Box>

        <Ask 
          providerNames={selectedProviders.length > 0 ? selectedProviders : undefined} 
        />
      </Box>
    </ThemeProvider>
  );
};
