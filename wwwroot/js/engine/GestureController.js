// ============================================================================
// FILE: wwwroot/js/engine/GestureController.js (VIZO BoardViewer - 120 FPS)
// ============================================================================
import { CanvasStore } from '../state/CanvasStore.js';
import { AppEvents } from '../state/EventBus.js';
import { CameraNavigator } from './CameraNavigator.js';

const IGNORED_UI_SELECTOR = '#canvas-zoom-controls, #net-inspector-hud, #viewer-topbar, #viewer-spotlight-modal, #viewer-shortcuts-modal, #viewer-dropzone';

export class GestureController {
    constructor(containerEl, onTransformChanged, engine) {
        this.container = containerEl;
        this.onTransformChanged = onTransformChanged;
        this.engine = engine;

        this.isMouseDragging = false;
        this.mouseStartX = 0;
        this.mouseStartY = 0;
        this.initialPanX = 0;
        this.initialPanY = 0;
        this.touchMode = null;
        this.touchStartX = 0;
        this.touchStartY = 0;
        this.initialPinchDist = 0;
        this.initialScale = 1.0;
        this.initialWorldCenter = { x: 0, y: 0 };
        this.cachedRect = null;
        this.wheelEndTimeout = null;
        this.tapCandidate = null;
        this.lastTap = null;
        this.lastTouchReset = 0;
        this.zoomControls = containerEl.querySelector('#canvas-zoom-controls');
        this.zoomValue = this.zoomControls?.querySelector('[data-zoom-value]');

        AppEvents.on('canvas:scaleChanged', () => this.updateZoomValue());
        this.updateZoomValue();
        this.updateCachedRect();
        window.addEventListener('resize', () => this.updateCachedRect());
        this.bindEvents();
    }

    updateZoomValue() {
        if (this.zoomValue) this.zoomValue.textContent = `${Math.round(CanvasStore.scale * 100)}%`;
    }

    resetView() {
        CameraNavigator.cancelGlide();
        CanvasStore.resetTransform();
        this.engine?.requestRender();
    }

    zoomAt(factor, x, y) {
        CameraNavigator.cancelGlide();
        const scale = CanvasStore.scale;
        const next = Math.min(Math.max(1.0, scale * factor), 30.0);
        if (next === scale) return;
        const worldX = (x - CanvasStore.panX) / scale;
        const worldY = (y - CanvasStore.panY) / scale;
        CanvasStore.setTransform(next, x - worldX * next, y - worldY * next);
        this.engine?.requestRender();
    }

    updateCachedRect() {
        if (this.container) {
            const r = this.container.getBoundingClientRect();
            this.cachedRect = { left: r.left, top: r.top, width: r.width, height: r.height };
        }
    }

    bindEvents() {
        this.zoomControls?.addEventListener('click', (e) => {
            const button = e.target.closest('[data-zoom]');
            if (!button) return;
            if (button.dataset.zoom === 'fit') {
                this.resetView();
            } else {
                const rect = this.cachedRect || this.container.getBoundingClientRect();
                this.zoomAt(button.dataset.zoom === 'in' ? 1.25 : 1 / 1.25, rect.width / 2, rect.height / 2);
            }
        });

        this.container.addEventListener('dblclick', (e) => {
            if (e.target !== this.engine?.canvas || CanvasStore.currentTool !== 'pan' || Date.now() - this.lastTouchReset < 700) return;
            e.preventDefault();
            this.resetView();
        });

        // -------------------------------------------------------------
        // 1. MOUSE (DESKTOP)
        // -------------------------------------------------------------
        this.container.addEventListener('mousedown', (e) => {
            if (e.button !== 0 && e.button !== 1) return;
            if (e.target.closest(IGNORED_UI_SELECTOR)) return;

            if (CanvasStore.currentTool !== 'pan' && e.button === 0) return;

            CameraNavigator.cancelGlide();
            this.isMouseDragging = true;
            this.mouseStartX = e.clientX;
            this.mouseStartY = e.clientY;
            this.initialPanX = CanvasStore.panX;
            this.initialPanY = CanvasStore.panY;
        });

        window.addEventListener('mousemove', (e) => {
            if (!this.isMouseDragging) return;
            const dx = e.clientX - this.mouseStartX;
            const dy = e.clientY - this.mouseStartY;
            if (Math.hypot(dx, dy) < 2) return;

            CanvasStore.setInteracting(true);
            this.container.style.cursor = 'grabbing';
            CanvasStore.setTransform(CanvasStore.scale, this.initialPanX + dx, this.initialPanY + dy);
            this.engine?.requestRender();
        });

        window.addEventListener('mouseup', () => {
            if (this.isMouseDragging) {
                this.isMouseDragging = false;
                CanvasStore.setInteracting(false);
                this.container.style.cursor = 'grab';
                this.engine?.requestRender();
            }
        });

        // -------------------------------------------------------------
        // 2. SMARTPHONE / TABLET (Touch)
        // -------------------------------------------------------------
        this.container.addEventListener('touchstart', (e) => {
            if (e.target.closest(IGNORED_UI_SELECTOR)) {
                this.tapCandidate = null;
                this.lastTap = null;
                return;
            }

            if (e.touches.length === 1 && e.target === this.engine?.canvas && CanvasStore.currentTool === 'pan') {
                const touch = e.touches[0];
                this.tapCandidate = { x: touch.clientX, y: touch.clientY, time: Date.now() };
            } else {
                this.tapCandidate = null;
                this.lastTap = null;
            }

            const rect = this.cachedRect || this.container.getBoundingClientRect();

            if (e.touches.length === 1 && CanvasStore.currentTool === 'pan') {
                CameraNavigator.cancelGlide();
                this.touchMode = 'pan';
                this.touchStartX = e.touches[0].clientX;
                this.touchStartY = e.touches[0].clientY;
                this.initialPanX = CanvasStore.panX;
                this.initialPanY = CanvasStore.panY;
            } else if (e.touches.length >= 2) {
                CameraNavigator.cancelGlide();
                e.preventDefault();
                this.touchMode = 'pinch';
                CanvasStore.setInteracting(true);
                this.initialPinchDist = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY);
                this.initialScale = CanvasStore.scale;
                this.initialPanX = CanvasStore.panX;
                this.initialPanY = CanvasStore.panY;

                const midX = ((e.touches[0].clientX + e.touches[1].clientX) / 2) - rect.left;
                const midY = ((e.touches[0].clientY + e.touches[1].clientY) / 2) - rect.top;
                this.initialWorldCenter = {
                    x: (midX - this.initialPanX) / this.initialScale,
                    y: (midY - this.initialPanY) / this.initialScale
                };
            }
        }, { passive: false });

