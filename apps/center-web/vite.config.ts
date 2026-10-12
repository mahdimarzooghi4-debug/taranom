import { defineConfig } from 'vite';
import { disconnectedPreviewSecurityHeaders } from '../../tools/preview_security/headers';

export default defineConfig({
  base: '/',
  server: { host: '127.0.0.1', port: 4183, strictPort: true },
  preview: { host: '127.0.0.1', port: 4184, strictPort: true, headers: disconnectedPreviewSecurityHeaders },
  build: { target: 'es2022', sourcemap: false },
});
