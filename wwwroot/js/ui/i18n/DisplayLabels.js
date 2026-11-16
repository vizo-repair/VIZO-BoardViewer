export function formatBoardSide(side, { short = false } = {}) {
    const s = String(side || 'TOP').toUpperCase();
    const isBottom = s === 'BOTTOM' || s === 'BOT';
    if (short) return isBottom ? 'BOT' : 'TOP';
    return isBottom ? 'BOTTOM' : 'TOP';
}

export function formatCorner(code) {
    return String(code || 'TL').toUpperCase();
}
