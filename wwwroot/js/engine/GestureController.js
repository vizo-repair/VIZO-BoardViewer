// ============================================================================
// FILE: wwwroot/js/engine/GestureController.js (VIZO 2.0 - High Performance 120 FPS)
// ============================================================================
import { CanvasStore } from '../state/CanvasStore.js';

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

        this.updateCachedRect();
        window.addEventListener('resize', () => this.updateCachedRect());
        this.bindEvents();
    }

    updateCachedRect() {
        if (this.container) {
            const r = this.container.getBoundingClientRect();
            this.cachedRect = { left: r.left, top: r.top, width: r.width, height: r.height };
        }
    }

    bindEvents() {
        // -------------------------------------------------------------
        // 1. MOUSE (DESKTOP)
        // -------------------------------------------------------------
        this.container.addEventListener('mousedown', (e) => {
            if (e.button !== 0 && e.button !== 1) return;
            if (e.target.closest('#card-mode, #search-mode, #ai-status, #lang-menu, #pdf-modal, #file-badge, #left-toolbar, #pwa-install-banner, #repairs-dropdown-menu')) return;

            if (CanvasStore.currentTool !== 'pan' && e.button === 0) return;

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
            if (e.target.closest('#card-mode, #search-mode, #ai-status, #lang-menu, #pdf-modal, #file-badge, #left-toolbar, #pwa-install-banner, #repairs-dropdown-menu')) return;

            const rect = this.cachedRect || this.container.getBoundingClientRect();

            if (e.touches.length === 1 && CanvasStore.currentTool === 'pan') {
                this.touchMode = 'pan';
                this.touchStartX = e.touches[0].clientX;
                this.touchStartY = e.touches[0].clientY;
                this.initialPanX = CanvasStore.panX;
                this.initialPanY = CanvasStore.panY;
            } else if (e.touches.length >= 2) {
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
            if (e.target.closest('#card-mode, #search-mode, #ai-status, #lang-menu, #pdf-modal, #file-badge, #left-toolbar, #pwa-install-banner, #repairs-dropdown-menu')) return;
            e.preventDefault();

            // Enable fast render mode (disable heavy shadows)
            CanvasStore.setInteracting(true);

            if (this.wheelEndTimeout) clearTimeout(this.wheelEndTimeout);
            this.wheelEndTimeout = setTimeout(() => {
                CanvasStore.setInteracting(false);
                this.engine?.requestRender();
            }, 100);

            const rect = this.cachedRect || this.container.getBoundingClientRect();
            const factor = e.deltaY < 0 ? 1.15 : 0.85;
            const newScale = Math.min(Math.max(1.0, CanvasStore.scale * factor), 30.0);

            if (newScale !== CanvasStore.scale) {
                const mouseX = e.clientX - rect.left;
                const mouseY = e.clientY - rect.top;

                const worldX = (mouseX - CanvasStore.panX) / CanvasStore.scale;
                const worldY = (mouseY - CanvasStore.panY) / CanvasStore.scale;

                CanvasStore.setTransform(newScale, mouseX - (worldX * newScale), mouseY - (worldY * newScale));
                this.engine?.requestRender();
            }
        }, { passive: false });
    }
}