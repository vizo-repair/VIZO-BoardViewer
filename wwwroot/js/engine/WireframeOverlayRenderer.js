// ============================================================================
// FILE: wwwroot/js/engine/WireframeOverlayRenderer.js
// High-contrast OpenBoardView-style renderer
// ============================================================================
import { transformPoint } from './HomographySolver.js';
import { CanvasStore } from '../state/CanvasStore.js';
import {
    componentAabb,
    componentOrientedCorners,
    isOutlineUsable,
    resolveBoardBounds
} from './BoardCorners.js';

const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
const LABEL_ZOOM = 4.0;
const PAD_ZOOM = 0.6;
const HALO = 'rgba(7, 9, 14, 0.88)';
const OUTLINE_FG = '#67e8f9';
const OUTLINE_CORE = '#ecfeff';
const OUTLINE_GLOW = 'rgba(34, 211, 238, 0.65)';
const IC_FG = '#facc15';
const IC_FILL = 'rgba(250, 204, 21, 0.04)';
const TH_FG = '#22c55e';
const TH_FILL = 'rgba(34, 197, 94, 0.06)';
const PASSIVE_FG = 'rgba(148, 163, 184, 0.55)';
const PASSIVE_FILL = 'rgba(255, 255, 255, 0.01)';

const PAD_FG = 'rgba(203, 213, 225, 0.85)';
const PAD_CORE = '#475569';

const VIA_FG = 'rgba(192, 132, 252, 0.65)';
const VIA_CORE = '#7c3aed';
const TP_FG = 'rgba(232, 121, 249, 0.65)';
const TP_CORE = '#a855f7';
const BROKEN_FG = '#fb7185';

let cacheKey = '';
let cache = null;

export function invalidateWireframeCache() {
    cacheKey = '';
    cache = null;
}

function isShortPinName(refDes, type, desc) {
    const u = (refDes || '').toUpperCase();
    if (u.startsWith('SHORTPIN') || u.startsWith('SLN') || u.startsWith('SLP') || u.startsWith('NBP_SHORTPIN')) return true;
    const d = (desc || type || '').toUpperCase();
    return d.includes('SHORTPIN') || d.includes('SHORT_PIN');
}

export function sampleCalibFx(kind, progress) {
    const u = Math.max(0, Math.min(1, Number(progress) || 0));
    const phase = (u * u) * Math.PI * 2 * 5.4;
    const pulse = 0.5 + 0.5 * Math.sin(phase);

    if (kind === 'lock') {
        const fade = u > 0.78 ? 1 - (u - 0.78) / 0.22 : 1;
        const flash = (u > 0.56 && u < 0.78) ? Math.sin((u - 0.56) / 0.22 * Math.PI) : 0;
        return {
            kind,
            progress: u,
            opacity: Math.max(0, fade),
            glow: 0.55 + pulse * 1.05 + flash * 1.7,
            widthMul: 1 + pulse * 0.5 + flash * 1.05,
            fillMul: 1 + pulse * 0.55 + flash * 1.5,
            flash,
            hideHandles: true
        };
    }

    const appear = Math.min(1, u / 0.32);
    const appearE = 1 - (1 - appear) * (1 - appear);
    const flash = (u > 0.52 && u < 0.76) ? Math.sin((u - 0.52) / 0.24 * Math.PI) : 0;
    return {
        kind: 'acquire',
        progress: u,
        opacity: 0.12 + appearE * 0.88,
        glow: 0.25 + pulse * 0.95 * appearE + flash * 1.6,
        widthMul: 0.75 + appearE * 0.35 + pulse * 0.4 + flash * 0.9,
        fillMul: 0.2 + appearE * 0.9 + flash * 1.3,
        flash,
        hideHandles: u < 0.78
    };
}

