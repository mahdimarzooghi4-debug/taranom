import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CenterDemo } from './center-demo';
import { OrgDraftQa } from './org-draft-qa';
import { AppointmentRequestQueueQa } from './appointment-request-queue';
import './style.css';
const root = document.getElementById('root');
if (!root) throw Error('NO_ROOT');
const orgQa=import.meta.env.VITE_TARANOM_ORG_DRAFT_QA==='1' &&
  window.location.pathname==='/org-drafts-qa';
const appointmentQa=import.meta.env.VITE_TARANOM_APPOINTMENT_REQUEST_QA==='1' &&
  window.location.pathname==='/appointment-requests-qa';
createRoot(root).render(<StrictMode>{appointmentQa?<AppointmentRequestQueueQa/>:
  orgQa?<OrgDraftQa/>:<CenterDemo/>}</StrictMode>);
