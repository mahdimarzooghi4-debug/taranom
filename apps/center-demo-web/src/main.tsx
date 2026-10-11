import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CenterDemo } from './center-demo';
import './style.css';
const root = document.getElementById('root');
if (!root) throw Error('NO_ROOT');
createRoot(root).render(<StrictMode><CenterDemo /></StrictMode>);
