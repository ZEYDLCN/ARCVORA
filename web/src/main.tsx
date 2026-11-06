import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { registerServiceWorker } from './lib/pwa';
import './styles.css';
import './apps.css';

createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>);

void registerServiceWorker(navigator, import.meta.env.PROD);
