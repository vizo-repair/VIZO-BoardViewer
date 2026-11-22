// ============================================================================
// FILE: wwwroot/js/engine/CanvasEngine.js (VIZO BoardViewer - 120 FPS Engine)
// ============================================================================
import { CanvasStore } from '../state/CanvasStore.js';
import { AppState, AppEvents } from '../state/AppState.js';
import { GestureController } from './GestureController.js';
import { drawFrame, getImageRenderRect } from './CanvasRenderer.js';
import { findNearestCadComponent } from './SnapEngine.js';
import { t } from '../ui/i18n/I18nStore.js';
import { formatComponentRole } from '../ui/i18n/DisplayLabels.js';
import {
    COMPONENT_KINDS,
    normalizeComponentKind,
    getComponentKindStyle,
    classifyNetKind
} from './ComponentKind.js';

const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

export class CanvasEngine {
    constructor(canvasEl, containerEl) {
        this.canvas = canvasEl;
        this.container = containerEl;
        this.ctx = canvasEl.getContext('2d', { alpha: true });
        this.pointerDownPos = { x: 0, y: 0, clientX: 0, clientY: 0, time: 0 };
        this.rafId = null;

        // Cursor hover state
        this.hoverHit = null;
        this.hoverScreenPos = { x: 0, y: 0 };
        this.lastCanvasPos = null;
        this.afterFrameRender = null;

        this.cachedCanvasRect = null;

        this.gestures = new GestureController(containerEl, () => this.requestRender(), this);
        this.bindEvents();
        this.resize();
        window.addEventListener('resize', () => this.resize());
    }

    requestRender() {
        if (this.rafId) return;
        this.rafId = requestAnimationFrame(() => {
            this.rafId = null;
            this.renderImmediate();
        });
    }

    bindEvents() {
        const getPos = (e) => {
            const r = this.cachedCanvasRect || this.canvas.getBoundingClientRect();
            const cx = (e.clientX - r.left - CanvasStore.panX) / CanvasStore.scale;
            const cy = (e.clientY - r.top - CanvasStore.panY) / CanvasStore.scale;
            const imgRect = getImageRenderRect(this.container);

            const rawNormX = imgRect.renderWidth ? (cx - imgRect.offsetX) / imgRect.renderWidth : 0.5;
            const rawNormY = imgRect.renderHeight ? (cy - imgRect.offsetY) / imgRect.renderHeight : 0.5;

            return {
                x: Math.max(0.0, Math.min(1.0, rawNormX)),
                y: Math.max(0.0, Math.min(1.0, rawNormY)),
                rawX: rawNormX,
                rawY: rawNormY,
                canvasX: cx,
                canvasY: cy
            };
        };

        AppEvents.on('activeSide:changed', () => {
            this.hoverHit = null;
        });

        AppEvents.on('state:reset', () => {
            this.hoverHit = null;
        });

        this.canvas.addEventListener('pointerleave', () => {
            if (this.hoverHit) {
                this.hoverHit = null;
                this.requestRender();
            }
        });

        this.canvas.addEventListener('pointerdown', (e) => {
            try { this.canvas.setPointerCapture(e.pointerId); } catch (_) { }

            const pos = getPos(e);
            this.lastCanvasPos = pos;

            this.pointerDownPos = {
                x: pos.canvasX,
                y: pos.canvasY,
                clientX: e.clientX,
                clientY: e.clientY,
                time: Date.now()
            };
        });

        this.canvas.addEventListener('pointermove', (e) => {
            const pos = getPos(e);
            this.lastCanvasPos = pos;
            this.container.style.cursor = 'grab';

            // Live Hover HUD (works only in open board space)
            if (!CanvasStore.isInteracting && AppState.boardviewNetlist) {
                const imgRect = getImageRenderRect(this.container);
                const hit = findNearestCadComponent(pos.rawX, pos.rawY, 28, imgRect, CanvasStore.scale, { allowBody: true });

                const r = this.cachedCanvasRect || this.canvas.getBoundingClientRect();
                this.hoverScreenPos = { x: e.clientX - r.left, y: e.clientY - r.top };

                if (hit !== this.hoverHit) {
                    this.hoverHit = hit;
                    this.requestRender();
                } else if (this.hoverHit) {
                    this.requestRender();
                }
            }
        });

        const onPointerFinish = (e) => {
            try { this.canvas.releasePointerCapture(e.pointerId); } catch (_) { }
        };

        this.canvas.addEventListener('pointerup', onPointerFinish);
        this.canvas.addEventListener('pointercancel', onPointerFinish);
    }

