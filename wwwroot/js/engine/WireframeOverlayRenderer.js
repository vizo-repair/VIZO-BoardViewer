// ============================================================================
// FILE: wwwroot/js/engine/WireframeOverlayRenderer.js
// Engineering board render (19 ComponentKind classes, adaptive pads, Pin 1, Dimming, Hover)
// ============================================================================
import { transformPoint } from './HomographySolver.js';
import { CanvasStore } from '../state/CanvasStore.js';
import {
    COMPONENT_KINDS,
    getComponentKind,
    getComponentKindStyle,
    isMechanicalKind,
    classifyNetKind
} from './ComponentKind.js';
import {
    componentAabb,
    componentOrientedCorners,
    isOutlineUsable,
    resolveBoardBounds
} from './BoardCorners.js';

const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
const PAD_ZOOM = 0.55;
const HALO = 'rgba(7, 9, 14, 0.88)';
const OUTLINE_FG = '#67e8f9';
const OUTLINE_CORE = '#ecfeff';
const OUTLINE_GLOW = 'rgba(34, 211, 238, 0.65)';

const VIA_FG = 'rgba(148, 132, 184, 0.36)';
const VIA_CORE = 'rgba(116, 94, 162, 0.48)';
const TP_FG = 'rgba(182, 128, 198, 0.45)';
const TP_CORE = 'rgba(142, 86, 170, 0.55)';
const BROKEN_FG = '#fb7185';

// Calm contact pad colors (silver for active signals, muted GND / NC, gold Pin 1)
const PAD_SIGNAL_FG = 'rgba(196, 208, 222, 0.76)';
const PAD_SIGNAL_CORE = '#3b4758';
const PAD_SIGNAL_RING = 'rgba(196, 210, 226, 0.78)';
const PAD_SIGNAL_INNER = 'rgba(12, 18, 30, 0.88)';

const PAD_GND_FG = 'rgba(66, 114, 162, 0.72)';
const PAD_GND_CORE = '#111c2d';
const PAD_GND_RING = 'rgba(94, 148, 204, 0.74)';
const PAD_GND_INNER = 'rgba(16, 29, 46, 0.88)';

const PAD_NC_FG = 'rgba(56, 68, 86, 0.42)';
const PAD_NC_CORE = 'rgba(15, 23, 42, 0.85)';
const PAD_NC_RING = 'rgba(82, 96, 116, 0.46)';
const PAD_NC_INNER = 'rgba(15, 22, 34, 0.74)';

const PAD_PIN1_FG = 'rgba(218, 172, 42, 0.84)';
const PAD_PIN1_RING = 'rgba(234, 188, 56, 0.84)';
const PAD_PIN1_INNER = 'rgba(28, 22, 10, 0.90)';

const DIM_FACTOR = 0.27;
const DIM_PAD_FACTOR = 0.24;

let cacheKey = '';
let cache = null;

