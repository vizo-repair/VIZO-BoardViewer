// ============================================================================
// FILE: wwwroot/js/engine/CameraNavigator.js
// Smooth camera kinematics (Glide & Zoom) to selected part, pin, or VIA
// ============================================================================
import { AppState, AppEvents } from '../state/AppState.js';
import { CanvasStore } from '../state/CanvasStore.js';
import { transformPoint } from './HomographySolver.js';
import { getImageRenderRect } from './CanvasRenderer.js';
import { showAppToast } from '../ui/ToastNotification.js';
import { t } from '../ui/i18n/I18nStore.js';
import { formatBoardSide } from '../ui/i18n/DisplayLabels.js';
import { getVisibleFocusCenter } from './ViewportFocus.js';
import { getEffectiveMatrix } from './BoardCorners.js';

let animFrameId = null;

export class CameraNavigator {
    static init(engine) {
        AppEvents.on('component:focus', ({ refDes, pinNum }) => {
            this.flyToComponent(refDes, engine, pinNum);
        });
    }

    static flyToComponent(refDes, engine, pinNum = null) {
        if (!refDes) return;

        let cleanRef = refDes.trim().toUpperCase();
        let targetPin = pinNum ? String(pinNum).trim() : null;

        // If a string like "D18.2" is passed, split into RefDes and Pin
        if (cleanRef.includes('.') && !targetPin) {
            const parts = cleanRef.split('.');
            cleanRef = parts[0];
            targetPin = parts[1];
        }

        const netlist = AppState.boardviewNetlist;

        let targetNormX = 0.5;
        let targetNormY = 0.5;
        let compSide = AppState.activeSide || "TOP";
        let isOpposite = false;
        let targetComp = null;
        let visualBox = null;
        let modeLabel = "🎯";
        let cadX = 0;
        let cadY = 0;
        let found = false;

        // 1. Search in ICs and discretes in netlist.components
        if (netlist?.components) {
            targetComp = netlist.components[cleanRef] ||
                Object.entries(netlist.components).find(([k]) => k.toUpperCase() === cleanRef)?.[1];

            if (targetComp) {
                cadX = targetComp.x;
                cadY = targetComp.y;
                compSide = (targetComp.side || "TOP").toUpperCase().startsWith("B") ? "BOTTOM" : "TOP";

                // If a specific pin is specified (e.g., pin 2), fly directly to its coordinates
                if (targetPin && targetComp.pins && targetComp.pins[targetPin]) {
                    cadX = targetComp.pins[targetPin].x;
                    cadY = targetComp.pins[targetPin].y;
                }
                found = true;
            }
        }

        // 2. Search in VIAs and TestPoints
        if (!found && Array.isArray(netlist?.testPoints)) {
            const tp = netlist.testPoints.find(t => (t.nail || '').toUpperCase() === cleanRef);
            if (tp) {
                cadX = tp.x;
                cadY = tp.y;
                compSide = (tp.side || "TOP").toUpperCase().startsWith("B") ? "BOTTOM" : "TOP";
                modeLabel = tp.isVia ? "VIA" : "TP";
                targetComp = { x: tp.x, y: tp.y, side: compSide };
                found = true;
            }
        }

        const compTargetSide = compSide;
        const effectiveMatrix = getEffectiveMatrix(netlist, compTargetSide, null);

        if (found && effectiveMatrix) {
            isOpposite = (compSide !== (AppState.activeSide || "TOP").toUpperCase());
            const norm = transformPoint(effectiveMatrix, cadX, cadY);
            targetNormX = norm.x;
            targetNormY = norm.y;
        } else {
            showAppToast(t('compNotOnSide', "Part '{ref}' was not found on the current side", { ref: cleanRef }), true);
            return;
        }

        const container = document.getElementById('table-container');
        const imgRect = getImageRenderRect(container);

        const targetCanvasX = imgRect.offsetX + (targetNormX * imgRect.renderWidth);
        const targetCanvasY = imgRect.offsetY + (targetNormY * imgRect.renderHeight);

        const focus = getVisibleFocusCenter(container);
        const targetScale = Math.min(Math.max(CanvasStore.scale, 2.4), 8.0);

        const targetPanX = focus.x - (targetCanvasX * targetScale);
        const targetPanY = focus.y - (targetCanvasY * targetScale);

        CanvasStore.setFocusedHighlight({
            refDes: targetPin ? `${cleanRef}.${targetPin}` : cleanRef,
            modeLabel: modeLabel,
            comp: targetComp,
            visualBox: visualBox,
            normX: targetNormX,
            normY: targetNormY,
            side: compSide,
            isOpposite: isOpposite,
            expiresAt: Date.now() + 5000
        });

        this.animateGlide(CanvasStore.scale, CanvasStore.panX, CanvasStore.panY, targetScale, targetPanX, targetPanY, engine);

        if (isOpposite) {
            showAppToast(t('compOnOtherSide', 'Part {ref} is on the other side ({side})', { ref: cleanRef, side: formatBoardSide(compSide) }));
        }
    }

    static cancelGlide() {
        if (animFrameId) {
            cancelAnimationFrame(animFrameId);
            animFrameId = null;
            CanvasStore.setInteracting(false);
        }
    }

    static animateGlide(fromScale, fromPanX, fromPanY, toScale, toPanX, toPanY, engine) {
        this.cancelGlide();

        const startTime = performance.now();
        const duration = 320;

        CanvasStore.setInteracting(true);

        const step = (now) => {
            const elapsed = now - startTime;
            const progress = Math.min(1.0, elapsed / duration);
            const ease = 1 - Math.pow(1 - progress, 3); // Cubic Out

            const curScale = fromScale + (toScale - fromScale) * ease;
            const curPanX = fromPanX + (toPanX - fromPanX) * ease;
            const curPanY = fromPanY + (toPanY - fromPanY) * ease;

            CanvasStore.setTransform(curScale, curPanX, curPanY);
            engine?.renderImmediate();

            if (progress < 1.0) {
                animFrameId = requestAnimationFrame(step);
            } else {
                animFrameId = null;
                CanvasStore.setInteracting(false);
                engine?.requestRender();
            }
        };

        animFrameId = requestAnimationFrame(step);
    }
}