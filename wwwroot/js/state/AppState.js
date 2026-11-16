// ============================================================================
// FILE: wwwroot/js/state/AppState.js (VIZO 2.0)
// ============================================================================
import { AppEvents } from './EventBus.js';

export { AppEvents };

const state = {
    activeSide: "TOP", // "TOP" | "BOTTOM"
    boardviewFile: null,
    boardviewNetlist: null
};

export const AppState = {
    get activeSide() { return state.activeSide; },
    setActiveSide(side) {
        state.activeSide = (side && side.toUpperCase() === "BOTTOM") ? "BOTTOM" : "TOP";
        AppEvents.emit('activeSide:changed', state.activeSide);
    },

    get boardviewFile() { return state.boardviewFile; },
    setBoardviewFile(f) { state.boardviewFile = f; AppEvents.emit('boardview:changed', f); },

    get boardviewNetlist() { return state.boardviewNetlist; },
    setBoardviewNetlist(n) { state.boardviewNetlist = n; AppEvents.emit('boardviewNetlist:changed', n); },

    getActiveMatrix() {
        return null;
    },

    clearAll() {
        state.activeSide = "TOP";
        state.boardviewFile = null;
        state.boardviewNetlist = null;
        AppEvents.emit('state:reset', null);
    }
};