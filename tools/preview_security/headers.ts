// Test-only built-preview HTTP security headers for BOTH disconnected shells.
// Never claim these Vite preview settings configure a future hosting platform.
// frame-ancestors cannot be enforced by a CSP meta tag; it requires HTTP.
export const disconnectedPreviewSecurityHeaders: Readonly<Record<string, string>> =
  Object.freeze({
    'Content-Security-Policy': [
      "default-src 'none'",
      "base-uri 'none'",
      "object-src 'none'",
      "frame-ancestors 'none'",
      "frame-src 'none'",
      "form-action 'none'",
      "script-src 'self'",
      "style-src 'self'",
      "img-src 'self' data:",
      "font-src 'self'",
      "connect-src 'none'",
      "worker-src 'none'",
      "media-src 'none'",
    ].join('; '),
    'X-Frame-Options': 'DENY',
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'Permissions-Policy':
      'camera=(), microphone=(), geolocation=(), payment=(), usb=()',
    'Cross-Origin-Opener-Policy': 'same-origin',
    'Cross-Origin-Resource-Policy': 'same-origin',
    'Cache-Control': 'no-store',
  });
