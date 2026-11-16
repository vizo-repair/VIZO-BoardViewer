export function t(key, fallback, vars) {
    let text = fallback ?? key ?? '';
    if (vars && typeof vars === 'object') {
        for (const [k, v] of Object.entries(vars)) {
            text = text.replaceAll(`{${k}}`, String(v ?? ''));
        }
    }
    return text;
}

export function userFacingError(err, fallback = 'An error occurred.') {
    if (!err) return fallback;
    if (typeof err === 'string') return err;
    return err.message || fallback;
}

export function getCurrentLang() { return 'en'; }
export function getSavedLanguage() { return 'en'; }
export const SUPPORTED_LANGS = ['en'];
export async function setLanguage() { return 'en'; }
export function localeTag() { return 'en-US'; }
export function installVizoLangHeader() {}
