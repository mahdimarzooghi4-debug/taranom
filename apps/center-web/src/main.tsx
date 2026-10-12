import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CenterApp } from './center-app';
import './styles.css';

const root = document.getElementById('root');
if (!root) throw new Error('Clinical center app root missing');
createRoot(root).render(<StrictMode><CenterApp /></StrictMode>);
