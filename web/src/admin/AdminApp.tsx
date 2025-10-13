import React, { useEffect, useState } from 'react';
import { ThemeProvider } from '@mui/material/styles';
import { theme } from '../theme';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AdminProvider, useAdminAuth } from './AdminContext';
import { LoginPage } from './LoginPage';
import { Dashboard } from './pages/Dashboard';
import { ProvidersPage } from './pages/ProvidersPage';
import { OpenAiPage } from './pages/OpenAiPage';
import { UsersPage } from './pages/UsersPage';
import { AdminLayout } from './AdminLayout';
import { getProfile } from './api';

const AdminRoutes: React.FC = () => {
  const { user, token, login } = useAdminAuth();
  const [initializing, setInitializing] = useState(true);

  useEffect(() => {
    (async () => {
      if (!token) {
        setInitializing(false);
        return;
      }

      if (!user) {
        try {
          const profile = await getProfile();
          login(token, profile);
        } catch {
          localStorage.removeItem('docduck-admin-token');
          localStorage.removeItem('docduck-admin-user');
        }
      }

      setInitializing(false);
    })();
  }, [token, user, login]);

  if (initializing) {
    return null;
  }

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/"
        element={token && user ? <AdminLayout title="DocDuck Admin" navKey="dashboard"><Dashboard /></AdminLayout> : <Navigate to="/login" replace />}
      />
      <Route
        path="/providers"
        element={token && user ? <AdminLayout title="Providers" navKey="providers"><ProvidersPage /></AdminLayout> : <Navigate to="/login" replace />}
      />
      <Route
        path="/openai"
        element={token && user ? <AdminLayout title="OpenAI Configuration" navKey="openai"><OpenAiPage /></AdminLayout> : <Navigate to="/login" replace />}
      />
      <Route
        path="/users"
        element={token && user ? <AdminLayout title="Admin Users" navKey="users"><UsersPage /></AdminLayout> : <Navigate to="/login" replace />}
      />
      <Route path="*" element={<Navigate to={token && user ? '/' : '/login'} replace />} />
    </Routes>
  );
};

export const AdminApp: React.FC = () => {
  return (
    <AdminProvider>
      <ThemeProvider theme={theme}>
        <BrowserRouter basename="/admin">
          <AdminRoutes />
        </BrowserRouter>
      </ThemeProvider>
    </AdminProvider>
  );
};