export function renderWireframeOverlay(ctx, scale, toX, toY, matrix, netlist, side, options = {}) {
    if (!matrix || !netlist || !ctx) return;

    const bounds = resolveBoardBounds(netlist);
    if (!bounds) return;

    const currentSide = normalizeSide(side);
    const packed = getWireframeCache(netlist, currentSide, bounds);
    const zoom = Number.isFinite(options.zoom) && options.zoom > 0 ? options.zoom : (scale || 1);
    const isDragging = !!options.isDragging;
    const opacity = Number.isFinite(options.opacity) ? options.opacity : 0.92;
    const quadBroken = !!options.quadBroken;
    const fx = options.fx || null;
    const lite = isDragging && !fx;

    ctx.save();
    const fxOpacity = fx ? fx.opacity : 1;
    ctx.globalAlpha = Math.max(0.08, Math.min(1, opacity * fxOpacity));
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.shadowBlur = 0;

    if (fx && !lite) {
        ctx.shadowColor = quadBroken ? 'rgba(251, 113, 133, 0.7)' : OUTLINE_GLOW;
        ctx.shadowBlur = (5 + fx.glow * 16) / scale;
    }

    // 1. Board outline
    const outlineFg = quadBroken ? BROKEN_FG : (fx?.flash > 0.35 ? OUTLINE_CORE : OUTLINE_FG);
    const widthMul = fx ? fx.widthMul : 1;
    const outlineW = (lite ? 1.8 : 2.4) * widthMul;
    if (packed.outline) {
        strokeCadPolyline(ctx, matrix, toX, toY, packed.outline, true, outlineFg, outlineW, scale, lite, !quadBroken);
    } else {
        strokeCadBox(ctx, matrix, toX, toY, bounds.minX, bounds.minY, bounds.maxX, bounds.maxY, outlineFg, outlineW, scale, lite, !quadBroken);
    }

    // 2. Component outlines
    const fillMul = fx ? fx.fillMul : 1;
    for (const item of packed.components) {
        let fg = PASSIVE_FG;
        let fill = scaleFill(PASSIVE_FILL, fillMul);
        let w = 0.9 * widthMul;

        if (item.isMechanical) {
            fg = 'rgba(100, 116, 139, 0.35)'; // thin subtle gray line for heatsinks
            fill = 'transparent';
            w = 0.8 * widthMul;
        } else if (item.isTh) {
            fg = TH_FG;
            fill = scaleFill(TH_FILL, fillMul);
            w = 1.3 * widthMul;
        } else if (item.isIc) {
            fg = IC_FG;
            fill = scaleFill(IC_FILL, fillMul);
            w = 1.4 * widthMul;
        }

        strokeCadPolygon(ctx, matrix, toX, toY, item.corners, fg, fill, w, scale, lite);
    }

    // 3. Vias and test points
    if (!lite) {
        fillCadRings(ctx, matrix, toX, toY, packed.vias, VIA_FG, VIA_CORE, 2.0 / scale, 0.9 / scale, scale);
        fillCadRings(ctx, matrix, toX, toY, packed.testPoints, TP_FG, TP_CORE, 2.2 / scale, 0.9 / scale, scale);
    }

    // 4. Pads — always visible
    if (!lite && zoom >= PAD_ZOOM) {
        fillCadPads(ctx, matrix, toX, toY, packed.pads, scale);
    }

    // 5. RefDes text (strictly fitted to outline bounds, scales with zoom)
    if (!lite) {
        const dpr = window.devicePixelRatio || 1;
        const cw = ctx.canvas.width;
        const ch = ctx.canvas.height;
        const panX = CanvasStore.panX;
        const panY = CanvasStore.panY;
        const margin = 50 / scale;
        const viewLeft = -panX / scale - margin;
        const viewRight = (cw / dpr - panX) / scale + margin;
        const viewTop = -panY / scale - margin;
        const viewBottom = (ch / dpr - panY) / scale + margin;

        for (const item of packed.components) {
            if (!item.corners || item.corners.length < 4) continue;

            // Do not draw giant text for heatsinks and mounting frames over chips
            if (item.isMechanical) continue;

            const normMid = transformPoint(matrix, item.cx, item.cy);
            const mx = toX(normMid.x);
            const my = toY(normMid.y);

            // Off-screen culling
            if (mx < viewLeft || mx > viewRight || my < viewTop || my > viewBottom) {
                continue;
            }

            // Outline corner coordinates
            const p0 = transformPoint(matrix, item.corners[0].x, item.corners[0].y);
            const p1 = transformPoint(matrix, item.corners[1].x, item.corners[1].y);
            const p2 = transformPoint(matrix, item.corners[2].x, item.corners[2].y);

            const x0 = toX(p0.x), y0 = toY(p0.y);
            const x1 = toX(p1.x), y1 = toY(p1.y);
            const x2 = toX(p2.x), y2 = toY(p2.y);

            // Outline dimensions in canvas coordinates
            const edge01 = Math.hypot(x1 - x0, y1 - y0); // X width
            const edge12 = Math.hypot(x2 - x1, y2 - y1); // Y height

            // Vertical text ONLY if height is noticeably larger than width (by 15%+)
            // On square chips (like U11) and horizontal parts, text is STRICTLY horizontal
            const isVertical = (edge12 > edge01 * 1.15);

            const len = isVertical ? edge12 : edge01;
            const wid = isVertical ? edge01 : edge12;

            // Angle of the long edge
            let angle = isVertical
                ? Math.atan2(y2 - y1, x2 - x1)
                : Math.atan2(y1 - y0, x1 - x0);

            // Normalize angle to [-PI/2, PI/2] to avoid upside-down text
            if (angle > Math.PI / 2) angle -= Math.PI;
            else if (angle < -Math.PI / 2) angle += Math.PI;

            const label = item.refDes.toUpperCase();
            const charCount = Math.max(label.length, 1);

            // FONT STRICTLY BOUND TO PART OUTLINE:
            // String length occupies ~70% of outline length, height ~65% of outline width
            const fsFromLen = (len * 0.70) / (charCount * 0.58);
            const fsFromWid = wid * 0.65;
            const fs = Math.min(fsFromLen, fsFromWid);

            // When zoomed far out and text is smaller than 3px on screen, skip rendering dust
            if (fs * scale * dpr < 2.5) continue;

            const isLarge = item.isIc || item.isTh || (fs * scale * dpr > 24);

            ctx.save();
            ctx.translate(mx, my);
            ctx.rotate(angle);

            ctx.font = `bold ${fs}px ${FONT_FAMILY}`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';

            if (isLarge) {
                // Large labels (U1, PCH1, etc.): semi-transparent watermark with soft outline
                ctx.strokeStyle = 'rgba(7, 9, 14, 0.30)';
                ctx.lineWidth = Math.min(fs * 0.08, 6.0 / scale); // Cap stroke width so it does not fill black
                ctx.strokeText(label, 0, 0);

                // Semi-transparent fill (~35-38%): text stays readable while pads underneath remain visible
                ctx.fillStyle = item.isIc
                    ? 'rgba(254, 240, 138, 0.38)'
                    : (item.isTh ? 'rgba(134, 239, 172, 0.35)' : 'rgba(226, 232, 240, 0.30)');
                ctx.fillText(label, 0, 0);
            } else {
                // For small discretes (R12, C45), keep dense high-contrast font
                ctx.strokeStyle = HALO;
                ctx.lineWidth = Math.max(0.8 / scale, fs * 0.16);
                ctx.strokeText(label, 0, 0);

                ctx.fillStyle = item.isIc ? '#fef08a' : (item.isTh ? '#86efac' : '#e2e8f0');
                ctx.fillText(label, 0, 0);
            }

            ctx.restore();
        }
    }

    ctx.restore();
}

