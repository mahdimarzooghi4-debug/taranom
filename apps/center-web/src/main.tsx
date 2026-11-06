import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CenterApp } from './center-app';
import { CenterFigmaPortal } from './center-figma-portal';
import { CenterManagerApplication } from './center-manager-application';
import './styles.css';
import './figma-portal.css';

const root = document.getElementById('root');
if (!root) throw new Error('Clinical center app root missing');
createRoot(root).render(<StrictMode>{window.location.pathname === '/center-portal/manager-request'
  ? <CenterManagerApplication /> : window.location.pathname === '/center-portal' || window.location.pathname.startsWith('/center-portal/')
  ? <CenterFigmaPortal /> : <CenterApp />}</StrictMode>);
