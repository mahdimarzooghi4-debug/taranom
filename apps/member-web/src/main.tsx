import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemberApp } from './member-app';
import './styles.css';
import { PatientWebPortal } from './patient-web-portal';
import './patient-web-portal.css';
import { PatientMobileApp } from './patient-mobile-app';
import './patient-mobile-app.css';

const node = document.getElementById('root');
if (!node) throw new Error('Member application root unavailable');
createRoot(node).render(
  <StrictMode>
    {window.location.pathname === '/patient-app' || window.location.pathname.startsWith('/patient-app/')
      ? <PatientMobileApp />
      : window.location.pathname === '/patient-portal' || window.location.pathname.startsWith('/patient-portal/')
      ? <PatientWebPortal />
      : <MemberApp />}
  </StrictMode>
);
