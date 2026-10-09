import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemberApp } from './member-app';
import './styles.css';

const node = document.getElementById('root');
if (!node) throw new Error('Member application root unavailable');
createRoot(node).render(
  <StrictMode>
    <MemberApp />
  </StrictMode>
);