        this.container.addEventListener('touchmove', (e) => {
            if (this.tapCandidate && (e.touches.length !== 1 || Math.hypot(e.touches[0].clientX - this.tapCandidate.x, e.touches[0].clientY - this.tapCandidate.y) > 8)) {
                this.tapCandidate = null;
                this.lastTap = null;
            }
            if (!this.touchMode) return;
            const rect = this.cachedRect || this.container.getBoundingClientRect();

            if (this.touchMode === 'pan' && e.touches.length === 1 && CanvasStore.currentTool === 'pan') {
                e.preventDefault();
                const dx = e.touches[0].clientX - this.touchStartX;
                const dy = e.touches[0].clientY - this.touchStartY;
                if (Math.hypot(dx, dy) < 2) return;

                CanvasStore.setInteracting(true);
                CanvasStore.setTransform(CanvasStore.scale, this.initialPanX + dx, this.initialPanY + dy);
                this.engine?.requestRender();
            } else if (this.touchMode === 'pinch' && e.touches.length >= 2) {
                e.preventDefault();
                const dist = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY);
                if (this.initialPinchDist > 5) {
                    const factor = dist / this.initialPinchDist;
                    const newScale = Math.min(Math.max(1.0, this.initialScale * factor), 30.0);
                    const midX = ((e.touches[0].clientX + e.touches[1].clientX) / 2) - rect.left;
                    const midY = ((e.touches[0].clientY + e.touches[1].clientY) / 2) - rect.top;

                    CanvasStore.setTransform(newScale, midX - (this.initialWorldCenter.x * newScale), midY - (this.initialWorldCenter.y * newScale));
                    this.engine?.requestRender();
                }
            }
        }, { passive: false });

        const onTouchEnd = (e) => {
            if (e.touches.length === 0 && this.tapCandidate) {
                const tap = this.tapCandidate;
                const now = Date.now();
                if (e.type === 'touchend' && now - tap.time <= 300) {
                    if (this.lastTap && now - this.lastTap.time <= 350 && Math.hypot(tap.x - this.lastTap.x, tap.y - this.lastTap.y) <= 25) {
                        this.lastTap = null;
                        this.lastTouchReset = now;
                        this.resetView();
                    } else {
                        this.lastTap = { x: tap.x, y: tap.y, time: now };
                    }
                } else {
                    this.lastTap = null;
                }
                this.tapCandidate = null;
            }
            if (e.touches.length === 0) {
                this.touchMode = null;
                CanvasStore.setInteracting(false);
                this.engine?.requestRender();
            } else if (e.touches.length === 1 && CanvasStore.currentTool === 'pan') {
                this.touchMode = 'pan';
                this.touchStartX = e.touches[0].clientX;
                this.touchStartY = e.touches[0].clientY;
                this.initialPanX = CanvasStore.panX;
                this.initialPanY = CanvasStore.panY;
            }
        };

        this.container.addEventListener('touchend', onTouchEnd);
        this.container.addEventListener('touchcancel', onTouchEnd);

        // -------------------------------------------------------------
        // 3. MOUSE WHEEL (DESKTOP ZOOM) - With Fast-Path mode
        // -------------------------------------------------------------
        window.addEventListener('wheel', (e) => {
            if (e.target.closest(IGNORED_UI_SELECTOR)) return;
            e.preventDefault();
            CameraNavigator.cancelGlide();

            CanvasStore.setInteracting(true);

            if (this.wheelEndTimeout) clearTimeout(this.wheelEndTimeout);
            this.wheelEndTimeout = setTimeout(() => {
                CanvasStore.setInteracting(false);
                this.engine?.requestRender();
            }, 100);

            const rect = this.cachedRect || this.container.getBoundingClientRect();
            const factor = e.deltaY < 0 ? 1.15 : 0.85;
            this.zoomAt(factor, e.clientX - rect.left, e.clientY - rect.top);
        }, { passive: false });
    }
}