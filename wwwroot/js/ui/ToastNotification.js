// ============================================================================
// FILE: wwwroot/js/ui/ToastNotification.js
// Neon toast notifications
// ============================================================================
import { escapeHtml } from './HtmlSanitizer.js';

export function showAppToast(msg, isError = false) {
    let toast = document.getElementById('vizo-app-toast');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'vizo-app-toast';
        toast.className = 'fixed top-20 left-1/2 -translate-x-1/2 glass px-4 py-2 rounded-xl border text-white text-xs font-bold shadow-2xl z-50 flex items-center gap-2 transition-opacity duration-300 pointer-events-none opacity-0';
        document.body.appendChild(toast);
    }
    toast.style.borderColor = isError ? 'rgba(239, 68, 68, 0.6)' : 'rgba(59, 130, 246, 0.6)';
    toast.innerHTML = `<span class="material-symbols-outlined text-sm ${isError ? 'text-red-400' : 'text-blue-400'}">${isError ? 'error' : 'check_circle'}</span><span>${escapeHtml(msg)}</span>`;
    toast.style.opacity = '1';

    if (toast._timeout) clearTimeout(toast._timeout);
    toast._timeout = setTimeout(() => { toast.style.opacity = '0'; }, 3500);
}