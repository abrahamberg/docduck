import React, { createContext, useContext, useMemo, useState } from 'react';
import type { AdminUser } from './types';

interface AdminAuthState {
  user: AdminUser | null;
  token: string | null;
  login: (token: string, user: AdminUser) => void;
  logout: () => void;
}

const AdminContext = createContext<AdminAuthState | undefined>(undefined);

export const AdminProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('docduck-admin-token'));
  const [user, setUser] = useState<AdminUser | null>(() => {
    const raw = localStorage.getItem('docduck-admin-user');
    if (!raw) {
      return null;
    }
    try {
      return JSON.parse(raw) as AdminUser;
    } catch {
      localStorage.removeItem('docduck-admin-user');
      return null;
    }
  });

  const value = useMemo<AdminAuthState>(() => ({
    user,
    token,
    login: (nextToken, nextUser) => {
      setToken(nextToken);
      setUser(nextUser);
      localStorage.setItem('docduck-admin-token', nextToken);
      localStorage.setItem('docduck-admin-user', JSON.stringify(nextUser));
    },
    logout: () => {
      setToken(null);
      setUser(null);
      localStorage.removeItem('docduck-admin-token');
      localStorage.removeItem('docduck-admin-user');
    },
  }), [user, token]);

  return (
    <AdminContext.Provider value={value}>
      {children}
    </AdminContext.Provider>
  );
};

export function useAdminAuth(): AdminAuthState {
  const ctx = useContext(AdminContext);
  if (!ctx) {
    throw new Error('useAdminAuth must be used within AdminProvider');
  }
  return ctx;
}
