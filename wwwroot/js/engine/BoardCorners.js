// ============================================================================
// FILE: wwwroot/js/engine/BoardCorners.js
// ============================================================================
import { computeHomography4Points, transformPoint } from './HomographySolver.js';

export const CORNER_ORDER = ['TL', 'TR', 'BR', 'BL'];

export const DEFAULT_PHOTO_CORNERS = [
    { x: 0.20, y: 0.20 },
    { x: 0.80, y: 0.20 },
    { x: 0.80, y: 0.80 },
    { x: 0.20, y: 0.80 }
];

const MIN_QUAD_AREA = 1e-5;
const COINCIDENT_EPS = 1e-8;
const COLLINEAR_EPS = 1e-12;

let cachedDefaultMatrixKey = '';
let cachedDefaultMatrix = null;
let standaloneOrientation = {
    rotateDeg: 0,
    flipH: false
};

export function getStandaloneOrientation() {
    return { ...standaloneOrientation };
}

export function rotateStandaloneCad(step = 90) {
    standaloneOrientation.rotateDeg = (((standaloneOrientation.rotateDeg || 0) + step) % 360 + 360) % 360;
    invalidateDefaultMatrixCache();
    return standaloneOrientation.rotateDeg;
}

export function flipStandaloneCad() {
    standaloneOrientation.flipH = !standaloneOrientation.flipH;
    invalidateDefaultMatrixCache();
    return standaloneOrientation.flipH;
}

export function resetStandaloneOrientation() {
    standaloneOrientation.rotateDeg = 0;
    standaloneOrientation.flipH = false;
    invalidateDefaultMatrixCache();
}

export function invalidateDefaultMatrixCache() {
    cachedDefaultMatrixKey = '';
    cachedDefaultMatrix = null;
}

export function isUsableBounds(bounds) {
    return !!(bounds
        && Number.isFinite(bounds.minX) && Number.isFinite(bounds.maxX)
        && Number.isFinite(bounds.minY) && Number.isFinite(bounds.maxY)
        && (bounds.maxX - bounds.minX) > 1e-9
        && (bounds.maxY - bounds.minY) > 1e-9);
}

export function componentAabb(comp) {
    if (!comp) return null;
    const w = Number(comp.width) || Math.abs((comp.maxX ?? 0) - (comp.minX ?? 0));
    const h = Number(comp.height) || Math.abs((comp.maxY ?? 0) - (comp.minY ?? 0));
    const minX = (comp.maxX > comp.minX) ? comp.minX : (Number.isFinite(comp.x) ? comp.x - w / 2 : NaN);
    const maxX = (comp.maxX > comp.minX) ? comp.maxX : (Number.isFinite(comp.x) ? comp.x + w / 2 : NaN);
    const minY = (comp.maxY > comp.minY) ? comp.minY : (Number.isFinite(comp.y) ? comp.y - h / 2 : NaN);
    const maxY = (comp.maxY > comp.minY) ? comp.maxY : (Number.isFinite(comp.y) ? comp.y + h / 2 : NaN);
    if (![minX, minY, maxX, maxY].every(Number.isFinite)) return null;
    if (!(maxX > minX) || !(maxY > minY)) return null;
    return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
}

