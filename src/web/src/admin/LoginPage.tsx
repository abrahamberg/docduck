import React, { useState } from 'react';
import { Box, Button, Container, Paper, TextField, Typography, Alert } from '@mui/material';
import { login, getProfile } from './api';
import { useAdminAuth } from './AdminContext';

export const LoginPage: React.FC = () => {
  const { login: setAuth } = useAdminAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      setLoading(true);
      setError(null);
      const { token } = await login(username.trim(), password);
      localStorage.setItem('docduck-admin-token', token);
      try {
        const profile = await getProfile();
        setAuth(token, profile);
      } catch (profileError) {
        localStorage.removeItem('docduck-admin-token');
        throw profileError;
      }
      window.location.href = '/admin';
    } catch (err: any) {
      setError(err.message || 'Login failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Container maxWidth="xs" sx={{ display: 'flex', alignItems: 'center', minHeight: '100vh' }}>
      <Paper component="form" onSubmit={handleSubmit} elevation={3} sx={{ p: 3, width: '100%', display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="h5" sx={{ fontWeight: 600 }}>Admin Login</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField
          label="Username"
          value={username}
          onChange={event => setUsername(event.target.value)}
          required
          autoFocus
        />
        <TextField
          label="Password"
          type="password"
          value={password}
          onChange={event => setPassword(event.target.value)}
          required
        />
        <Button type="submit" variant="contained" disabled={loading}>
          {loading ? 'Signing in…' : 'Sign In'}
        </Button>
        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
          <Button href="/">Back</Button>
        </Box>
      </Paper>
    </Container>
  );
};
