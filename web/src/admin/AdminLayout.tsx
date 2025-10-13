import React from 'react';
import { AppBar, Toolbar, Typography, Button, Container, Box, Stack } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { useAdminAuth } from './AdminContext';

interface Props {
  title: string;
  navKey: 'dashboard' | 'providers' | 'openai' | 'users';
  actions?: React.ReactNode;
  children: React.ReactNode;
}

const NAV_ITEMS: Array<{ key: Props['navKey']; label: string; to: string }> = [
  { key: 'dashboard', label: 'Dashboard', to: '/' },
  { key: 'providers', label: 'Providers', to: '/providers' },
  { key: 'openai', label: 'OpenAI', to: '/openai' },
  { key: 'users', label: 'Users', to: '/users' },
];

export const AdminLayout: React.FC<Props> = ({ title, navKey, actions, children }) => {
  const { user, logout } = useAdminAuth();

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      <AppBar position="static" color="default" elevation={0} sx={{ borderBottom: theme => `1px solid ${theme.palette.divider}` }}>
        <Toolbar sx={{ gap: 2, flexWrap: 'wrap' }}>
          <Typography variant="h6" sx={{ fontWeight: 600 }}>
            {title}
          </Typography>
          <Stack direction="row" spacing={1} sx={{ flexGrow: 1, flexWrap: 'wrap' }}>
            {NAV_ITEMS.map(item => (
              <Button
                key={item.key}
                component={RouterLink}
                to={item.to}
                variant={navKey === item.key ? 'contained' : 'outlined'}
                color={navKey === item.key ? 'primary' : 'inherit'}
              >
                {item.label}
              </Button>
            ))}
          </Stack>
          {actions}
          {user && (
            <Button variant="outlined" onClick={logout}>
              Log out ({user.username})
            </Button>
          )}
        </Toolbar>
      </AppBar>
      <Container maxWidth="md" sx={{ py: 4, flex: 1 }}>
        {children}
      </Container>
    </Box>
  );
};
