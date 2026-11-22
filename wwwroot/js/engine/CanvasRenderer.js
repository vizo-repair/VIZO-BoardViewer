// ============================================================================
// FILE: wwwroot/js/engine/CanvasRenderer.js (VIZO 2.0 - Unified Canvas Layer)
// ============================================================================
import { CanvasStore } from '../state/CanvasStore.js';
import { renderWireframeOverlay } from './WireframeOverlayRenderer.js';
import { renderFocusHighlight } from './FocusHighlightRenderer.js';
import { AppState } from '../state/AppState.js';
import { getEffectiveMatrix, invalidateDefaultMatrixCache, resolveBoardBounds } from './BoardCorners.js';


const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
let cachedRect = null;

export function invalidateRenderRect() {
    cachedRect = null;
    invalidateDefaultMatrixCache();
}

window.addEventListener('resize', invalidateRenderRect);

export function getImageRenderRect(container) {
    if (cachedRect) return cachedRect;

    const cw = container ? (container.clientWidth || window.innerWidth) : window.innerWidth;
    const ch = container ? (container.clientHeight || window.innerHeight) : window.innerHeight;

    let iw = 0;
    let ih = 0;

    // Use the physical aspect ratio of the Boardview file
    if (AppState.boardviewNetlist) {
        const bounds = resolveBoardBounds(AppState.boardviewNetlist);
        if (bounds) {
            const bw = bounds.width || (bounds.maxX - bounds.minX);
            const bh = bounds.height || (bounds.maxY - bounds.minY);
            if (bw > 0 && bh > 0) {
                iw = bw;
                ih = bh;
            }
        }
    }

    if (iw === 0 || ih === 0) {
        return { cw, ch, iw: cw, ih: ch, renderWidth: cw, renderHeight: ch, offsetX: 0, offsetY: 0 };
    }

    const imgRatio = iw / ih;
    const containerRatio = cw / ch;

    let renderWidth, renderHeight, offsetX, offsetY;

    // 6% margin on edges so the board does not touch screen borders
    const fitFactor = 0.88;

    if (containerRatio > imgRatio) {
        renderHeight = ch * fitFactor;
        renderWidth = renderHeight * imgRatio;
        offsetX = (cw - renderWidth) / 2;
        offsetY = (ch - renderHeight) / 2;
    } else {
        renderWidth = cw * fitFactor;
        renderHeight = renderWidth / imgRatio;
        offsetX = (cw - renderWidth) / 2;
        offsetY = (ch - renderHeight) / 2;
    }

    cachedRect = { cw, ch, iw, ih, renderWidth, renderHeight, offsetX, offsetY };
    return cachedRect;
}

export function drawFrame(ctx, canvas, container, requestRender, overlayOptions = null) {
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    const imgRect = getImageRenderRect(container);
    const toX = (normX) => imgRect.offsetX + (normX * imgRect.renderWidth);
    const toY = (normY) => imgRect.offsetY + (normY * imgRect.renderHeight);

    const scale = CanvasStore.scale;
    const isInteracting = CanvasStore.isInteracting;

    ctx.imageSmoothingEnabled = !isInteracting;
    ctx.imageSmoothingQuality = isInteracting ? 'low' : 'high';

    ctx.save();

    const panX = isInteracting ? Math.round(CanvasStore.panX) : CanvasStore.panX;
    const panY = isInteracting ? Math.round(CanvasStore.panY) : CanvasStore.panY;

    ctx.scale(dpr, dpr);
    ctx.translate(panX, panY);
    ctx.scale(scale, scale);

    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    const currentBoardSide = (AppState.activeSide || "TOP").toUpperCase();
    const activeMatrix = getEffectiveMatrix(AppState.boardviewNetlist, currentBoardSide, AppState.getActiveMatrix());

    // Render CAD Boardview
    if (AppState.boardviewNetlist && activeMatrix) {
        renderWireframeOverlay(
            ctx, scale, toX, toY, activeMatrix,
            AppState.boardviewNetlist,
            AppState.activeSide,
            {
                zoom: scale,
                opacity: 1.0,
                isDragging: !!isInteracting,
                quadBroken: false,
                fx: null,
                inspectedNet: CanvasStore.inspectedNet,
                hoverHit: overlayOptions?.hoverHit || null
            }
        );
    }

    renderFocusHighlight(ctx, scale, toX, toY, requestRender);
    ctx.restore();
}