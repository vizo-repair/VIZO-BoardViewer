// ============================================================================
// FILE: wwwroot/js/ui/HtmlSanitizer.js
// Single escaping entry point before any innerHTML interpolation.
// Any data from AI, third-party sites (Serper/Mouser/DigiKey), Telegram profiles,
// and DB is treated as untrusted.
// ============================================================================

const ESCAPE_MAP = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
    '`': '&#96;'
};

/**
 * Escapes a string for safe HTML insertion (text and quoted attribute values).
 * Non-strings are coerced to string; null/undefined -> ''.
 */
export function escapeHtml(value) {
    if (value === null || value === undefined) return '';
    const str = typeof value === 'string' ? value : String(value);
    return str.replace(/[&<>"'`]/g, ch => ESCAPE_MAP[ch]);
}

/**
 * Returns a URL safe for href/src attributes, or fallback.
 * Only http(s):, root-relative paths ("/..."), and image/PDF data: URLs are allowed.
 * javascript:, vbscript:, data:text/html, etc. are rejected.
 */
export function safeUrl(value, fallback = '') {
    if (typeof value !== 'string') return fallback;
    const url = value.trim();
    if (!url) return fallback;

    if (url.startsWith('/') && !url.startsWith('//')) return url;
    if (/^https?:\/\//i.test(url)) return url;
    if (/^data:(image\/(png|jpe?g|gif|webp)|application\/pdf);base64,/i.test(url)) return url;

    return fallback;
}

export function brokenImagePlaceholder(label) {
    const text = escapeHtml(label || '');
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300" fill="none"><rect width="100%" height="100%" fill="#1e293b"/><text x="50%" y="50%" fill="#ef4444" font-size="22" font-family="sans-serif" font-weight="bold" text-anchor="middle" dominant-baseline="middle">${text}</text></svg>`;
    return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`;
}

/**
 * URL for <img>, PDF.js, and same-origin iframe.
 * External http(s) is rejected: CSP img-src/connect-src allows only 'self' | data | blob.
 */
export function safeLocalMediaUrl(value, fallback = '') {
    if (typeof value !== 'string') return fallback;
    const url = value.trim();
    if (!url) return fallback;

    if (url.startsWith('/') && !url.startsWith('//')) return url;
    if (/^data:(image\/(png|jpe?g|gif|webp)|application\/pdf);base64,/i.test(url)) return url;

    return fallback;
}