export function resolveBoardBounds(netlist) {
    const bounds = netlist?.bounds;
    if (isUsableBounds(bounds)) return bounds;

    const comps = netlist?.components;
    if (!comps || typeof comps !== 'object') return null;

    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const [refDes, comp] of Object.entries(comps)) {
        const u = refDes.toUpperCase();
        if (u.startsWith('$') || u.startsWith('FID') || u.startsWith('FRAME') || u.startsWith('BORDER')) {
            continue;
        }
        const box = componentAabb(comp);
        if (!box) continue;
        minX = Math.min(minX, box.minX);
        minY = Math.min(minY, box.minY);
        maxX = Math.max(maxX, box.maxX);
        maxY = Math.max(maxY, box.maxY);
    }

    if (!Number.isFinite(minX) || !(maxX > minX) || !(maxY > minY)) return null;

    const margin = 20;
    const bMinX = minX - margin;
    const bMaxX = maxX + margin;
    const bMinY = minY - margin;
    const bMaxY = maxY + margin;

    return {
        minX: bMinX,
        minY: bMinY,
        maxX: bMaxX,
        maxY: bMaxY,
        width: bMaxX - bMinX,
        height: bMaxY - bMinY,
        unit: 'MIL',
        outline: [
            { x: bMinX, y: bMinY },
            { x: bMaxX, y: bMinY },
            { x: bMaxX, y: bMaxY },
            { x: bMinX, y: bMaxY },
            { x: bMinX, y: bMinY }
        ]
    };
}

export function getDefaultCadMatrix(netlist, side, customOrientation = null) {
    const bounds = resolveBoardBounds(netlist);
    if (!bounds) return null;
    const isBottom = String(side || 'TOP').toUpperCase().startsWith('B');
    const orient = customOrientation || standaloneOrientation;
    const rotateDeg = normalizeRotateDeg(orient);
    const flipH = !!orient.flipH ^ isBottom;

    const key = `${bounds.minX}_${bounds.minY}_${bounds.maxX}_${bounds.maxY}_${isBottom}_${rotateDeg}_${flipH}`;
    if (cachedDefaultMatrix && cachedDefaultMatrixKey === key) {
        return cachedDefaultMatrix;
    }

    const cadCorners = getBoardCadCorners(bounds, { rotateDeg, flipH });
    if (!cadCorners) return null;

    const src = cadCorners.map(c => ({ x: c.x, y: c.y }));
    const dst = [
        { x: 0.0, y: 0.0 },
        { x: 1.0, y: 0.0 },
        { x: 1.0, y: 1.0 },
        { x: 0.0, y: 1.0 }
    ];

    cachedDefaultMatrix = computeHomography4Points(src, dst);
    cachedDefaultMatrixKey = key;
    return cachedDefaultMatrix;
}

export function getEffectiveMatrix(netlist, side, customMatrix = null) {
    if (customMatrix && Array.isArray(customMatrix) && customMatrix.length === 9) {
        return customMatrix;
    }
    if (!netlist) return null;
    return getDefaultCadMatrix(netlist, side);
}

export function normalizeRotateDeg(options = {}) {
    if (Number.isFinite(options.rotateDeg)) return options.rotateDeg;
    const rotateK = ((Number(options.rotateK) || 0) % 4 + 4) % 4;
    return rotateK * 90;
}

export function snapRotateDeg(deg, threshold = 12) {
    const wrapped = ((Number(deg) % 360) + 360) % 360;
    const nearest = Math.round(wrapped / 90) * 90;
    const delta = Math.min(Math.abs(wrapped - nearest), 360 - Math.abs(wrapped - nearest));
    if (delta <= threshold) return ((nearest % 360) + 360) % 360;
    return wrapped;
}

export function nearestRotateDeg(deg) {
    const wrapped = ((Number(deg) % 360) + 360) % 360;
    return ((Math.round(wrapped / 90) * 90) % 360 + 360) % 360;
}

export function getRotationHandleNorm(dstPoints) {
    if (!dstPoints || dstPoints.length < 4) return null;
    const [tl, tr, br, bl] = dstPoints;
    const cx = (tl.x + tr.x + br.x + bl.x) / 4;
    const cy = (tl.y + tr.y + br.y + bl.y) / 4;
    let dx = ((tl.x + tr.x) / 2) - cx;
    let dy = ((tl.y + tr.y) / 2) - cy;
    let len = Math.hypot(dx, dy);
    if (len < 1e-5) {
        dx = 0;
        dy = -1;
        len = 1;
    }
    const dist = Math.max(0.09, len + 0.07);
    return {
        x: cx + (dx / len) * dist,
        y: cy + (dy / len) * dist,
        cx,
        cy
    };
}