export function invalidateWireframeCache() {
    cacheKey = '';
    cache = null;
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

    const inspectedNet = (!fx && options.inspectedNet && options.inspectedNet.netName)
        ? options.inspectedNet
        : null;
    const activeCompRefs = inspectedNet?.componentRefs || null;
    const hasDimming = !!(inspectedNet && activeCompRefs && activeCompRefs.size > 0);
    const selectedNetUpper = inspectedNet ? inspectedNet.netName.toUpperCase() : '';

    const hoverHit = (!fx && !lite && options.hoverHit) ? options.hoverHit : null;
    const hoveredRefUpper = (hoverHit && !hoverHit.isTestPoint && hoverHit.refDes)
        ? hoverHit.refDes.toUpperCase()
        : null;

    const cadToCanvas = computeCadToCanvasScale(matrix, toX, toY, bounds);
    const view = computeViewportBounds(ctx, scale);

    ctx.save();
    const fxOpacity = fx ? fx.opacity : 1;
    const baseAlpha = Math.max(0.08, Math.min(1, opacity * fxOpacity));
    ctx.globalAlpha = baseAlpha;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.shadowBlur = 0;

    if (fx && !lite) {
        ctx.shadowColor = quadBroken ? 'rgba(251, 113, 133, 0.7)' : OUTLINE_GLOW;
        ctx.shadowBlur = (5 + fx.glow * 16) / scale;
    }

    // 1. Board outline
    ctx.globalAlpha = hasDimming ? baseAlpha * 0.45 : baseAlpha;
    const outlineFg = quadBroken ? BROKEN_FG : (fx?.flash > 0.35 ? OUTLINE_CORE : OUTLINE_FG);
    const widthMul = fx ? fx.widthMul : 1;
    const outlineW = (lite ? 1.8 : 2.4) * widthMul;
    if (packed.outline) {
        strokeCadPolyline(ctx, matrix, toX, toY, packed.outline, true, outlineFg, outlineW, scale);
    } else {
        strokeCadBox(ctx, matrix, toX, toY, bounds.minX, bounds.minY, bounds.maxX, bounds.maxY, outlineFg, outlineW, scale);
    }

    // 2. Component outlines (19 ComponentKind classes + background dimming on active net)
    const fillMul = fx ? fx.fillMul : 1;
    if (hasDimming) {
        // 2a. Dimmed background components
        ctx.globalAlpha = baseAlpha * DIM_FACTOR;
        for (const item of packed.components) {
            if (activeCompRefs.has(item.refUpper) || item.refUpper === hoveredRefUpper) continue;
            drawComponentBody(ctx, matrix, toX, toY, item, fillMul, widthMul, scale, lite, false);
        }
        // 2b. Highlighted components on the selected net (or hovered)
        ctx.globalAlpha = baseAlpha;
        for (const item of packed.components) {
            const isNetComp = activeCompRefs.has(item.refUpper);
            if (!isNetComp && item.refUpper !== hoveredRefUpper) continue;
            drawComponentBody(ctx, matrix, toX, toY, item, fillMul, widthMul, scale, lite, isNetComp);
        }
    } else {
        ctx.globalAlpha = baseAlpha;
        for (const item of packed.components) {
            drawComponentBody(ctx, matrix, toX, toY, item, fillMul, widthMul, scale, lite, false);
        }
    }

    // 3. Vias and test points
    if (!lite) {
        ctx.globalAlpha = hasDimming ? baseAlpha * DIM_PAD_FACTOR : baseAlpha;
        fillCadRings(ctx, matrix, toX, toY, packed.vias, VIA_FG, VIA_CORE, 2.0 / scale, 0.9 / scale, scale, view);
        fillCadRings(ctx, matrix, toX, toY, packed.testPoints, TP_FG, TP_CORE, 2.2 / scale, 0.9 / scale, scale, view);
    }

    // 4. Large RefDes watermarks (under pads so pin numbers stay clear)
    if (!lite) {
        renderComponentLabels(ctx, matrix, toX, toY, packed.components, scale, view, baseAlpha, hasDimming, activeCompRefs, hoveredRefUpper, true);
    }

    // 5. Adaptive contact pads: pitch-based size, Pin 1, GND/NC/Signal colors, inner pin/net labels on zoom
    if (!lite && zoom >= PAD_ZOOM) {
        renderAdaptivePads(
            ctx, matrix, toX, toY, packed.pads, scale, cadToCanvas, view,
            baseAlpha, hasDimming, activeCompRefs, selectedNetUpper, hoveredRefUpper
        );
    }

    // 6. Small high-contrast RefDes labels (resistors, capacitors)
    if (!lite) {
        renderComponentLabels(ctx, matrix, toX, toY, packed.components, scale, view, baseAlpha, hasDimming, activeCompRefs, hoveredRefUpper, false);
    }

    // 7. Hover highlight of the full component and all its pads
    if (hoveredRefUpper) {
        ctx.globalAlpha = baseAlpha;
        drawHoverHighlightInternal(ctx, matrix, toX, toY, packed, hoveredRefUpper, hoverHit, scale, cadToCanvas);
    }

    ctx.restore();
}

export function renderHoverComponentHighlight(ctx, scale, toX, toY, matrix, netlist, side, hoverHit) {
    if (!ctx || !matrix || !netlist || !hoverHit || hoverHit.isTestPoint || !hoverHit.refDes) return;
    const bounds = resolveBoardBounds(netlist);
    if (!bounds) return;

    const currentSide = normalizeSide(side);
    const packed = getWireframeCache(netlist, currentSide, bounds);
    const hoveredRefUpper = hoverHit.refDes.toUpperCase();
    const cadToCanvas = computeCadToCanvasScale(matrix, toX, toY, bounds);

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    drawHoverHighlightInternal(ctx, matrix, toX, toY, packed, hoveredRefUpper, hoverHit, scale, cadToCanvas);
    ctx.restore();
}

function drawComponentBody(ctx, matrix, toX, toY, item, fillMul, widthMul, scale, lite, isHighlightedNet) {
    const style = item.style;
    const fg = style.fg;
    const fill = style.fill ? scaleFill(style.fill, fillMul) : null;
    const w = (style.width + (isHighlightedNet ? 0.55 : 0)) * widthMul;
    const dashed = !lite && style.dashed;

    strokeCadPolygon(ctx, matrix, toX, toY, item.corners, fg, fill, w, scale, lite, dashed);

    // Subtle bright rim over components on the selected net
    if (isHighlightedNet && !lite) {
        ctx.save();
        ctx.strokeStyle = 'rgba(224, 242, 254, 0.55)';
        ctx.lineWidth = 0.85 / scale;
        ctx.beginPath();
        const first = transformPoint(matrix, item.corners[0].x, item.corners[0].y);
        ctx.moveTo(toX(first.x), toY(first.y));
        for (let i = 1; i < item.corners.length; i++) {
            const p = transformPoint(matrix, item.corners[i].x, item.corners[i].y);
            ctx.lineTo(toX(p.x), toY(p.y));
        }
        ctx.closePath();
        ctx.stroke();
        ctx.restore();
    }
}

