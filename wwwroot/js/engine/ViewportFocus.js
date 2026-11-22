// Visible-canvas focus point: center a part in the area not covered by the
// bottom dock, net inspector HUD, and left toolbar, instead of geometric center.

const FOCUS_PAD = 12;
const MIN_FOCUS_SPAN = 48;

function isOverlayVisible(el) {
    if (!el || el.classList.contains('hidden')) return false;
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden') return false;
    if (Number.parseFloat(cs.opacity) === 0) return false;
    const r = el.getBoundingClientRect();
    return r.width > 1 && r.height > 1;
}

function clampFocusSpan(start, end, max, minSpan) {
    if (end - start >= minSpan) return { start, end };
    let nextEnd = Math.min(max, start + minSpan);
    let nextStart = start;
    if (nextEnd - nextStart < minSpan) nextStart = Math.max(0, nextEnd - minSpan);
    return { start: nextStart, end: Math.max(nextEnd, nextStart + 1) };
}

export function getVisibleFocusCenter(container) {
    const cw = container?.clientWidth || window.innerWidth;
    const ch = container?.clientHeight || window.innerHeight;
    const cRect = container?.getBoundingClientRect?.() || { left: 0, top: 0, right: cw, bottom: ch };

    let left = 0;
    let top = 0;
    let right = cw;
    let bottom = ch;

    const netHud = document.getElementById('net-inspector-hud');
    if (isOverlayVisible(netHud)) {
        const n = netHud.getBoundingClientRect();
        const localLeft = n.left - cRect.left;
        const localRight = n.right - cRect.left;
        const localTop = n.top - cRect.top;
        const localBottom = n.bottom - cRect.top;

        if (localRight > 0 && localLeft < cw && localBottom > 0 && localTop < ch) {
            if (localLeft >= cw * 0.52 && n.width <= cw * 0.42) {
                right = Math.min(right, localLeft - FOCUS_PAD);
            } else {
                bottom = Math.min(bottom, localTop - FOCUS_PAD);
            }
        }
    }

    const toolbar = document.getElementById('left-toolbar');
    if (isOverlayVisible(toolbar)) {
        const t = toolbar.getBoundingClientRect();
        const localLeft = t.left - cRect.left;
        const localRight = t.right - cRect.left;
        if (t.height > t.width && localRight > 0 && localLeft < cw && localLeft <= 48) {
            left = Math.max(left, localRight + FOCUS_PAD);
        }
    }

    ({ start: left, end: right } = clampFocusSpan(left, right, cw, MIN_FOCUS_SPAN));
    ({ start: top, end: bottom } = clampFocusSpan(top, bottom, ch, MIN_FOCUS_SPAN));

    return { x: (left + right) / 2, y: (top + bottom) / 2 };
}