    resize() {
        const dpr = window.devicePixelRatio || 1;
        const w = this.container.clientWidth || window.innerWidth;
        const h = this.container.clientHeight || window.innerHeight;
        this.canvas.width = Math.round(w * dpr);
        this.canvas.height = Math.round(h * dpr);
        this.canvas.style.width = w + 'px';
        this.canvas.style.height = h + 'px';

        this.cachedCanvasRect = this.canvas.getBoundingClientRect();
        this.requestRender();
    }

    render() {
        this.requestRender();
    }

    renderImmediate() {
        const activeHoverHit = (this.hoverHit && !CanvasStore.isInteracting) ? this.hoverHit : null;

        drawFrame(
            this.ctx,
            this.canvas,
            this.container,
            () => this.requestRender(),
            { hoverHit: activeHoverHit }
        );

        if (typeof this.afterFrameRender === 'function') {
            this.afterFrameRender();
        }

        if (activeHoverHit) {
            this.renderHoverTooltip();
        }
    }

    renderHoverTooltip() {
        const hit = this.hoverHit;
        if (!hit || !hit.refDes) return;

        const dpr = window.devicePixelRatio || 1;
        const ctx = this.ctx;
        const sx = this.hoverScreenPos.x;
        const sy = this.hoverScreenPos.y;

        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);

        // Target dot on the snapped board pin
        if (hit.isPinSnapped && Number.isFinite(hit.snappedX) && Number.isFinite(hit.snappedY)) {
            const imgRect = getImageRenderRect(this.container);
            const canvasX = imgRect.offsetX + (hit.snappedX * imgRect.renderWidth);
            const canvasY = imgRect.offsetY + (hit.snappedY * imgRect.renderHeight);
            const pinScreenX = Math.round((canvasX * CanvasStore.scale + CanvasStore.panX) * dpr);
            const pinScreenY = Math.round((canvasY * CanvasStore.scale + CanvasStore.panY) * dpr);

            const ringRadius = 9.0 * dpr;

            ctx.shadowBlur = 0;
            ctx.fillStyle = '#07090e';
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, ringRadius + 1.8 * dpr, 0, Math.PI * 2);
            ctx.fill();

