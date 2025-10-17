import { createTheme } from '@mui/material/styles';

export const theme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#4fa' },
    background: {
      default: '#0d1117',
      paper: '#161b22'
    },
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily: 'system-ui, Inter, Roboto, Arial, sans-serif',
    body2: { lineHeight: 1.5 }
  },
  components: {
    MuiButton: {
      styleOverrides: {
        root: { textTransform: 'none', fontWeight: 600 }
      }
    },
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: 'none' }
      }
    }
  }
});
