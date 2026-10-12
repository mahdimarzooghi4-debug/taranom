import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { OperationsApp } from './operations-app';
import { MarketEntryApp } from './market-entry-app';
import './styles.css';
const el = document.getElementById('root');
if (!el) throw Error('Missing root');
createRoot(el).render(<StrictMode>{new Set(['/markets','/markets/individuals','/markets/support','/markets/corporate']).has(window.location.pathname) ? <MarketEntryApp /> : <OperationsApp />}</StrictMode>);
