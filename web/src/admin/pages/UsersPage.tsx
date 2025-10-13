import React, { useEffect, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Paper, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { changePassword, createUser, listUsers, setAdmin } from '../api';
import type { AdminUser } from '../types';

export const UsersPage: React.FC = () => {
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [newUsername, setNewUsername] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [saving, setSaving] = useState(false);
  const [passwordDialog, setPasswordDialog] = useState<{ open: boolean; userId: string | null; username: string }>(
    { open: false, userId: null, username: '' }
  );
  const [newUserPassword, setNewUserPassword] = useState('');

  const trimmedUsername = newUsername.trim();
  const usernameError = trimmedUsername.length > 0 && trimmedUsername.length < 3 ? 'Username must be at least 3 characters.' : null;
  const passwordError = newPassword.length > 0 && newPassword.length < 8 ? 'Password must be at least 8 characters.' : null;
  const createDisabled = saving || trimmedUsername.length < 3 || newPassword.length < 8;
  const passwordUpdateError = newUserPassword.length > 0 && newUserPassword.length < 8 ? 'Password must be at least 8 characters.' : null;
  const updateDisabled = newUserPassword.length < 8;

  const loadUsers = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await listUsers();
      setUsers(response.users);
    } catch (err: any) {
      setError(err.message || 'Failed to load users');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadUsers();
  }, []);

  const handleCreateUser = async () => {
    if (trimmedUsername.length < 3 || newPassword.length < 8) {
      setError('Provide a username with at least 3 characters and a password with at least 8 characters.');
      return;
    }

    try {
      setSaving(true);
      setError(null);
  await createUser(trimmedUsername, newPassword, isAdmin);
      setCreateDialogOpen(false);
      setNewUsername('');
      setNewPassword('');
      setIsAdmin(false);
      await loadUsers();
    } catch (err: any) {
      setError(err.message || 'Failed to create user');
    } finally {
      setSaving(false);
    }
  };

  const toggleAdmin = async (userId: string, next: boolean) => {
    try {
      setError(null);
      await setAdmin(userId, next);
      await loadUsers();
    } catch (err: any) {
      setError(err.message || 'Failed to update user');
    }
  };

  const openPasswordDialog = (user: AdminUser) => {
    setError(null);
    setPasswordDialog({ open: true, userId: user.id, username: user.username });
    setNewUserPassword('');
  };

  const updatePassword = async () => {
    if (!passwordDialog.userId) return;
    if (newUserPassword.length < 8) {
      setError('Password must be at least 8 characters.');
      return;
    }
    try {
      setError(null);
      await changePassword(passwordDialog.userId, newUserPassword);
      setPasswordDialog({ open: false, userId: null, username: '' });
      setNewUserPassword('');
    } catch (err: any) {
      setError(err.message || 'Failed to update password');
    }
  };

  if (loading) {
    return <CircularProgress />;
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Typography variant="h5" sx={{ fontWeight: 600 }}>Admin Users</Typography>
      {error && <Alert severity="error">{error}</Alert>}
      <Button variant="contained" onClick={() => setCreateDialogOpen(true)} sx={{ alignSelf: 'flex-start' }}>
        New User
      </Button>
      <Paper>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Username</TableCell>
              <TableCell>Role</TableCell>
              <TableCell>Created</TableCell>
              <TableCell>Updated</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {users.map(user => (
              <TableRow key={user.id}>
                <TableCell>{user.username}</TableCell>
                <TableCell>{user.isAdmin ? 'Admin' : 'User'}</TableCell>
                <TableCell>{new Date(user.createdAt).toLocaleString()}</TableCell>
                <TableCell>{new Date(user.updatedAt).toLocaleString()}</TableCell>
                <TableCell align="right">
                  <Button onClick={() => openPasswordDialog(user)}>Change Password</Button>
                  <FormControlLabel
                    control={<Switch checked={user.isAdmin} onChange={event => toggleAdmin(user.id, event.target.checked)} />}
                    label="Admin"
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>

      <Dialog open={createDialogOpen} onClose={() => setCreateDialogOpen(false)}>
        <DialogTitle>Create New Admin User</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, mt: 1 }}>
          <TextField
            label="Username"
            value={newUsername}
            onChange={event => setNewUsername(event.target.value)}
            error={Boolean(usernameError)}
            helperText={usernameError ?? ' '}
            autoFocus
          />
          <TextField
            label="Password"
            type="password"
            value={newPassword}
            onChange={event => setNewPassword(event.target.value)}
            error={Boolean(passwordError)}
            helperText={passwordError ?? 'Minimum 8 characters.'}
          />
          <FormControlLabel control={<Switch checked={isAdmin} onChange={event => setIsAdmin(event.target.checked)} />} label="Admin" />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCreateDialogOpen(false)}>Cancel</Button>
          <Button onClick={handleCreateUser} disabled={createDisabled}>{saving ? 'Creating…' : 'Create'}</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={passwordDialog.open} onClose={() => setPasswordDialog({ open: false, userId: null, username: '' })}>
        <DialogTitle>Change Password for {passwordDialog.username}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, mt: 1 }}>
          <TextField
            label="New Password"
            type="password"
            value={newUserPassword}
            onChange={event => setNewUserPassword(event.target.value)}
            error={Boolean(passwordUpdateError)}
            helperText={passwordUpdateError ?? 'Minimum 8 characters.'}
            autoFocus
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPasswordDialog({ open: false, userId: null, username: '' })}>Cancel</Button>
          <Button onClick={updatePassword} disabled={updateDisabled}>Update</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