            ctx.strokeStyle = '#facc15';
            ctx.lineWidth = 2.2 * dpr;
            ctx.shadowColor = '#facc15';
            ctx.shadowBlur = 12 * dpr;
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, ringRadius, 0, Math.PI * 2);
            ctx.stroke();

            ctx.fillStyle = '#facc15';
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, 4.8 * dpr, 0, Math.PI * 2);
            ctx.fill();

            ctx.shadowBlur = 0;
            ctx.fillStyle = '#ffffff';
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, 2.0 * dpr, 0, Math.PI * 2);
            ctx.fill();
        }

        const effectiveRef = hit.displayRefDes || hit.refDes;
        const kind = normalizeComponentKind(hit.type);
        const kindStyle = getComponentKindStyle(kind);
        const kindAccent = (kind !== COMPONENT_KINDS.UNKNOWN && kind !== COMPONENT_KINDS.MECHANICAL)
            ? kindStyle.fg
            : '#38bdf8';
        const roleText = (hit.type && kind !== COMPONENT_KINDS.UNKNOWN)
            ? formatComponentRole(hit.type)
            : (hit.isTestPoint ? formatComponentRole(hit.type) : '');
        const bomText = formatBomSummary(hit, effectiveRef);

        if (hit.isPinSnapped && hit.snappedPin) {
            const pinHeader = `${effectiveRef}.${hit.snappedPin}`;
            const roleSegment = roleText ? ` (${roleText})` : '';
            const netKind = classifyNetKind(hit.net);
            const netBadgeText = netKind === 'NC'
                ? t('pinTypeNc', 'NC')
                : hit.net.trim();

            const otherPins = (hit.allPins || [])
                .filter(p => String(p.pin) !== String(hit.snappedPin))
                .slice(0, 4);

            const fontSizeTitle = 11.5 * dpr;
            const fontSizeRole = 10.2 * dpr;
            const fontSizeBadge = 9.8 * dpr;
            const fontSizeBody = 10.3 * dpr;
            const padX = 11 * dpr;
            const padY = 8 * dpr;
            const lineHeight = 15.5 * dpr;
            const dotGap = 11 * dpr;

            ctx.font = `bold ${fontSizeTitle}px ${FONT_FAMILY}`;
            const headerW = ctx.measureText(pinHeader).width;
            ctx.font = `${fontSizeRole}px ${FONT_FAMILY}`;
            const roleW = roleSegment ? ctx.measureText(roleSegment).width : 0;
            const badgeMetrics = measureNetBadge(ctx, netBadgeText, fontSizeBadge, dpr);

            let maxW = dotGap + headerW + roleW + (8 * dpr) + badgeMetrics.w;

            if (bomText) {
                ctx.font = `600 ${fontSizeBody}px ${FONT_FAMILY}`;
                maxW = Math.max(maxW, ctx.measureText(bomText).width);
            }

            ctx.font = `bold ${fontSizeBody}px ${FONT_FAMILY}`;
            for (const op of otherPins) {
                const opKind = classifyNetKind(op.net);
                const opNetText = opKind === 'NC' ? t('pinTypeNc', 'NC') : op.net;
                maxW = Math.max(maxW, ctx.measureText(`${op.pin}: ${opNetText}`).width + 14 * dpr);
            }

            const tagW = Math.ceil(maxW) + padX * 2;
            const totalLines = 1 + (bomText ? 1 : 0) + (otherPins.length > 0 ? (otherPins.length + 0.5) : 0);
            const tagH = Math.ceil(totalLines * lineHeight) + padY * 2;

            const posX = Math.min((this.canvas.width - tagW - 16), Math.max(16, sx * dpr + 20 * dpr));
            const posY = Math.max(16, Math.min(this.canvas.height - tagH - 16, sy * dpr - (tagH / 2)));

            ctx.fillStyle = 'rgba(7, 12, 24, 0.95)';
            ctx.strokeStyle = '#22d3ee';
            ctx.lineWidth = 1.3 * dpr;
            ctx.shadowColor = 'rgba(6, 182, 212, 0.35)';
            ctx.shadowBlur = 12 * dpr;

            ctx.beginPath();
            if (ctx.roundRect) ctx.roundRect(posX, posY, tagW, tagH, 7 * dpr);
            else ctx.rect(posX, posY, tagW, tagH);
            ctx.fill();
            ctx.stroke();

            ctx.shadowBlur = 0;
            let curY = posY + padY + (fontSizeTitle * 0.62);

            // Component class indicator dot
            ctx.fillStyle = kindAccent;
            ctx.beginPath();
            ctx.arc(posX + padX + 3.5 * dpr, curY, 3.5 * dpr, 0, Math.PI * 2);
            ctx.fill();

            ctx.textAlign = 'left';
            ctx.textBaseline = 'middle';
            let curX = posX + padX + dotGap;

            ctx.font = `bold ${fontSizeTitle}px ${FONT_FAMILY}`;
            ctx.fillStyle = '#f8fafc';
            ctx.fillText(pinHeader, curX, curY);
            curX += headerW;

            if (roleSegment) {
                ctx.font = `${fontSizeRole}px ${FONT_FAMILY}`;
                ctx.fillStyle = '#94a3b8';
                ctx.fillText(roleSegment, curX, curY);
                curX += roleW;
            }

            drawNetBadge(ctx, curX + 8 * dpr, curY, netBadgeText, netKind, fontSizeBadge, dpr);

            if (bomText) {
                curY += lineHeight;
                ctx.font = `600 ${fontSizeBody}px ${FONT_FAMILY}`;
                ctx.fillStyle = '#cbd5e1';
                ctx.fillText(bomText, posX + padX, curY);
            }

            if (otherPins.length > 0) {
                curY += lineHeight * 0.55;
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.1)';
                ctx.lineWidth = 1 * dpr;
                ctx.beginPath();
                ctx.moveTo(posX + padX, curY);
                ctx.lineTo(posX + tagW - padX, curY);
                ctx.stroke();

                curY += lineHeight * 0.72;
                ctx.font = `${fontSizeBody}px ${FONT_FAMILY}`;

                for (const op of otherPins) {
                    const opPinLabel = `${op.pin}:`;
                    ctx.fillStyle = '#64748b';
                    ctx.fillText(opPinLabel, posX + padX, curY);

                    const opKind = classifyNetKind(op.net);
                    const displayNet = opKind === 'NC' ? t('pinTypeNc', 'NC') : op.net;
                    ctx.fillStyle = getNetTextColor(opKind);
                    const pinColOffset = Math.max(20 * dpr, ctx.measureText(opPinLabel).width + 6 * dpr);
                    ctx.fillText(displayNet, posX + padX + pinColOffset, curY);
                    curY += lineHeight;
                }
            }

            ctx.restore();
            return;
        }

        // Hover card for component body or testpoint / VIA
        const roleSegment = roleText ? ` (${roleText})` : '';
        const hasNet = hit.isTestPoint || (hit.net && hit.net.trim().length > 0);
        const netKind = hasNet ? classifyNetKind(hit.net) : null;
        const netBadgeText = hasNet
            ? (netKind === 'NC' ? t('pinTypeNc', 'NC') : hit.net.trim())
            : '';

        const fontSizeTitle = 11.5 * dpr;
        const fontSizeSub = 10.2 * dpr;
        const fontSizeBadge = 9.8 * dpr;
        const padX = 11 * dpr;
        const padY = 6.5 * dpr;
        const dotGap = 11 * dpr;

        ctx.font = `bold ${fontSizeTitle}px ${FONT_FAMILY}`;
        const refW = ctx.measureText(effectiveRef).width;
        ctx.font = `${fontSizeSub}px ${FONT_FAMILY}`;
        const roleW = roleSegment ? ctx.measureText(roleSegment).width : 0;

        const inlineBom = bomText && bomText.length <= 28 ? ` • ${bomText}` : '';
        const secondLineBom = bomText && bomText.length > 28 ? bomText : '';
        const inlineBomW = inlineBom ? ctx.measureText(inlineBom).width : 0;
        const badgeMetrics = hasNet ? measureNetBadge(ctx, netBadgeText, fontSizeBadge, dpr) : { w: 0, h: 0 };

        let row1W = dotGap + refW + roleW + inlineBomW + (hasNet ? (8 * dpr + badgeMetrics.w) : 0);
        let row2W = secondLineBom ? ctx.measureText(secondLineBom).width : 0;

        const lineHeight = 15 * dpr;
        const tagW = Math.ceil(Math.max(row1W, row2W)) + padX * 2;
        const tagH = Math.ceil((secondLineBom ? lineHeight * 2 : lineHeight) + padY * 2);

        const posX = Math.min((this.canvas.width - tagW - 14), Math.max(14, sx * dpr + 18 * dpr));
        const posY = Math.max(14, sy * dpr - tagH - 12 * dpr);

        ctx.fillStyle = 'rgba(7, 12, 24, 0.95)';
        ctx.strokeStyle = kindAccent;
        ctx.lineWidth = 1.2 * dpr;
        ctx.shadowColor = 'rgba(0, 0, 0, 0.65)';
        ctx.shadowBlur = 10 * dpr;

        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(posX, posY, tagW, tagH, 6 * dpr);
        else ctx.rect(posX, posY, tagW, tagH);
        ctx.fill();
        ctx.stroke();

        ctx.shadowBlur = 0;
        const row1Y = posY + padY + lineHeight * 0.5;

        ctx.fillStyle = kindAccent;
        ctx.beginPath();
        ctx.arc(posX + padX + 3.5 * dpr, row1Y, 3.5 * dpr, 0, Math.PI * 2);
        ctx.fill();

        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        let curX = posX + padX + dotGap;

        ctx.font = `bold ${fontSizeTitle}px ${FONT_FAMILY}`;
        ctx.fillStyle = '#ffffff';
        ctx.fillText(effectiveRef, curX, row1Y);
        curX += refW;

        if (roleSegment) {
            ctx.font = `${fontSizeSub}px ${FONT_FAMILY}`;
            ctx.fillStyle = '#94a3b8';
            ctx.fillText(roleSegment, curX, row1Y);
            curX += roleW;
        }

        if (inlineBom) {
            ctx.font = `600 ${fontSizeSub}px ${FONT_FAMILY}`;
            ctx.fillStyle = '#cbd5e1';
            ctx.fillText(inlineBom, curX, row1Y);
            curX += inlineBomW;
        }

        if (hasNet) {
            drawNetBadge(ctx, curX + 8 * dpr, row1Y, netBadgeText, netKind, fontSizeBadge, dpr);
        }

        if (secondLineBom) {
            const row2Y = row1Y + lineHeight;
            ctx.font = `600 ${fontSizeSub}px ${FONT_FAMILY}`;
            ctx.fillStyle = '#cbd5e1';
            ctx.fillText(secondLineBom, posX + padX, row2Y);
        }

        ctx.restore();
    }
}

