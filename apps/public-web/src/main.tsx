import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { PublicSite } from './public-site';
import './styles.css';

const root = document.getElementById('root');
if (!root) throw new Error('Taranom public website root unavailable');

createRoot(root).render(
  <StrictMode>
    <PublicSite />
  </StrictMode>,
);