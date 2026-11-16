// ============================================================================
// FILE: wwwroot/js/engine/FocusHighlightRenderer.js
// Renders neon reticle, pulse animation, and opposite-side X-ray outline
// ============================================================================
import { CanvasStore } from '../state/CanvasStore.js';
import { AppState } from '../state/AppState.js';
import { formatBoardSide } from '../ui/i18n/DisplayLabels.js';
import { getComponentPhysicalBounds } from './BoardCorners.js';

let focusBadges = [];
const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

export function getFocusHighlightBadges() {
    return focusBadges;
}

export function renderFocusHighlight(ctx, scale, toX, toY, requestRender) {
    focusBadges = [];
    const focus = CanvasStore.focusedHighlight;
    if (!focus) return;

    const now = Date.now();
    if (now > focus.expiresAt) {
        CanvasStore.clearFocusedHighlight();
        return;
    }

    const currentSide = (AppState.activeSide || "TOP").toUpperCase();
    const isOpposite = (focus.side !== currentSide);
    const matrix = AppState.getActiveMatrix();

    const timeLeft = focus.expiresAt - now;
    const alpha = timeLeft < 1000 ? (timeLeft / 1000) : 1.0;
    const pulse = Math.sin((now % 1000) / 1000 * Math.PI * 2);

    const px = toX(focus.normX);
    const py = toY(focus.normY);

    ctx.save();
    ctx.globalAlpha = alpha;

    // -------------------------------------------------------------------------
    // SCENARIO 1: PART ON CURRENT SIDE (GOLD / AMBER RETICLE)
    // -------------------------------------------------------------------------
    if (!isOpposite) {
        const isVisionOnly = focus.modeLabel === "👁️";
        const themeColor = isVisionOnly ? '#f59e0b' : '#facc15';
        const ringR = (16 + pulse * 3) / scale;

        // Outer pulsing ring
        ctx.strokeStyle = themeColor;
        ctx.lineWidth = 2.0 / scale;
        ctx.shadowColor = themeColor;
        ctx.shadowBlur = 12 / scale;
        ctx.beginPath();
        ctx.arc(px, py, ringR, 0, Math.PI * 2);
        ctx.stroke();

        // Inner reticle
        const armLen = 24 / scale;
        const gap = 4 / scale;
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 1.2 / scale;
        ctx.shadowBlur = 0;

        ctx.beginPath();
        ctx.moveTo(px, py - gap); ctx.lineTo(px, py - armLen);
        ctx.moveTo(px, py + gap); ctx.lineTo(px, py + armLen);
        ctx.moveTo(px - gap, py); ctx.lineTo(px - armLen, py);
        ctx.moveTo(px + gap, py); ctx.lineTo(px + armLen, py);
        ctx.stroke();

        // Package highlight (CAD or visual bounding box)
        if (focus.comp && matrix) {
            const physBounds = getComponentPhysicalBounds(focus.comp, matrix, toX, toY);
            if (physBounds && physBounds.w > 0 && physBounds.h > 0) {
                const bx = physBounds.minX;
                const by = physBounds.minY;
                const bw = physBounds.w;
                const bh = physBounds.h;

                ctx.strokeStyle = 'rgba(250, 204, 21, 0.9)';
                ctx.lineWidth = 1.5 / scale;
                ctx.fillStyle = 'rgba(250, 204, 21, 0.15)';
                ctx.beginPath();
                if (ctx.roundRect) ctx.roundRect(bx, by, bw, bh, 3 / scale);
                else ctx.rect(bx, by, bw, bh);
                ctx.fill();
                ctx.stroke();
            }
        } else if (focus.visualBox) {
            const vb = focus.visualBox;
            const bx = toX(vb.left);
            const by = toY(vb.top);
            const bw = toX(vb.left + vb.width) - bx;
            const bh = toY(vb.top + vb.height) - by;

            ctx.strokeStyle = 'rgba(245, 158, 11, 0.9)';
            ctx.lineWidth = 1.5 / scale;
            ctx.fillStyle = 'rgba(245, 158, 11, 0.15)';
            ctx.beginPath();
            if (ctx.roundRect) ctx.roundRect(bx, by, bw, bh, 4 / scale);
            else ctx.rect(bx, by, bw, bh);
            ctx.fill();
            ctx.stroke();
        }

        // Name badge
        const label = `${focus.modeLabel} ${focus.refDes}`;
        ctx.font = `bold ${11 / scale}px ${FONT_FAMILY}`;
        const tw = ctx.measureText(label).width;
        const tagW = tw + (12 / scale);
        const tagH = 18 / scale;
        const tagX = px - (tagW / 2);
        const tagY = py - ringR - tagH - (4 / scale);

        ctx.fillStyle = 'rgba(15, 23, 42, 0.95)';
        ctx.strokeStyle = themeColor;
        ctx.lineWidth = 1.2 / scale;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(tagX, tagY, tagW, tagH, 4 / scale);
        else ctx.rect(tagX, tagY, tagW, tagH);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = '#ffffff';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(label, px, tagY + tagH / 2);
    }
    // -------------------------------------------------------------------------
    // SCENARIO 2: PART ON OPPOSITE SIDE (DASHED X-RAY + PORTAL)
    // -------------------------------------------------------------------------
    else {
        const themeColor = '#c084fc';
        const ringR = (18 + pulse * 2) / scale;

        ctx.strokeStyle = themeColor;
        ctx.lineWidth = 1.8 / scale;
        ctx.setLineDash([4 / scale, 3 / scale]);
        ctx.shadowColor = themeColor;
        ctx.shadowBlur = 10 / scale;
        ctx.beginPath();
        ctx.arc(px, py, ringR, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);

        if (focus.comp && matrix) {
            const physBounds = getComponentPhysicalBounds(focus.comp, matrix, toX, toY);
            if (physBounds && physBounds.w > 0 && physBounds.h > 0) {
                const bx = physBounds.minX;
                const by = physBounds.minY;
                const bw = physBounds.w;
                const bh = physBounds.h;

                ctx.strokeStyle = 'rgba(192, 132, 252, 0.7)';
                ctx.lineWidth = 1.2 / scale;
                ctx.fillStyle = 'rgba(192, 132, 252, 0.12)';
                ctx.setLineDash([3 / scale, 3 / scale]);
                ctx.beginPath();
                ctx.rect(bx, by, bw, bh);
                ctx.fill();
                ctx.stroke();
                ctx.setLineDash([]);
            }
        }

        const label = `🔄 ${focus.refDes} (${formatBoardSide(focus.side)})`;
        ctx.font = `bold ${10.5 / scale}px ${FONT_FAMILY}`;
        const tw = ctx.measureText(label).width;
        const tagW = tw + (14 / scale);
        const tagH = 20 / scale;
        const tagX = px - (tagW / 2);
        const tagY = py - ringR - tagH - (4 / scale);

        ctx.fillStyle = 'rgba(30, 10, 45, 0.95)';
        ctx.strokeStyle = '#c084fc';
        ctx.lineWidth = 1.4 / scale;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(tagX, tagY, tagW, tagH, 5 / scale);
        else ctx.rect(tagX, tagY, tagW, tagH);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = '#f5d0fe';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(label, px, tagY + tagH / 2);

        focusBadges.push({
            x: tagX,
            y: tagY,
            w: tagW,
            h: tagH,
            targetSide: focus.side,
            refDes: focus.refDes
        });
    }

    ctx.restore();

    if (requestRender) {
        requestAnimationFrame(() => requestRender());
    }
}