function drawHoverHighlightInternal(ctx, matrix, toX, toY, packed, hoveredRefUpper, hoverHit, scale, cadToCanvas) {
    const item = packed.compByRefUpper.get(hoveredRefUpper);
    if (!item || !item.corners || item.corners.length < 2) return;

    const accent = resolveHoverAccentColor(item.kind, item.style);

    // 1. Soft component outline highlight
    ctx.save();
    ctx.beginPath();
    const first = transformPoint(matrix, item.corners[0].x, item.corners[0].y);
    ctx.moveTo(toX(first.x), toY(first.y));
    for (let i = 1; i < item.corners.length; i++) {
        const p = transformPoint(matrix, item.corners[i].x, item.corners[i].y);
        ctx.lineTo(toX(p.x), toY(p.y));
    }
    ctx.closePath();

    ctx.fillStyle = withAlpha(accent, 0.14);
    ctx.fill();

    ctx.shadowColor = accent;
    ctx.shadowBlur = 8 / scale;
    ctx.strokeStyle = accent;
    ctx.lineWidth = 1.9 / scale;
    ctx.stroke();
    ctx.restore();

    // 2. Highlight all pads of the hovered component
    const compPads = packed.padsByRefUpper.get(hoveredRefUpper);
    if (!compPads || !compPads.length) return;

    const snappedPinStr = (hoverHit?.isPinSnapped && hoverHit.snappedPin != null)
        ? String(hoverHit.snappedPin).toUpperCase()
        : null;

    ctx.save();
    ctx.shadowBlur = 0;
    for (const pad of compPads) {
        const p = transformPoint(matrix, pad.x, pad.y);
        const x = toX(p.x);
        const y = toY(p.y);
        const r = computePadCanvasRadius(pad, cadToCanvas, scale);
        if (r * scale < 0.4) continue;

        const isSnappedPin = snappedPinStr && pad.pinKeyUpper === snappedPinStr;
        const ringColor = isSnappedPin ? '#facc15' : (pad.isPin1 ? '#fde047' : '#e0f2fe');
        const expand = (isSnappedPin ? 1.8 : 0.9) / scale;

        ctx.strokeStyle = ringColor;
        ctx.lineWidth = (isSnappedPin ? 2.0 : 1.2) / scale;
        ctx.beginPath();
        if (pad.isPin1) {
            const half = r + expand;
            const cr = half * 0.24;
            if (ctx.roundRect) ctx.roundRect(x - half, y - half, half * 2, half * 2, cr);
            else ctx.rect(x - half, y - half, half * 2, half * 2);
        } else {
            ctx.arc(x, y, r + expand, 0, Math.PI * 2);
        }
        ctx.stroke();
    }
    ctx.restore();
}

function renderAdaptivePads(ctx, matrix, toX, toY, pads, scale, cadToCanvas, view, baseAlpha, hasDimming, activeCompRefs, selectedNetUpper, hoveredRefUpper) {
    if (!pads || !pads.length) return;

    const dpr = window.devicePixelRatio || 1;
    let currentAlpha = -1;

    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';

    for (let i = 0; i < pads.length; i++) {
        const pad = pads[i];
        const p = transformPoint(matrix, pad.x, pad.y);
        const x = toX(p.x);
        const y = toY(p.y);

        if (x < view.left || x > view.right || y < view.top || y > view.bottom) continue;

        const r = computePadCanvasRadius(pad, cadToCanvas, scale);
        const screenR = r * scale;
        if (screenR < 0.35) continue;

        const screenDia = screenR * 2;
        const isCompActive = !hasDimming
            || activeCompRefs.has(pad.refUpper)
            || pad.refUpper === hoveredRefUpper
            || (selectedNetUpper && pad.netUpper === selectedNetUpper);

        const targetAlpha = isCompActive ? baseAlpha : baseAlpha * DIM_PAD_FACTOR;
        if (targetAlpha !== currentAlpha) {
            ctx.globalAlpha = targetAlpha;
            currentAlpha = targetAlpha;
        }

        const showPinLabel = screenDia >= 11;
        const showNetLabel = screenDia >= 30 && !!pad.shortNet;

        if (pad.isPin1) {
            drawPin1SquarePad(ctx, x, y, r, scale, showPinLabel, screenDia);
        } else {
            drawCircularPad(ctx, x, y, r, scale, pad.padType, showPinLabel, screenDia);
        }

        if (showPinLabel) {
            drawPadInnerText(ctx, x, y, r, scale, dpr, pad, showNetLabel);
        }
    }
}