export function getBoardCadCorners(bounds, options = {}) {
    if (!isUsableBounds(bounds)) return null;

    const rotateDeg = normalizeRotateDeg(options);
    const flipH = !!options.flipH;
    const cadYUp = !!options.cadYUp;

    const minX = bounds.minX;
    const maxX = bounds.maxX;
    const minY = bounds.minY;
    const maxY = bounds.maxY;

    const pts = cadYUp
        ? [
            { corner: 'TL', x: minX, y: maxY },
            { corner: 'TR', x: maxX, y: maxY },
            { corner: 'BR', x: maxX, y: minY },
            { corner: 'BL', x: minX, y: minY }
        ]
        : [
            { corner: 'TL', x: minX, y: minY },
            { corner: 'TR', x: maxX, y: minY },
            { corner: 'BR', x: maxX, y: maxY },
            { corner: 'BL', x: minX, y: maxY }
        ];

    const cx = (minX + maxX) / 2;
    const cy = (minY + maxY) / 2;

    if (flipH) {
        for (const p of pts) p.x = cx * 2 - p.x;
    }

    if (Math.abs(rotateDeg) > 1e-9) {
        const rad = rotateDeg * Math.PI / 180;
        const cos = Math.cos(rad);
        const sin = Math.sin(rad);
        for (const p of pts) {
            const dx = p.x - cx;
            const dy = p.y - cy;
            p.x = cx + dx * cos + dy * sin;
            p.y = cy - dx * sin + dy * cos;
        }
    }

    return pts;
}

export function quadArea(pts) {
    if (!pts || pts.length < 3) return 0;
    let sum = 0;
    for (let i = 0; i < pts.length; i++) {
        const a = pts[i];
        const b = pts[(i + 1) % pts.length];
        sum += a.x * b.y - b.x * a.y;
    }
    return sum / 2;
}

export function isValidQuad(pts, minArea = MIN_QUAD_AREA) {
    if (!pts || pts.length < 4) return false;
    const q = pts.slice(0, 4);
    if (!q.every(p => p && Number.isFinite(p.x) && Number.isFinite(p.y))) return false;

    for (let i = 0; i < 4; i++) {
        for (let j = i + 1; j < 4; j++) {
            if (Math.hypot(q[i].x - q[j].x, q[i].y - q[j].y) < COINCIDENT_EPS) return false;
        }
    }

    if (Math.abs(quadArea(q)) < minArea) return false;

    let sign = 0;
    for (let i = 0; i < 4; i++) {
        const a = q[i];
        const b = q[(i + 1) % 4];
        const c = q[(i + 2) % 4];
        const cr = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
        if (Math.abs(cr) < COLLINEAR_EPS) return false;
        const s = cr > 0 ? 1 : -1;
        if (sign === 0) sign = s;
        else if (s !== sign) return false;
    }
    return true;
}

export function pointInConvexQuad(x, y, pts) {
    if (!pts || pts.length < 4 || !Number.isFinite(x) || !Number.isFinite(y)) return false;
    let sign = 0;
    for (let i = 0; i < 4; i++) {
        const a = pts[i];
        const b = pts[(i + 1) % 4];
        const cr = (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x);
        if (Math.abs(cr) < 1e-14) continue;
        const s = cr > 0 ? 1 : -1;
        if (sign === 0) sign = s;
        else if (s !== sign) return false;
    }
    return sign !== 0;
}

export function orderTargetsAsCorners(points) {
    const pts = (points || []).slice(0, 4).map(p => ({
        x: Number(p.targetX ?? p.x),
        y: Number(p.targetY ?? p.y)
    }));
    if (pts.length < 4 || !pts.every(p => Number.isFinite(p.x) && Number.isFinite(p.y))) {
        return DEFAULT_PHOTO_CORNERS.map(p => ({ ...p }));
    }
    const byY = [...pts].sort((a, b) => a.y - b.y);
    const top = byY.slice(0, 2).sort((a, b) => a.x - b.x);
    const bot = byY.slice(2, 4).sort((a, b) => a.x - b.x);
    return [top[0], top[1], bot[1], bot[0]];
}

