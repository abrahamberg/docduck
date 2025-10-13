import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { AdminApp } from './admin/AdminApp';

const rootElem = document.getElementById('root');
if (!rootElem) throw new Error('Root element not found');
const isAdminRoute = window.location.pathname.startsWith('/admin');
createRoot(rootElem).render(isAdminRoute ? <AdminApp /> : <App />);