function drawPin1SquarePad(ctx, x, y, r, scale, showText, screenDia) {
    const size = r * 2;
    const cornerR = r * 0.22;

    if (showText) {
        ctx.fillStyle = PAD_PIN1_INNER;
        ctx.strokeStyle = PAD_PIN1_RING;
        ctx.lineWidth = 1.35 / scale;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x - r, y - r, size, size, cornerR);
        else ctx.rect(x - r, y - r, size, size);
        ctx.fill();
        ctx.stroke();

        // Small gold key dot in top-left corner of Pin 1 pad at high zoom
        if (screenDia >= 18) {
            const dotR = Math.min(1.6 / scale, r * 0.11);
            ctx.fillStyle = PAD_PIN1_RING;
            ctx.beginPath();
            ctx.arc(x - r * 0.64, y - r * 0.64, dotR, 0, Math.PI * 2);
            ctx.fill();
        }
    } else {
        ctx.fillStyle = PAD_PIN1_FG;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x - r, y - r, size, size, cornerR);
        else ctx.rect(x - r, y - r, size, size);
        ctx.fill();

        if (screenDia >= 4.5) {
            ctx.strokeStyle = '#e5c158';
            ctx.lineWidth = 0.85 / scale;
            ctx.stroke();

            ctx.fillStyle = '#2c2008';
            ctx.beginPath();
            ctx.arc(x, y, Math.max(r * 0.34, 0.35), 0, Math.PI * 2);
            ctx.fill();
        }
    }
}