export function areBoardCornerAnchors(anchors) {
    return Array.isArray(anchors)
        && anchors.length >= 4
        && anchors.slice(0, 4).every(a => a && a.kind === 'board-corner');
}

export function computeLiveHomography(anchors, previousMatrix) {
    if (!anchors || anchors.length < 4) return previousMatrix || null;
    const src = anchors.slice(0, 4).map(a => ({ x: Number(a.cadX), y: Number(a.cadY) }));
    const dst = anchors.slice(0, 4).map(a => ({ x: Number(a.targetX), y: Number(a.targetY) }));
    if (!isValidQuad(src) || !isValidQuad(dst)) return previousMatrix || null;
    return computeHomography4Points(src, dst) || previousMatrix || null;
}

export function projectBoardQuad(matrix, bounds, options) {
    const cad = getBoardCadCorners(bounds, options);
    if (!cad || !matrix) return null;
    return cad.map(c => transformPoint(matrix, c.x, c.y));
}

export function componentOrientedCorners(comp) {
    const box = componentAabb(comp);
    const aabb = box
        ? [
            { x: box.minX, y: box.minY },
            { x: box.maxX, y: box.minY },
            { x: box.maxX, y: box.maxY },
            { x: box.minX, y: box.maxY }
        ]
        : null;

    const pins = [];
    if (comp?.pins && typeof comp.pins === 'object') {
        for (const pin of Object.values(comp.pins)) {
            if (pin && Number.isFinite(pin.x) && Number.isFinite(pin.y)) pins.push(pin);
        }
    }

    if (!pins.length) return aabb;

    // 1 pin (test point / via)
    if (pins.length === 1) {
        if (aabb) {
            aabb.cx = (box.minX + box.maxX) / 2;
            aabb.cy = (box.minY + box.maxY) / 2;
            return aabb;
        }
        return null;
    }

    // 2 pins (resistors, capacitors) — exact contour strictly between two pads at any angle
    if (pins.length === 2) {
        const p1 = pins[0];
        const p2 = pins[1];
        const dx = p2.x - p1.x;
        const dy = p2.y - p1.y;
        const dist = Math.hypot(dx, dy);

        if (dist > 450 || (comp.type && /HEATSINK|HOLE|MECHANICAL|BRACKET/i.test(comp.type))) {
            if (aabb) {
                aabb.cx = (box.minX + box.maxX) / 2;
                aabb.cy = (box.minY + box.maxY) / 2;
                return aabb;
            }
        }

        if (dist > 1e-6) {
            const mx = (p1.x + p2.x) / 2;
            const my = (p1.y + p2.y) / 2;
            const ux = dx / dist;
            const uy = dy / dist;
            const vx = -uy;
            const vy = ux;

            // Contour sits between pad centers
            const halfL = dist * 0.5;
            const halfW = dist * 0.24;

            const res = [
                { x: mx - halfL * ux - halfW * vx, y: my - halfL * uy - halfW * vy },
                { x: mx + halfL * ux - halfW * vx, y: my + halfL * uy - halfW * vy },
                { x: mx + halfL * ux + halfW * vx, y: my + halfL * uy + halfW * vy },
                { x: mx - halfL * ux + halfW * vx, y: my - halfL * uy + halfW * vy }
            ];
            res.cx = mx;
            res.cy = my;
            return res;
        }
    }

    // 3+ pins (ICs, transistors, connectors JSD1, JPWRB1, PU801, PQ502):
    // Matches original BoardViewer: rectangle passes directly through extreme pad centers
    let minX = Infinity, maxX = -Infinity;
    let minY = Infinity, maxY = -Infinity;

    for (const p of pins) {
        if (p.x < minX) minX = p.x;
        if (p.x > maxX) maxX = p.x;
        if (p.y < minY) minY = p.y;
        if (p.y > maxY) maxY = p.y;
    }

    // Geometric center of the part — strictly in the middle
    const cx = (minX + maxX) / 2;
    const cy = (minY + maxY) / 2;

    const res = [
        { x: minX, y: minY },
        { x: maxX, y: minY },
        { x: maxX, y: maxY },
        { x: minX, y: maxY }
    ];
    res.cx = cx;
    res.cy = cy;
    return res;
}

