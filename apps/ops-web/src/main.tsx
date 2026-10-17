import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { OperationsApp } from './operations-app';
import { MarketEntryApp } from './market-entry-app';
import { EpicProgressApp } from './epic-progress-app';
import { ReleaseDependenciesApp } from './release-dependencies-app';
import { OfflineAiQaApp } from './ai-registry-qa';
import './styles.css';
const el = document.getElementById('root');
if (!el) throw Error('Missing root');
createRoot(el).render(<StrictMode>{window.location.pathname === '/operations/ai-lab'
 ? <OfflineAiQaApp /> : window.location.pathname === '/operations/progress'
 ? <EpicProgressApp /> : window.location.pathname === '/operations/dependencies'
 ? <ReleaseDependenciesApp /> : new Set(['/markets','/markets/individuals','/markets/support','/markets/corporate']).has(window.location.pathname)
 ? <MarketEntryApp /> : <OperationsApp />}</StrictMode>);
