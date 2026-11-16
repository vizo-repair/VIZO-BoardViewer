// =========================================================================
// VIZO 2.0 - state/CanvasStore.js
// =========================================================================
import { AppEvents } from './EventBus.js';

const state = {
    currentTool: 'pan',
    tableScale: 1.0,
    tablePanX: 0,
    tablePanY: 0,
    isInteracting: false,
    focusedHighlight: null
};

export const CanvasStore = {
    get currentTool() { return state.currentTool; },
    setTool(tool) { state.currentTool = tool; },

    get isInteracting() { return state.isInteracting; },
    setInteracting(val) { state.isInteracting = !!val; },

    get scale() { return state.tableScale; },
    get panX() { return state.tablePanX; },
    get panY() { return state.tablePanY; },

    setTransform(scale, panX, panY) {
        state.tableScale = scale;
        state.tablePanX = panX;
        state.tablePanY = panY;
    },

    resetTransform() {
        state.tableScale = 1.0;
        state.tablePanX = 0;
        state.tablePanY = 0;
    },

    get focusedHighlight() { return state.focusedHighlight; },
    setFocusedHighlight(h) { state.focusedHighlight = h; },
    clearFocusedHighlight() { state.focusedHighlight = null; },

    reset() {
        state.currentTool = 'pan';
        state.tableScale = 1.0;
        state.tablePanX = 0;
        state.tablePanY = 0;
        state.isInteracting = false;
        state.focusedHighlight = null;
    }
};

AppEvents.on('state:reset', () => {
    CanvasStore.reset();
});