export function isOutlineUsable(outline, bounds) {
    if (!Array.isArray(outline) || outline.length < 3 || !isUsableBounds(bounds)) return false;
    const diag = Math.hypot(bounds.maxX - bounds.minX, bounds.maxY - bounds.minY) || 1;
    let longJumps = 0;
    for (let i = 1; i < outline.length; i++) {
        const a = outline[i - 1];
        const b = outline[i];
        if (!a || !b || !Number.isFinite(a.x) || !Number.isFinite(b.x)) {
            longJumps++;
            continue;
        }
        if (Math.hypot(b.x - a.x, b.y - a.y) > diag * 0.35) longJumps++;
    }
    return longJumps <= Math.max(2, outline.length * 0.08);
}
export function getComponentPhysicalBounds(comp, effectiveMatrix, toX, toY) {
    if (!comp || !effectiveMatrix) return null;

    // 1. Priority: 4 true oriented corners (by outer pin centers)
    const corners = componentOrientedCorners(comp);
    if (corners && corners.length >= 4) {
        const p0 = transformPoint(effectiveMatrix, corners[0].x, corners[0].y);
        const p1 = transformPoint(effectiveMatrix, corners[1].x, corners[1].y);
        const p2 = transformPoint(effectiveMatrix, corners[2].x, corners[2].y);
        const p3 = transformPoint(effectiveMatrix, corners[3].x, corners[3].y);

        const x0 = toX(p0.x), y0 = toY(p0.y);
        const x1 = toX(p1.x), y1 = toY(p1.y);
        const x2 = toX(p2.x), y2 = toY(p2.y);
        const x3 = toX(p3.x), y3 = toY(p3.y);

        const minX = Math.min(x0, x1, x2, x3);
        const maxX = Math.max(x0, x1, x2, x3);
        const minY = Math.min(y0, y1, y2, y3);
        const maxY = Math.max(y0, y1, y2, y3);

        const w = maxX - minX;
        const h = maxY - minY;
        const cx = (x0 + x1 + x2 + x3) / 4;
        const cy = (y0 + y1 + y2 + y3) / 4;

        if (w > 0 && h > 0) return { w, h, cx, cy, minX, maxX, minY, maxY };
    }

    // 2. Fallback: AABB from package dimensions (all 4 vertices)
    const box = componentAabb(comp);
    if (box) {
        const p0 = transformPoint(effectiveMatrix, box.minX, box.minY);
        const p1 = transformPoint(effectiveMatrix, box.maxX, box.minY);
        const p2 = transformPoint(effectiveMatrix, box.maxX, box.maxY);
        const p3 = transformPoint(effectiveMatrix, box.minX, box.maxY);

        const x0 = toX(p0.x), y0 = toY(p0.y);
        const x1 = toX(p1.x), y1 = toY(p1.y);
        const x2 = toX(p2.x), y2 = toY(p2.y);
        const x3 = toX(p3.x), y3 = toY(p3.y);

        const minX = Math.min(x0, x1, x2, x3);
        const maxX = Math.max(x0, x1, x2, x3);
        const minY = Math.min(y0, y1, y2, y3);
        const maxY = Math.max(y0, y1, y2, y3);

        const w = maxX - minX;
        const h = maxY - minY;
        const cx = (x0 + x1 + x2 + x3) / 4;
        const cy = (y0 + y1 + y2 + y3) / 4;

        if (w > 0 && h > 0) return { w, h, cx, cy, minX, maxX, minY, maxY };
    }

    return null;
}