function getWireframeCache(netlist, side, bounds) {
    const compCount = netlist.components ? Object.keys(netlist.components).length : 0;
    const tpCount = Array.isArray(netlist.testPoints) ? netlist.testPoints.length : 0;
    const key = `${netlist.board || ''}|${netlist.format || ''}|${side}|${compCount}|${tpCount}|obv_style_v6`;
    if (cache && cacheKey === key) return cache;

    const outline = isOutlineUsable(bounds.outline, bounds) ? bounds.outline : null;
    const allComponents = [];
    const pads = [];

    if (netlist.components) {
        for (const [refDes, comp] of Object.entries(netlist.components)) {
            if (!comp) continue;
            if (normalizeSide(comp.side) !== side && String(comp.side).toUpperCase() !== 'BOTH') continue;
            const corners = componentOrientedCorners(comp);
            if (!corners) continue;

            const uRef = refDes.toUpperCase();
            const isMechanical = comp.type === 'Mechanical' ||
                uRef.startsWith('HEATSINK') || uRef.startsWith('BRACKET') ||
                uRef.startsWith('SHIELD') || uRef.startsWith('FRAME') ||
                uRef.startsWith('STANDOFF') || uRef.startsWith('STUDFIX');

            const isHole = /^H\d|^MH\d|^HOLE/i.test(uRef);
            const isTh = !isMechanical && (String(comp.side).toUpperCase() === 'BOTH' || comp.type === 'TH' ||
                uRef.startsWith('DIMM') || uRef.startsWith('PCI') || uRef.startsWith('J') || uRef.startsWith('CN') || isHole);
            const isIc = !isMechanical && (uRef.startsWith('PU') || uRef.startsWith('U') || uRef.startsWith('UE') || uRef.startsWith('UV') || uRef.startsWith('QA') ||
                uRef.startsWith('PCH') || uRef.startsWith('CPU') || uRef.startsWith('GPU') || uRef.startsWith('SOC'));

            allComponents.push({
                refDes,
                isIc,
                isTh,
                isMechanical,
                corners,
                cx: corners.cx !== undefined ? corners.cx : comp.x,
                cy: corners.cy !== undefined ? corners.cy : comp.y
            });

            if (comp.pins && typeof comp.pins === 'object') {
                for (const pin of Object.values(comp.pins)) {
                    if (!pin || !Number.isFinite(pin.x) || !Number.isFinite(pin.y)) continue;
                    const pinLayer = String(pin.layer || comp.side || 'TOP').toUpperCase();
                    if (pinLayer !== 'BOTH' && normalizeSide(pinLayer) !== side) continue;
                    pads.push({ x: pin.x, y: pin.y });
                }
            }
        }
    }

    const vias = [];
    const testPoints = [];
    if (Array.isArray(netlist.testPoints)) {
        for (const tp of netlist.testPoints) {
            if (!tp || !Number.isFinite(tp.x) || !Number.isFinite(tp.y)) continue;
            if (normalizeSide(tp.side) !== side) continue;
            const isVia = !!(tp.isVia || tp.pointType === 'VIA' || String(tp.nail || '').startsWith('$'));
            if (isVia) vias.push({ x: tp.x, y: tp.y });
            else testPoints.push({ x: tp.x, y: tp.y });
        }
    }

    cacheKey = key;
    cache = {
        outline,
        components: allComponents,
        pads,
        vias,
        testPoints
    };
    return cache;
}

