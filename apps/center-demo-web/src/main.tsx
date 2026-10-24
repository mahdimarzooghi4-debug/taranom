import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CenterDemo } from './center-demo';
import { OrgDraftQa } from './org-draft-qa';
import './style.css';
const root = document.getElementById('root');
if (!root) throw Error('NO_ROOT');
const orgQa=import.meta.env.VITE_TARANOM_ORG_DRAFT_QA==='1' &&
  window.location.pathname==='/org-drafts-qa';
createRoot(root).render(<StrictMode>{orgQa?<OrgDraftQa/>:<CenterDemo/>}</StrictMode>);
