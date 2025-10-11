import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';

const rootElem = document.getElementById('root');
if (!rootElem) throw new Error('Root element not found');
createRoot(rootElem).render(<App />);