function normalizeSide(side) {
    return String(side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP';
}

function scaleFill(rgba, mul) {
    if (!mul || mul === 1) return rgba;
    const m = rgba.match(/rgba?\((\d+),\s*(\d+),\s*(\d+),\s*([0-9.]+)\)/);
    if (!m) return rgba;
    const a = Math.max(0, Math.min(0.35, Number(m[4]) * mul));
    return `rgba(${m[1]}, ${m[2]}, ${m[3]}, ${a})`;
}

function fillCadPads(ctx, matrix, toX, toY, points, scale) {
    if (!points || !points.length) return;
    const outer = Math.max(1.3 / scale, 0.7);
    const inner = Math.max(outer * 0.45, 0.35);
    fillCadDots(ctx, matrix, toX, toY, points, PAD_FG, outer);
    fillCadDots(ctx, matrix, toX, toY, points, PAD_CORE, inner);
}

function fillCadRings(ctx, matrix, toX, toY, points, ring, core, radius, lineW, scale) {
    if (!points || !points.length) return;
    const r = Math.max(radius, 0.85);
    ctx.strokeStyle = ring;
    ctx.lineWidth = Math.max(lineW, 0.7 / scale);
    ctx.beginPath();
    for (const pt of points) {
        const p = transformPoint(matrix, pt.x, pt.y);
        const x = toX(p.x);
        const y = toY(p.y);
        ctx.moveTo(x + r, y);
        ctx.arc(x, y, r, 0, Math.PI * 2);
    }
    ctx.stroke();
    fillCadDots(ctx, matrix, toX, toY, points, core, Math.max(r * 0.32, 0.4));
}

function fillCadDots(ctx, matrix, toX, toY, points, color, radius) {
    if (!points || !points.length) return;
    const r = Math.max(radius, 0.45);
    ctx.fillStyle = color;
    ctx.beginPath();
    for (const pt of points) {
        const p = transformPoint(matrix, pt.x, pt.y);
        const x = toX(p.x);
        const y = toY(p.y);
        ctx.moveTo(x + r, y);
        ctx.arc(x, y, r, 0, Math.PI * 2);
    }
    ctx.fill();
}

function strokeCadPolygon(ctx, matrix, toX, toY, corners, fg, fill, width, scale, lite) {
    if (!corners || corners.length < 2) return;
    ctx.beginPath();
    const first = transformPoint(matrix, corners[0].x, corners[0].y);
    ctx.moveTo(toX(first.x), toY(first.y));
    for (let i = 1; i < corners.length; i++) {
        const p = transformPoint(matrix, corners[i].x, corners[i].y);
        ctx.lineTo(toX(p.x), toY(p.y));
    }
    ctx.closePath();
    if (fill && !lite) {
        ctx.fillStyle = fill;
        ctx.fill();
    }
    ctx.strokeStyle = fg;
    ctx.lineWidth = width / scale;
    ctx.stroke();
}

function strokeCadBox(ctx, matrix, toX, toY, minX, minY, maxX, maxY, fg, width, scale, lite, neon) {
    strokeCadPolyline(ctx, matrix, toX, toY, [
        { x: minX, y: minY },
        { x: maxX, y: minY },
        { x: maxX, y: maxY },
        { x: minX, y: maxY }
    ], true, fg, width, scale, lite, neon);
}

function strokeCadPolyline(ctx, matrix, toX, toY, points, closePath, fg, width, scale, lite, neon) {
    if (!points || points.length < 2) return;
    ctx.beginPath();
    const first = transformPoint(matrix, points[0].x, points[0].y);
    ctx.moveTo(toX(first.x), toY(first.y));
    for (let i = 1; i < points.length; i++) {
        const p = transformPoint(matrix, points[i].x, points[i].y);
        ctx.lineTo(toX(p.x), toY(p.y));
    }
    if (closePath) ctx.closePath();
    ctx.strokeStyle = fg;
    ctx.lineWidth = width / scale;
    ctx.stroke();
}