function formatBomSummary(hit, effectiveRef) {
    if (!hit) return null;
    const refUpper = (effectiveRef || '').trim().toUpperCase();
    const parts = [];

    const val = (hit.value || '').trim();
    const pkg = (hit.package || '').trim();
    const pn = (hit.partNumber || '').trim();
    const desc = (hit.description || '').trim();

    if (val && val.toUpperCase() !== refUpper) {
        parts.push(val);
    }
    if (pkg && pkg.toUpperCase() !== refUpper &&
        !parts.some(p => p.toUpperCase().includes(pkg.toUpperCase()))) {
        parts.push(pkg);
    }
    if (parts.length === 0 && pn && pn.toUpperCase() !== refUpper) {
        parts.push(pn);
    }
    if (parts.length === 0 && desc && desc.toUpperCase() !== refUpper && !hit.isTestPoint) {
        parts.push(desc);
    }

    if (parts.length === 0) return null;
    const joined = parts.join(' ');
    return joined.length > 68 ? joined.slice(0, 65) + '...' : joined;
}

function getNetBadgePalette(netKind) {
    if (netKind === 'GND') {
        return { bg: 'rgba(56, 114, 168, 0.35)', border: '#60a5fa', text: '#bae6fd' };
    }
    if (netKind === 'NC') {
        return { bg: 'rgba(51, 65, 85, 0.55)', border: '#64748b', text: '#94a3b8' };
    }
    if (netKind === 'POWER') {
        return { bg: 'rgba(249, 115, 22, 0.24)', border: '#fb923c', text: '#fed7aa' };
    }
    return { bg: 'rgba(6, 182, 212, 0.22)', border: '#22d3ee', text: '#a5f3fc' };
}

