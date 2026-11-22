// =========================================================================
// VIZO BoardViewer - state/CanvasStore.js
// =========================================================================
import { AppEvents } from './EventBus.js';

const state = {
    currentTool: 'pan',
    tableScale: 1.0,
    tablePanX: 0,
    tablePanY: 0,
    isInteracting: false,
    focusedHighlight: null,
    inspectedNet: null
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
        const scaleChanged = scale !== state.tableScale;
        state.tableScale = scale;
        state.tablePanX = panX;
        state.tablePanY = panY;
        if (scaleChanged) AppEvents.emit('canvas:scaleChanged', state.tableScale);
    },

    resetTransform() {
        const scaleChanged = state.tableScale !== 1.0;
        state.tableScale = 1.0;
        state.tablePanX = 0;
        state.tablePanY = 0;
        if (scaleChanged) AppEvents.emit('canvas:scaleChanged', state.tableScale);
    },

    get focusedHighlight() { return state.focusedHighlight; },
    setFocusedHighlight(h) { state.focusedHighlight = h; },
    clearFocusedHighlight() { state.focusedHighlight = null; },

    get inspectedNet() { return state.inspectedNet; },
    setInspectedNet(info) { state.inspectedNet = info || null; },
    clearInspectedNet() { state.inspectedNet = null; },

    reset() {
        const scaleChanged = state.tableScale !== 1.0;
        state.currentTool = 'pan';
        state.tableScale = 1.0;
        state.tablePanX = 0;
        state.tablePanY = 0;
        state.isInteracting = false;
        state.focusedHighlight = null;
        state.inspectedNet = null;
        if (scaleChanged) AppEvents.emit('canvas:scaleChanged', state.tableScale);
    }
};

AppEvents.on('state:reset', () => {
    CanvasStore.reset();
});