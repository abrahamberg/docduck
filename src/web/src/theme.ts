import { createTheme } from '@mui/material/styles';

type ThemeMode = 'light' | 'dark';

const getPalette = (isDark: boolean) => ({
  mode: (isDark ? 'dark' : 'light') as ThemeMode,
  primary: { 
    main: isDark ? '#4fa' : '#667eea',
    light: isDark ? '#6fd' : '#7c8ff0',
    dark: isDark ? '#3c8' : '#5568d3',
  },
  secondary: {
    main: isDark ? '#90caf9' : '#764ba2',
  },
  success: { main: isDark ? '#3fb950' : '#2e7d32' },
  warning: { main: isDark ? '#d29922' : '#ed6c02' },
  error: { main: isDark ? '#f85149' : '#d32f2f' },
  background: isDark ? {
    default: '#0d1117',
    paper: '#161b22'
  } : {
    default: '#f5f5f5',
    paper: '#ffffff'
  },
  divider: isDark ? 'rgba(255, 255, 255, 0.12)' : 'rgba(0, 0, 0, 0.12)',
});

const getComponents = (isDark: boolean): any => ({
  MuiButton: {
    styleOverrides: {
      root: { 
        textTransform: 'none', 
        fontWeight: 600,
        borderRadius: 8,
        padding: '8px 16px',
      },
      contained: {
        boxShadow: 'none',
        '&:hover': {
          boxShadow: 'none',
        },
      },
    },
  },
  MuiPaper: {
    styleOverrides: {
      root: { 
        backgroundImage: 'none',
        border: isDark ? '1px solid rgba(255,255,255,0.06)' : '1px solid rgba(0,0,0,0.08)',
        backdropFilter: 'blur(4px)',
      },
      elevation2: {
        boxShadow: isDark 
          ? '0 1px 3px rgba(0, 0, 0, 0.3), 0 1px 2px rgba(0, 0, 0, 0.24)'
          : '0 1px 3px rgba(0, 0, 0, 0.12), 0 1px 2px rgba(0, 0, 0, 0.06)',
      },
    },
  },
  MuiTextField: {
    styleOverrides: {
      root: {
        '& .MuiOutlinedInput-root': {
          backgroundColor: isDark ? 'rgba(255, 255, 255, 0.05)' : 'rgba(0, 0, 0, 0.02)',
          transition: 'background-color 0.2s',
          '&:hover': {
            backgroundColor: isDark ? 'rgba(255, 255, 255, 0.08)' : 'rgba(0, 0, 0, 0.04)',
          },
          '&.Mui-focused': {
            backgroundColor: isDark ? 'rgba(255, 255, 255, 0.09)' : 'rgba(0, 0, 0, 0.05)',
          },
        },
      },
    },
  },
  MuiCard: {
    styleOverrides: {
      root: {
        borderRadius: 12,
        border: isDark ? '1px solid rgba(255,255,255,0.08)' : '1px solid rgba(0,0,0,0.12)',
        boxShadow: isDark 
          ? '0 4px 18px rgba(0,0,0,0.45)'
          : '0 2px 8px rgba(0,0,0,0.1)',
      },
    },
  },
  MuiChip: {
    styleOverrides: {
      root: {
        backdropFilter: 'blur(6px)',
      },
    },
  },
  MuiAutocomplete: {
    styleOverrides: {
      paper: {
        backdropFilter: 'blur(10px)',
      }
    }
  }
});

export const getTheme = (mode: ThemeMode) => {
  const isDark = mode === 'dark';
  
  return createTheme({
    palette: getPalette(isDark),
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
      h5: {
        fontWeight: 700,
        letterSpacing: '-0.02em',
      },
      h6: {
        fontWeight: 600,
        letterSpacing: '-0.01em',
      },
      body1: {
        lineHeight: 1.6,
      },
      body2: { 
        lineHeight: 1.5,
      },
      button: {
        fontWeight: 600,
        letterSpacing: '0.02em',
      },
    },
    components: getComponents(isDark),
  });
};

// Default theme (dark mode)
export const theme = getTheme('dark');
