// ============================================================================
// FILE: wwwroot/js/engine/CanvasEngine.js (VIZO 2.0 - 120 FPS Unified Scheduler)
// ============================================================================
import { CanvasStore } from '../state/CanvasStore.js';
import { AppState, AppEvents } from '../state/AppState.js';
import { GestureController } from './GestureController.js';
import { drawFrame, getImageRenderRect } from './CanvasRenderer.js';
import { findNearestCadComponent } from './SnapEngine.js';
import { t } from '../ui/i18n/I18nStore.js';

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

            const r = this.cachedCanvasRect || this.canvas.getBoundingClientRect();
            const screenX = e.clientX - r.left;
            const screenY = e.clientY - r.top;
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
        drawFrame(
            this.ctx,
            this.canvas,
            this.container,
            () => this.requestRender()
        );

        if (this.hoverHit && !CanvasStore.isInteracting) {
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

        // Draw crisp yellow target dot directly on the snapped board pin
        if (hit.isPinSnapped && Number.isFinite(hit.snappedX) && Number.isFinite(hit.snappedY)) {
            const imgRect = getImageRenderRect(this.container);
            const canvasX = imgRect.offsetX + (hit.snappedX * imgRect.renderWidth);
            const canvasY = imgRect.offsetY + (hit.snappedY * imgRect.renderHeight);
            const pinScreenX = Math.round((canvasX * CanvasStore.scale + CanvasStore.panX) * dpr);
            const pinScreenY = Math.round((canvasY * CanvasStore.scale + CanvasStore.panY) * dpr);

            const ringRadius = 10.0 * dpr;

            // 1. Dark backing (so the dot is clearly visible on any soldermask color)
            ctx.shadowBlur = 0;
            ctx.fillStyle = '#07090e';
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, ringRadius + 2.0 * dpr, 0, Math.PI * 2);
            ctx.fill();

            // 2. Glowing yellow outer ring
            ctx.strokeStyle = '#facc15';
            ctx.lineWidth = 2.4 * dpr;
            ctx.shadowColor = '#facc15';
            ctx.shadowBlur = 14 * dpr;
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, ringRadius, 0, Math.PI * 2);
            ctx.stroke();

            // 3. Yellow inner core
            ctx.fillStyle = '#facc15';
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, 5.5 * dpr, 0, Math.PI * 2);
            ctx.fill();

            // 4. White center dot (focus)
            ctx.shadowBlur = 0;
            ctx.fillStyle = '#ffffff';
            ctx.beginPath();
            ctx.arc(pinScreenX, pinScreenY, 2.2 * dpr, 0, Math.PI * 2);
            ctx.fill();
        }

        // --- Tooltip label rendering ---
        const effectiveRef = hit.displayRefDes || hit.refDes;
        const infoText = hit.value || hit.description || hit.partNumber || null;

        if (hit.isPinSnapped && hit.snappedPin) {
            const pinHeader = `${effectiveRef}.${hit.snappedPin}`;
            const netHeader = hit.net && !hit.net.toUpperCase().startsWith('NC') ? hit.net : t('netUnconnected', 'UNCONNECTED');

            let descLine = null;
            if (infoText && !infoText.equalsIgnoreCase(effectiveRef)) {
                descLine = infoText.length > 75 ? infoText.substring(0, 72) + '...' : infoText;
            }

            const otherPins = (hit.allPins || [])
                .filter(p => String(p.pin) !== String(hit.snappedPin))
                .slice(0, 4);

            const fontSizeTitle = 11.5 * dpr;
            const fontSizeBody = 10.5 * dpr;
            const padX = 12 * dpr;
            const padY = 8 * dpr;
            const lineHeight = 15 * dpr;

            ctx.font = `bold ${fontSizeTitle}px ${FONT_FAMILY}`;
            let maxW = ctx.measureText(`🎯 ${pinHeader} • ${netHeader}`).width;

            if (descLine) {
                ctx.font = `${fontSizeBody}px ${FONT_FAMILY}`;
                maxW = Math.max(maxW, ctx.measureText(`ℹ️ ${descLine}`).width);
            }

            ctx.font = `bold ${fontSizeBody}px ${FONT_FAMILY}`;
            for (const op of otherPins) {
                maxW = Math.max(maxW, ctx.measureText(`${op.pin}: ${op.net}`).width + 10 * dpr);
            }

            const tagW = Math.ceil(maxW) + padX * 2;
            let totalLines = 1 + (descLine ? 1 : 0) + (otherPins.length > 0 ? (otherPins.length + 0.5) : 0);
            const tagH = Math.ceil(totalLines * lineHeight) + padY * 2;

            const posX = Math.min((this.canvas.width - tagW - 16), Math.max(16, sx * dpr + 20 * dpr));
            const posY = Math.max(16, Math.min(this.canvas.height - tagH - 16, sy * dpr - (tagH / 2)));

            ctx.fillStyle = 'rgba(7, 12, 24, 0.94)';
            ctx.strokeStyle = '#22d3ee';
            ctx.lineWidth = 1.4 * dpr;
            ctx.shadowColor = 'rgba(6, 182, 212, 0.35)';
            ctx.shadowBlur = 12 * dpr;

            ctx.beginPath();
            if (ctx.roundRect) ctx.roundRect(posX, posY, tagW, tagH, 7 * dpr);
            else ctx.rect(posX, posY, tagW, tagH);
            ctx.fill();
            ctx.stroke();

            ctx.shadowBlur = 0;
            let curY = posY + padY + (fontSizeTitle * 0.85);

            ctx.textAlign = 'left';
            ctx.font = `bold ${fontSizeTitle}px ${FONT_FAMILY}`;
            ctx.fillStyle = '#38bdf8';
            ctx.fillText(`🎯 ${pinHeader}`, posX + padX, curY);

            const pinHeadW = ctx.measureText(`🎯 ${pinHeader} `).width;
            ctx.fillStyle = '#ffffff';
            ctx.fillText(`• ${netHeader}`, posX + padX + pinHeadW, curY);

            if (descLine) {
                curY += lineHeight;
                ctx.font = `${fontSizeBody}px ${FONT_FAMILY}`;
                ctx.fillStyle = '#94a3b8';
                ctx.fillText(`ℹ️ ${descLine}`, posX + padX, curY);
            }

            if (otherPins.length > 0) {
                curY += lineHeight * 0.4;
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.1)';
                ctx.lineWidth = 1 * dpr;
                ctx.beginPath();
                ctx.moveTo(posX + padX, curY);
                ctx.lineTo(posX + tagW - padX, curY);
                ctx.stroke();

                curY += lineHeight * 0.7;
                ctx.font = `${fontSizeBody}px ${FONT_FAMILY}`;

                for (const op of otherPins) {
                    ctx.fillStyle = '#64748b';
                    ctx.fillText(`${op.pin}:`, posX + padX, curY);

                    const isGnd = op.net.toUpperCase().includes('GND');
                    ctx.fillStyle = isGnd ? '#94a3b8' : '#e2e8f0';
                    ctx.fillText(op.net, posX + padX + (18 * dpr), curY);
                    curY += lineHeight;
                }
            }

            ctx.restore();
            return;
        }

        let label = `🎯 ${effectiveRef}`;
        if (infoText) {
            label += ` • ${infoText}`;
        } else if (hit.type && hit.type !== 'IC') {
            label += ` (${hit.type})`;
        }

        if (label.length > 60) label = label.substring(0, 57) + '...';

        const fontSize = 11.5 * dpr;
        ctx.font = `bold ${fontSize}px ${FONT_FAMILY}`;
        const tw = ctx.measureText(label).width;
        const padX = 10 * dpr;
        const padY = 5 * dpr;
        const tagW = Math.ceil(tw) + padX * 2;
        const tagH = fontSize + padY * 2;

        const posX = Math.min((this.canvas.width - tagW - 14), Math.max(14, sx * dpr + 18 * dpr));
        const posY = Math.max(14, sy * dpr - tagH - 12 * dpr);

        ctx.fillStyle = 'rgba(7, 12, 24, 0.94)';
        ctx.strokeStyle = '#38bdf8';
        ctx.lineWidth = 1.2 * dpr;
        ctx.shadowColor = 'rgba(0, 0, 0, 0.6)';
        ctx.shadowBlur = 10 * dpr;

        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(posX, posY, tagW, tagH, 5 * dpr);
        else ctx.rect(posX, posY, tagW, tagH);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = '#ffffff';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.shadowBlur = 0;
        ctx.fillText(label, posX + tagW / 2, posY + tagH / 2);

        ctx.restore();
    }
}