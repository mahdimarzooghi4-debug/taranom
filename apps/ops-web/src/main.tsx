import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { OperationsApp } from './operations-app';
import './styles.css';
const el = document.getElementById('root');
if (!el) throw Error('Missing root');
createRoot(el).render(<StrictMode><OperationsApp /></StrictMode>);
