// ============================================================================
// FILE: wwwroot/js/ui/DualSideController.js (VIZO 2.0 - ImageBitmap Engine)
// ============================================================================
import { AppState, AppEvents } from '../state/AppState.js';
import { CanvasStore } from '../state/CanvasStore.js';
import { getImageRenderRect, invalidateRenderRect } from '../engine/CanvasRenderer.js';
import { showAppToast } from './ToastNotification.js';
import { t } from './i18n/I18nStore.js';
import { formatBoardSide } from './i18n/DisplayLabels.js';
import { getStandaloneOrientation } from '../engine/BoardCorners.js';

export class DualSideController {
    static init() {
        AppEvents.on('activeSide:changed', () => {
            this.refreshSideLabel();
            invalidateRenderRect();
            AppEvents.emit('canvas:requestRender');
        });
        AppEvents.on('state:reset', () => invalidateRenderRect());
        window.addEventListener('resize', () => invalidateRenderRect());
        this.refreshSideLabel();
    }

    static refreshSideLabel() {
        const label = document.getElementById('label-board-side');
        if (label) label.innerText = formatBoardSide(AppState.activeSide || 'TOP', { short: true });
    }

    static toggleSide(targetSide = null) {
        const current = AppState.activeSide || "TOP";
        const nextSide = targetSide || (current === "TOP" ? "BOTTOM" : "TOP");

        const drawCanvas = document.getElementById('table-draw-canvas');
        if (drawCanvas) {
            drawCanvas.classList.add('flip-animate');
            setTimeout(() => drawCanvas.classList.remove('flip-animate'), 380);
        }

        this.mirrorCameraFocus();

        setTimeout(() => {
            AppState.setActiveSide(nextSide);
            this.refreshSideLabel();

            invalidateRenderRect();
            showAppToast(t('boardSideToast', 'Board side: {side}', { side: formatBoardSide(nextSide) }));
            AppEvents.emit('canvas:requestRender');
        }, 190);
    }

    static mirrorCameraFocus() {
        const container = document.getElementById('table-container');
        const cw = container?.clientWidth || window.innerWidth;
        const ch = container?.clientHeight || window.innerHeight;

        const currentScale = CanvasStore.scale || 1.0;
        const currentPanX = CanvasStore.panX || 0;
        const currentPanY = CanvasStore.panY || 0;

        const imgRect = getImageRenderRect(container);

        if (imgRect.renderWidth > 0 && imgRect.renderHeight > 0) {
            const screenCenterX = cw / 2;
            const screenCenterY = ch / 2;

            const canvasCenterX = (screenCenterX - currentPanX) / currentScale;
            const canvasCenterY = (screenCenterY - currentPanY) / currentScale;

            const normX = (canvasCenterX - imgRect.offsetX) / imgRect.renderWidth;
            const normY = (canvasCenterY - imgRect.offsetY) / imgRect.renderHeight;

            const orient = (typeof getStandaloneOrientation === 'function')
                ? getStandaloneOrientation()
                : { rotateDeg: 0 };
            const rot = ((Math.round((orient.rotateDeg || 0) / 90) * 90) % 360 + 360) % 360;
            const isQuarterTurn = (rot === 90 || rot === 270);

            let mirroredNormX = normX;
            let mirroredNormY = normY;

            // If the board is rotated 90°/270°, a physical flip mirrors the vertical screen axis
            if (isQuarterTurn) {
                mirroredNormY = Math.max(0.0, Math.min(1.0, 1.0 - normY));
            } else {
                mirroredNormX = Math.max(0.0, Math.min(1.0, 1.0 - normX));
            }

            const newCanvasX = imgRect.offsetX + (mirroredNormX * imgRect.renderWidth);
            const newCanvasY = imgRect.offsetY + (mirroredNormY * imgRect.renderHeight);

            const newPanX = screenCenterX - (newCanvasX * currentScale);
            const newPanY = screenCenterY - (newCanvasY * currentScale);

            CanvasStore.setTransform(currentScale, newPanX, newPanY);
        }
    }
}