function drawCircularPad(ctx, x, y, r, scale, padType, showText, screenDia) {
    if (showText) {
        let innerFill = PAD_SIGNAL_INNER;
        let ringStroke = PAD_SIGNAL_RING;
        if (padType === 'GND') {
            innerFill = PAD_GND_INNER;
            ringStroke = PAD_GND_RING;
        } else if (padType === 'NC') {
            innerFill = PAD_NC_INNER;
            ringStroke = PAD_NC_RING;
        }

        ctx.fillStyle = innerFill;
        ctx.strokeStyle = ringStroke;
        ctx.lineWidth = 1.15 / scale;
        ctx.beginPath();
        ctx.arc(x, y, r, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        return;
    }

    // Compact mode (screenDia < 11 px)
    let outerColor = PAD_SIGNAL_FG;
    let coreColor = PAD_SIGNAL_CORE;
    if (padType === 'GND') {
        outerColor = PAD_GND_FG;
        coreColor = PAD_GND_CORE;
    } else if (padType === 'NC') {
        outerColor = PAD_NC_FG;
        coreColor = PAD_NC_CORE;
    }

    ctx.fillStyle = outerColor;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();

    if (screenDia >= 3.2) {
        ctx.fillStyle = coreColor;
        ctx.beginPath();
        ctx.arc(x, y, Math.max(r * 0.44, 0.3), 0, Math.PI * 2);
        ctx.fill();

        // Subtle GND bar inside ground pads at medium zoom
        if (padType === 'GND' && screenDia >= 5.5) {
            ctx.strokeStyle = 'rgba(125, 175, 222, 0.72)';
            ctx.lineWidth = 0.75 / scale;
            ctx.beginPath();
            ctx.moveTo(x - r * 0.42, y);
            ctx.lineTo(x + r * 0.42, y);
            ctx.stroke();
        }
    }
}

function drawPadInnerText(ctx, x, y, r, scale, dpr, pad, showNetLabel) {
    const pinText = pad.pinLabel;
    if (!pinText && !showNetLabel) return;

    let pinColor = '#e2e8f0';
    if (pad.isPin1) pinColor = '#f3dc8a';
    else if (pad.padType === 'GND') pinColor = '#b4d4f0';
    else if (pad.padType === 'NC') pinColor = '#7c8ca3';

    if (showNetLabel) {
        if (pinText) {
            const maxW = r * 1.42;
            const fsPin = Math.min(r * 0.52, maxW / Math.max(pinText.length * 0.62, 1));
            if (fsPin * scale * dpr >= 4.5) {
                ctx.font = `bold ${fsPin}px ${FONT_FAMILY}`;
                ctx.fillStyle = pinColor;
                ctx.fillText(pinText, x, y - r * 0.27);
            }
        }

        let netText = pad.shortNet;
        const maxNetW = r * 1.58;
        const fsNet = Math.min(r * 0.38, maxNetW / Math.max(Math.min(netText.length, 7) * 0.58, 1));
        if (fsNet * scale * dpr >= 4.0) {
            ctx.font = `600 ${fsNet}px ${FONT_FAMILY}`;
            const maxChars = Math.max(3, Math.floor(maxNetW / Math.max(fsNet * 0.58, 1e-4)));
            if (netText.length > maxChars) {
                netText = netText.slice(0, maxChars - 1) + '…';
            }

            let netColor = '#94c5d8';
            if (pad.padType === 'GND') netColor = '#7eb0d8';
            else if (pad.padType === 'NC') netColor = '#64748b';
            else if (pad.padType === 'POWER') netColor = '#d9b98c';

            ctx.fillStyle = netColor;
            ctx.fillText(netText, x, y + r * 0.30);
        }
    } else if (pinText) {
        const maxW = r * 1.48;
        const fs = Math.min(r * 0.82, maxW / Math.max(pinText.length * 0.62, 1));
        if (fs * scale * dpr >= 4.5) {
            ctx.font = `bold ${fs}px ${FONT_FAMILY}`;
            ctx.fillStyle = pinColor;
            ctx.fillText(pinText, x, y);
        }
    }
}

function renderComponentLabels(ctx, matrix, toX, toY, components, scale, view, baseAlpha, hasDimming, activeCompRefs, hoveredRefUpper, largePass) {
    const dpr = window.devicePixelRatio || 1;
    let currentAlpha = -1;

    for (const item of components) {
        if (!item.corners || item.corners.length < 4) continue;
        if (item.style.hideText) continue;

        const normMid = transformPoint(matrix, item.cx, item.cy);
        const mx = toX(normMid.x);
        const my = toY(normMid.y);

        if (mx < view.left || mx > view.right || my < view.top || my > view.bottom) continue;

        const p0 = transformPoint(matrix, item.corners[0].x, item.corners[0].y);
        const p1 = transformPoint(matrix, item.corners[1].x, item.corners[1].y);
        const p2 = transformPoint(matrix, item.corners[2].x, item.corners[2].y);

        const x0 = toX(p0.x), y0 = toY(p0.y);
        const x1 = toX(p1.x), y1 = toY(p1.y);
        const x2 = toX(p2.x), y2 = toY(p2.y);

        const edge01 = Math.hypot(x1 - x0, y1 - y0);
        const edge12 = Math.hypot(x2 - x1, y2 - y1);
        const isVertical = (edge12 > edge01 * 1.15);

        const len = isVertical ? edge12 : edge01;
        const wid = isVertical ? edge01 : edge12;

        const label = item.refUpper;
        const charCount = Math.max(label.length, 1);

        const fsFromLen = (len * 0.70) / (charCount * 0.58);
        const fsFromWid = wid * 0.65;
        const fs = Math.min(fsFromLen, fsFromWid);

        if (fs * scale * dpr < 2.5) continue;

        const isLarge = item.style.isKey || (item.pinsCount >= 6) || (fs * scale * dpr > 24);
        if (isLarge !== largePass) continue;

        let angle = isVertical
            ? Math.atan2(y2 - y1, x2 - x1)
            : Math.atan2(y1 - y0, x1 - x0);

        if (angle > Math.PI / 2) angle -= Math.PI;
        else if (angle < -Math.PI / 2) angle += Math.PI;

        const isActive = !hasDimming || activeCompRefs.has(item.refUpper) || item.refUpper === hoveredRefUpper;
        const targetAlpha = isActive ? baseAlpha : baseAlpha * DIM_FACTOR;
        if (targetAlpha !== currentAlpha) {
            ctx.globalAlpha = targetAlpha;
            currentAlpha = targetAlpha;
        }

        ctx.save();
        ctx.translate(mx, my);
        ctx.rotate(angle);

        ctx.font = `bold ${fs}px ${FONT_FAMILY}`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        if (isLarge) {
            ctx.strokeStyle = 'rgba(7, 9, 14, 0.30)';
            ctx.lineWidth = Math.min(fs * 0.08, 6.0 / scale);
            ctx.strokeText(label, 0, 0);

            ctx.fillStyle = item.style.watermark || 'rgba(226, 232, 240, 0.32)';
            ctx.fillText(label, 0, 0);
        } else {
            ctx.strokeStyle = HALO;
            ctx.lineWidth = Math.max(0.8 / scale, fs * 0.16);
            ctx.strokeText(label, 0, 0);

            ctx.fillStyle = item.style.text || '#e2e8f0';
            ctx.fillText(label, 0, 0);
        }

        ctx.restore();
    }
}

function getWireframeCache(netlist, side, bounds) {
    const compCount = netlist.components ? Object.keys(netlist.components).length : 0;
    const tpCount = Array.isArray(netlist.testPoints) ? netlist.testPoints.length : 0;
    const key = `${netlist.board || ''}|${netlist.format || ''}|${side}|${compCount}|${tpCount}|obv_style_v9`;
    if (cache && cacheKey === key) return cache;

    const outline = isOutlineUsable(bounds.outline, bounds) ? bounds.outline : null;
    const boardShortSide = Math.min(bounds.width || 1, bounds.height || 1);
    const boardDiag = Math.hypot(bounds.width || 1, bounds.height || 1);
    const absMaxPadCad = Math.max(1e-3, boardShortSide * 0.008);
    const defaultSinglePadCad = Math.max(1e-3, boardDiag * 0.0012);

    const allComponents = [];
    const pads = [];
    const compByRefUpper = new Map();
    const padsByRefUpper = new Map();
    const twoPinRadii = [];
    const multiPinRadii = [];

    if (netlist.components) {
        for (const [refDes, comp] of Object.entries(netlist.components)) {
            if (!comp) continue;
            const compSideUpper = String(comp.side || 'TOP').toUpperCase();
            if (normalizeSide(compSideUpper) !== side && compSideUpper !== 'BOTH') continue;
            const corners = componentOrientedCorners(comp);
            if (!corners) continue;

            const refUpper = refDes.toUpperCase();
            const effectiveKind = resolveEffectiveKind(refUpper, comp);
            const style = getComponentKindStyle(effectiveKind);
            const isMechanical = isMechanicalKind(effectiveKind);

            const totalPinsCount = comp.pinsCount > 0
                ? comp.pinsCount
                : (comp.pins && typeof comp.pins === 'object' ? Object.keys(comp.pins).length : 0);

            const item = {
                refDes,
                refUpper,
                kind: effectiveKind,
                style,
                isMechanical,
                pinsCount: totalPinsCount,
                corners,
                cx: corners.cx !== undefined ? corners.cx : comp.x,
                cy: corners.cy !== undefined ? corners.cy : comp.y
            };
            allComponents.push(item);
            compByRefUpper.set(refUpper, item);

            if (comp.pins && typeof comp.pins === 'object') {
                const visiblePins = [];
                for (const [pinKey, pin] of Object.entries(comp.pins)) {
                    if (!pin || !Number.isFinite(pin.x) || !Number.isFinite(pin.y)) continue;
                    const pinLayer = String(pin.layer || comp.side || 'TOP').toUpperCase();
                    if (pinLayer !== 'BOTH' && normalizeSide(pinLayer) !== side) continue;
                    visiblePins.push({ pinKey, pin });
                }

                if (visiblePins.length > 0) {
                    const { cadRadius, minPitchCad } = computeComponentPadMetrics(
                        comp, visiblePins, absMaxPadCad, defaultSinglePadCad, boardDiag
                    );
                    if (!isMechanical) {
                        if (visiblePins.length === 2) twoPinRadii.push(cadRadius);
                        else if (visiblePins.length >= 3) multiPinRadii.push(cadRadius);
                    }
                    const pin1KeyUpper = (!isMechanical && totalPinsCount >= 3)
                        ? detectPin1KeyUpper(visiblePins, effectiveKind)
                        : null;

                    const compPadsList = [];
                    for (const { pinKey, pin } of visiblePins) {
                        const pinKeyUpper = String(pinKey).trim().toUpperCase();
                        const rawNet = typeof pin.net === 'string' ? pin.net.trim() : '';
                        const padType = classifyNetKind(rawNet);
                        const pinLabel = formatCompactPinLabel(pinKey, pin.name);
                        const shortNet = padType === 'NC' ? 'NC' : rawNet;

                        const padEntry = {
                            refUpper,
                            pinKey,
                            pinKeyUpper,
                            pinLabel,
                            net: rawNet,
                            netUpper: rawNet.toUpperCase(),
                            shortNet,
                            padType,
                            isPin1: pin1KeyUpper !== null && pinKeyUpper === pin1KeyUpper,
                            x: pin.x,
                            y: pin.y,
                            cadRadius,
                            minPitchCad
                        };
                        pads.push(padEntry);
                        compPadsList.push(padEntry);
                    }
                    padsByRefUpper.set(refUpper, compPadsList);
                }
            }
        }
    }

    // Reference SMD pad size (35th percentile of 2-pin SMD pads)
    const sampleRadii = twoPinRadii.length >= 8 ? twoPinRadii : twoPinRadii.concat(multiPinRadii);
    if (sampleRadii.length > 0) {
        sampleRadii.sort((a, b) => a - b);
        const p35 = sampleRadii[Math.min(sampleRadii.length - 1, Math.floor(sampleRadii.length * 0.35))];
        const stdPadCad = Math.max(1e-4, Math.min(absMaxPadCad, p35));
        const softMaxPadCad = Math.min(absMaxPadCad, stdPadCad * 1.55);

        for (let i = 0; i < pads.length; i++) {
            const pad = pads[i];
            if (pad.cadRadius > stdPadCad) {
                pad.cadRadius = Math.min(softMaxPadCad, stdPadCad + (pad.cadRadius - stdPadCad) * 0.15);
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
        testPoints,
        compByRefUpper,
        padsByRefUpper
    };
    return cache;
}

function resolveEffectiveKind(refUpper, comp) {
    const kind = getComponentKind(comp);
    if (kind !== COMPONENT_KINDS.UNKNOWN) return kind;

    if (refUpper.startsWith('HEATSINK') || refUpper.startsWith('BRACKET') ||
        refUpper.startsWith('SHIELD') || refUpper.startsWith('FRAME') ||
        refUpper.startsWith('STANDOFF') || refUpper.startsWith('STUDFIX') ||
        /^H\d|^MH\d|^HOLE/i.test(refUpper)) {
        return COMPONENT_KINDS.MECHANICAL;
    }
    if (comp.type === 'TH' || refUpper.startsWith('DIMM') || refUpper.startsWith('PCI') ||
        refUpper.startsWith('J') || refUpper.startsWith('CN')) {
        return COMPONENT_KINDS.CONNECTOR;
    }
    if (refUpper.startsWith('PU') || refUpper.startsWith('U') || refUpper.startsWith('UE') ||
        refUpper.startsWith('UV') || refUpper.startsWith('PCH') || refUpper.startsWith('CPU') ||
        refUpper.startsWith('GPU') || refUpper.startsWith('SOC')) {
        return COMPONENT_KINDS.IC;
    }
    return COMPONENT_KINDS.UNKNOWN;
}

function computeComponentPadMetrics(comp, visiblePins, maxPadCad, defaultSinglePadCad, boardDiag) {
    const count = visiblePins.length;
    const eps = Math.max(1e-6, (boardDiag || 1) * 1e-6);
    if (count === 1) {
        const box = componentAabb(comp);
        const fromBox = box ? Math.min(box.width, box.height) * 0.32 : defaultSinglePadCad;
        const cadRadius = Math.min(maxPadCad, Math.max(eps, fromBox));
        return { cadRadius, minPitchCad: cadRadius * 3 };
    }

    const sorted = visiblePins.map(vp => ({ x: vp.pin.x, y: vp.pin.y }));
    sorted.sort((a, b) => a.x - b.x);

    let minDist = Infinity;
    for (let i = 0; i < sorted.length; i++) {
        const a = sorted[i];
        for (let j = i + 1; j < sorted.length; j++) {
            const dx = sorted[j].x - a.x;
            if (dx >= minDist) break;
            const dy = sorted[j].y - a.y;
            const d = Math.hypot(dx, dy);
            if (d > eps && d < minDist) {
                minDist = d;
            }
        }
    }

    if (!Number.isFinite(minDist) || minDist <= eps) {
        const cadRadius = Math.min(maxPadCad, defaultSinglePadCad);
        return { cadRadius, minPitchCad: cadRadius * 3 };
    }

    const factor = count >= 3 ? 0.34 : 0.20;
    const cadRadius = Math.min(maxPadCad, Math.max(eps, minDist * factor));
    return { cadRadius, minPitchCad: minDist };
}

function detectPin1KeyUpper(visiblePins, kind) {
    const keys = visiblePins.map(vp => ({
        keyUpper: String(vp.pinKey).trim().toUpperCase(),
        nameUpper: String(vp.pin.name || '').trim().toUpperCase()
    }));

    const priority = ['1', '01', 'A1', 'P1', '1A'];
    for (const target of priority) {
        const match = keys.find(k => k.keyUpper === target);
        if (match) return match.keyUpper;
    }

    const byName = keys.find(k => k.nameUpper === '1' || k.nameUpper === 'A1');
    if (byName) return byName.keyUpper;

    if (kind === COMPONENT_KINDS.TRANSISTOR && keys.length <= 4) {
        const gate = keys.find(k => k.keyUpper === 'G' || k.nameUpper === 'G' || k.nameUpper === 'GATE');
        if (gate) return gate.keyUpper;
    }

    return null;
}

function formatCompactPinLabel(pinKey, pinName) {
    const key = String(pinKey || '').trim();
    if (key) return key.length > 5 ? key.slice(0, 5) : key;
    const name = String(pinName || '').trim();
    return name.length > 5 ? name.slice(0, 5) : name;
}

function computeCadToCanvasScale(matrix, toX, toY, bounds) {
    const p0 = transformPoint(matrix, bounds.minX, bounds.minY);
    const px = transformPoint(matrix, bounds.maxX, bounds.minY);
    const py = transformPoint(matrix, bounds.minX, bounds.maxY);

    const cadW = Math.abs(bounds.maxX - bounds.minX) || 1;
    const cadH = Math.abs(bounds.maxY - bounds.minY) || 1;

    const canvasW = Math.hypot(toX(px.x) - toX(p0.x), toY(px.y) - toY(p0.y));
    const canvasH = Math.hypot(toX(py.x) - toX(p0.x), toY(py.y) - toY(p0.y));
    return Math.max(1e-6, ((canvasW / cadW) + (canvasH / cadH)) * 0.5);
}

function computePadCanvasRadius(pad, cadToCanvas, scale) {
    const physR = pad.cadRadius * cadToCanvas;
    const pitchCap = pad.minPitchCad * cadToCanvas * 0.44;
    const minLegible = 0.85 / scale;
    return Math.min(Math.max(physR, minLegible), pitchCap);
}

function computeViewportBounds(ctx, scale) {
    const dpr = window.devicePixelRatio || 1;
    const cw = ctx.canvas.width;
    const ch = ctx.canvas.height;
    const panX = CanvasStore.panX;
    const panY = CanvasStore.panY;
    const margin = 50 / scale;
    return {
        left: -panX / scale - margin,
        right: (cw / dpr - panX) / scale + margin,
        top: -panY / scale - margin,
        bottom: (ch / dpr - panY) / scale + margin
    };
}

function resolveHoverAccentColor(kind, style) {
    if (kind === COMPONENT_KINDS.RESISTOR || kind === COMPONENT_KINDS.CAPACITOR ||
        kind === COMPONENT_KINDS.MECHANICAL || kind === COMPONENT_KINDS.UNKNOWN) {
        return '#38bdf8';
    }
    return style.fg || '#38bdf8';
}

function withAlpha(color, alpha) {
    if (!color) return `rgba(56, 189, 248, ${alpha})`;
    if (color.startsWith('#') && color.length === 7) {
        const r = parseInt(color.slice(1, 3), 16);
        const g = parseInt(color.slice(3, 5), 16);
        const b = parseInt(color.slice(5, 7), 16);
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }
    const m = color.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
    if (m) return `rgba(${m[1]}, ${m[2]}, ${m[3]}, ${alpha})`;
    return `rgba(56, 189, 248, ${alpha})`;
}

function normalizeSide(side) {
    return String(side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP';
}

function scaleFill(rgba, mul) {
    if (!rgba || !mul || mul === 1) return rgba;
    const m = rgba.match(/rgba?\((\d+),\s*(\d+),\s*(\d+),\s*([0-9.]+)\)/);
    if (!m) return rgba;
    const a = Math.max(0, Math.min(0.35, Number(m[4]) * mul));
    return `rgba(${m[1]}, ${m[2]}, ${m[3]}, ${a})`;
}

function fillCadRings(ctx, matrix, toX, toY, points, ring, core, radius, lineW, scale, view) {
    if (!points || !points.length) return;
    const r = Math.max(radius, 0.85);
    ctx.strokeStyle = ring;
    ctx.lineWidth = Math.max(lineW, 0.7 / scale);
    ctx.beginPath();
    const visible = [];
    for (const pt of points) {
        const p = transformPoint(matrix, pt.x, pt.y);
        const x = toX(p.x);
        const y = toY(p.y);
        if (view && (x < view.left || x > view.right || y < view.top || y > view.bottom)) continue;
        visible.push(x, y);
        ctx.moveTo(x + r, y);
        ctx.arc(x, y, r, 0, Math.PI * 2);
    }
    ctx.stroke();

    if (visible.length) {
        const coreR = Math.max(r * 0.32, 0.4);
        ctx.fillStyle = core;
        ctx.beginPath();
        for (let i = 0; i < visible.length; i += 2) {
            const x = visible[i], y = visible[i + 1];
            ctx.moveTo(x + coreR, y);
            ctx.arc(x, y, coreR, 0, Math.PI * 2);
        }
        ctx.fill();
    }
}

function strokeCadPolygon(ctx, matrix, toX, toY, corners, fg, fill, width, scale, lite, dashed = false) {
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
    if (dashed) ctx.setLineDash([3.2 / scale, 2.4 / scale]);
    ctx.strokeStyle = fg;
    ctx.lineWidth = width / scale;
    ctx.stroke();
    if (dashed) ctx.setLineDash([]);
}

function strokeCadBox(ctx, matrix, toX, toY, minX, minY, maxX, maxY, fg, width, scale) {
    strokeCadPolyline(ctx, matrix, toX, toY, [
        { x: minX, y: minY },
        { x: maxX, y: minY },
        { x: maxX, y: maxY },
        { x: minX, y: maxY }
    ], true, fg, width, scale);
}

function strokeCadPolyline(ctx, matrix, toX, toY, points, closePath, fg, width, scale) {
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