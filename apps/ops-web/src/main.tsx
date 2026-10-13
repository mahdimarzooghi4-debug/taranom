import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { OperationsApp } from './operations-app';
import { MarketEntryApp } from './market-entry-app';
import { EpicProgressApp } from './epic-progress-app';
import './styles.css';
const el = document.getElementById('root');
if (!el) throw Error('Missing root');
createRoot(el).render(<StrictMode>{window.location.pathname === '/operations/progress'
 ? <EpicProgressApp /> : new Set(['/markets','/markets/individuals','/markets/support','/markets/corporate']).has(window.location.pathname)
 ? <MarketEntryApp /> : <OperationsApp />}</StrictMode>);