function getNetTextColor(netKind) {
    if (netKind === 'GND') return '#7dd3fc';
    if (netKind === 'NC') return '#64748b';
    if (netKind === 'POWER') return '#fdba74';
    return '#e2e8f0';
}

function measureNetBadge(ctx, text, fontSize, dpr) {
    ctx.font = `bold ${fontSize}px ${FONT_FAMILY}`;
    const tw = ctx.measureText(text).width;
    return {
        w: Math.ceil(tw + 10 * dpr),
        h: Math.ceil(fontSize + 5 * dpr)
    };
}

function drawNetBadge(ctx, x, centerY, text, netKind, fontSize, dpr) {
    const pal = getNetBadgePalette(netKind);
    const { w, h } = measureNetBadge(ctx, text, fontSize, dpr);
    const y = centerY - h / 2;

    ctx.fillStyle = pal.bg;
    ctx.strokeStyle = pal.border;
    ctx.lineWidth = 1.0 * dpr;
    ctx.beginPath();
    if (ctx.roundRect) ctx.roundRect(x, y, w, h, 4 * dpr);
    else ctx.rect(x, y, w, h);
    ctx.fill();
    ctx.stroke();

    ctx.font = `bold ${fontSize}px ${FONT_FAMILY}`;
    ctx.fillStyle = pal.text;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, x + w / 2, centerY);
    ctx.textAlign = 'left';
}