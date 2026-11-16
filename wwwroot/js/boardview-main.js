// ============================================================================
// FILE: wwwroot/js/boardview-main.js
// ============================================================================
import { AppState, AppEvents } from './state/AppState.js';
import { CanvasStore } from './state/CanvasStore.js';
import { CanvasEngine } from './engine/CanvasEngine.js';
import { NetInspectorController } from './engine/NetInspectorController.js';
import { DualSideController } from './ui/DualSideController.js';
import { CameraNavigator } from './engine/CameraNavigator.js';
import { parseBoardviewFile } from './api/BoardviewApi.js';
import { findNearestCadComponent } from './engine/SnapEngine.js';
import { getImageRenderRect } from './engine/CanvasRenderer.js';
import { getEffectiveMatrix, rotateStandaloneCad, flipStandaloneCad } from './engine/BoardCorners.js';
import { formatBoardSide } from './ui/i18n/DisplayLabels.js';
import { showAppToast } from './ui/ToastNotification.js';
import { escapeHtml } from './ui/HtmlSanitizer.js';
import { t, userFacingError } from './ui/i18n/I18nStore.js';

let engine = null;
const inspector = new NetInspectorController();
let spotlightSelectedIdx = 0;
let currentSpotlightResults = [];
let isNodeListOpen = false;

const toggleZenMode = () => {
    const isZen = document.body.classList.toggle('zen-mode');
    showAppToast(t(isZen ? 'zenModeEnabled' : 'zenModeDisabled', isZen ? 'Zen mode: UI hidden. Press [H] to restore.' : 'Interface restored.'));
};

async function initViewer() {
    DualSideController.init();

    // 3. Create CanvasEngine BEFORE calling CameraNavigator
    const canvasEl = document.getElementById('table-draw-canvas');
    const containerEl = document.getElementById('table-container');
    engine = new CanvasEngine(canvasEl, containerEl);
    CameraNavigator.init(engine);

    // 4. Render Net Inspector in the render loop
    const origRender = engine.renderImmediate.bind(engine);
    engine.renderImmediate = function () {
        origRender();
        if (inspector.selectedNet) {
            const currentSide = (AppState.activeSide || 'TOP').toUpperCase();
            const matrix = getEffectiveMatrix(AppState.boardviewNetlist, currentSide, AppState.getActiveMatrix());
            const imgRect = getImageRenderRect(this.container);
            const toX = (normX) => imgRect.offsetX + (normX * imgRect.renderWidth);
            const toY = (normY) => imgRect.offsetY + (normY * imgRect.renderHeight);
            const scale = CanvasStore.scale;
            const dpr = window.devicePixelRatio || 1;

            if (matrix) {
                this.ctx.save();
                this.ctx.scale(dpr, dpr);
                this.ctx.translate(CanvasStore.panX, CanvasStore.panY);
                this.ctx.scale(scale, scale);
                inspector.renderNetOverlay(this.ctx, scale, toX, toY, matrix, () => this.requestRender());
                this.ctx.restore();
            }
        }
    };

    setupDragAndDrop();
    setupActions();
    setupCanvasClick();
    setupKeyboardShortcuts();
    setupSpotlightSearch();

    // Sync UI when board side changes
    AppEvents.on('activeSide:changed', () => {
        DualSideController.refreshSideLabel();
        updateSideLabel();
        if (inspector.selectedNet) {
            inspector.selectNet(inspector.selectedNet, AppState.boardviewNetlist, AppState.activeSide);
            updateNetHud(inspector.getSummary());
        }
        engine.render();
    });
}

function updateSideLabel() {
    const el = document.getElementById('viewer-side-label');
    if (el) el.innerText = formatBoardSide(AppState.activeSide || 'TOP', { short: true });
}

function setupDragAndDrop() {
    const dropzone = document.getElementById('viewer-dropzone');
    const dropTarget = document.getElementById('viewer-drop-target');

    window.addEventListener('dragover', (e) => {
        e.preventDefault();
        if (dropzone && dropzone.classList.contains('hidden')) {
            dropzone.classList.remove('hidden');
            dropzone.classList.add('flex');
        }
    });

    window.addEventListener('dragleave', (e) => {
        if (e.relatedTarget === null && AppState.boardviewNetlist) {
            dropzone?.classList.add('hidden');
            dropzone?.classList.remove('flex');
            dropTarget?.classList.remove('drag-over');
        }
    });

    if (dropTarget) {
        dropTarget.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropTarget.classList.add('drag-over');
        });
        dropTarget.addEventListener('dragleave', () => {
            dropTarget.classList.remove('drag-over');
        });
    }

    window.addEventListener('drop', async (e) => {
        e.preventDefault();
        dropTarget?.classList.remove('drag-over');
        const file = e.dataTransfer?.files?.[0];
        if (!file) return;

        const name = (file.name || '').toLowerCase();
        if (name.endsWith('.bdv') || name.endsWith('.brd') || name.endsWith('.cad') ||
            name.endsWith('.fz') || name.endsWith('.asc') || name.endsWith('.tvw') ||
            name.endsWith('.bvr') || name.endsWith('.cst') || name.endsWith('.xzz')) {
            await loadBoardview(file);
        } else {
            showAppToast(t('toastFileTypeUnsupported', 'This file type is not supported.'), true);
        }
    });
}

async function loadBoardview(file) {
    if (file.size > 100 * 1024 * 1024) {
        showAppToast(t('errFileTooLargeMb', 'File is too large. Maximum size is 100MB.'), true);
        return;
    }
    showAppToast(t('toastBoardviewLoading', 'Uploading and processing Boardview: {name}...', { name: file.name }));
    try {
        const dto = await parseBoardviewFile(file);
        if (!dto) throw new Error(t('errParseBoardviewFailed', 'Failed to parse Boardview file.'));

        AppState.setBoardviewNetlist(dto);
        AppState.setBoardviewFile({ type: "document", fileName: file.name, fileUrl: dto.netlistUrl || null });

        document.getElementById('viewer-dropzone')?.classList.add('hidden');
        document.getElementById('viewer-dropzone')?.classList.remove('flex');

        const titleEl = document.getElementById('viewer-board-title');
        if (titleEl) titleEl.innerText = dto.board || file.name;

        inspector.clearSelection();
        hideNetHud();

        CanvasStore.resetTransform();

        showAppToast(t('toastViewerFileLoaded', 'Board file {name} opened successfully ({count} parts)', {
            name: file.name,
            count: dto.stats?.totalComponents || 0
        }));

        engine.render();
    } catch (err) {
        showAppToast(t('toastBoardviewError', 'Boardview error: {msg}', { msg: userFacingError(err) }), true);
    }
}

function setupCanvasClick() {
    const canvas = document.getElementById('table-draw-canvas');
    if (!canvas) return;

    let downPos = { x: 0, y: 0, time: 0 };

    canvas.addEventListener('pointerdown', (e) => {
        downPos = { x: e.clientX, y: e.clientY, time: Date.now() };
    });

    canvas.addEventListener('pointerup', (e) => {
        const dist = Math.hypot(e.clientX - downPos.x, e.clientY - downPos.y);
        const duration = Date.now() - downPos.time;

        if (dist > 8 || duration > 350) return;

        const r = canvas.getBoundingClientRect();
        const cx = (e.clientX - r.left - CanvasStore.panX) / CanvasStore.scale;
        const cy = (e.clientY - r.top - CanvasStore.panY) / CanvasStore.scale;

        const netlist = AppState.boardviewNetlist;
        if (!netlist) return;

        const imgRect = getImageRenderRect(engine.container);
        const normX = imgRect.renderWidth ? (cx - imgRect.offsetX) / imgRect.renderWidth : 0.5;
        const normY = imgRect.renderHeight ? (cy - imgRect.offsetY) / imgRect.renderHeight : 0.5;

        const hit = findNearestCadComponent(normX, normY, 35, imgRect, CanvasStore.scale, { allowBody: true });

        if (hit) {
            const targetNet = (hit.net && !hit.net.toUpperCase().startsWith('NC') && hit.net !== 'UNCONNECTED')
                ? hit.net
                : (hit.snappedPin ? `PIN_${hit.snappedPin}` : 'NO_NET');

            const summary = inspector.selectNet(targetNet, netlist, AppState.activeSide, hit);
            if (summary) {
                updateNetHud(summary);
                engine.render();
                return;
            }
        }

        inspector.clearSelection();
        hideNetHud();
        engine.render();
    });
}

function updateNetHud(summary) {
    const hud = document.getElementById('net-inspector-hud');
    if (!hud || !summary) return;

    document.getElementById('hud-net-name').innerText = summary.netName;
    document.getElementById('hud-nodes-count').innerText = t('hudNodesCount', '{count} nodes', { count: summary.totalCount });

    const portalBtn = document.getElementById('hud-portal-btn');
    const oppositeLabel = document.getElementById('hud-opposite-count');

    if (summary.oppositeNodesCount > 0) {
        const otherSide = AppState.activeSide === 'TOP' ? 'BOTTOM' : 'TOP';
        if (oppositeLabel) {
            oppositeLabel.innerText = t('hudOppositeCount', '{count} on {side}', {
                count: summary.oppositeNodesCount,
                side: formatBoardSide(otherSide, { short: true })
            });
        }
        portalBtn?.classList.remove('hidden');
        portalBtn?.classList.add('flex');
    } else {
        portalBtn?.classList.add('hidden');
        portalBtn?.classList.remove('flex');
    }
    const compTeaser = document.getElementById('hud-comp-teaser');
    const compRefEl = document.getElementById('hud-comp-refdes');
    const compDescEl = document.getElementById('hud-comp-desc');

    // Show PDF / Pinout teaser buttons ONLY for active chips (not resistors/capacitors/holes)
    if (inspector.selectedRefDes && summary.isTargetActive) {
        if (compRefEl) compRefEl.innerText = inspector.selectedRefDes;
        const comp = AppState.boardviewNetlist?.components?.[inspector.selectedRefDes];
        const desc = comp?.value || comp?.description || comp?.partNumber || '';
        if (compDescEl) compDescEl.innerText = desc ? `• ${desc}` : '';
        compTeaser?.classList.remove('hidden');
        compTeaser?.classList.add('flex');
    } else {
        compTeaser?.classList.add('hidden');
        compTeaser?.classList.remove('flex');
    }

    // Ground rail protection: disable "Analyze Net" for GND
    const btnAnalyze = document.getElementById('btn-analyze-this-net');
    if (btnAnalyze) {
        if (summary.isGnd) {
            btnAnalyze.disabled = true;
            btnAnalyze.classList.add('opacity-40', 'cursor-not-allowed');
            btnAnalyze.classList.remove('cursor-pointer', 'hover:bg-blue-500');
            btnAnalyze.title = t('toastCannotTraceGnd', 'Ground (GND) nets cannot be analyzed.');
        } else {
            btnAnalyze.disabled = false;
            btnAnalyze.classList.remove('opacity-40', 'cursor-not-allowed');
            btnAnalyze.classList.add('cursor-pointer', 'hover:bg-blue-500');
            btnAnalyze.removeAttribute('title');
        }
    }

    document.querySelectorAll('.net-filter-btn').forEach(btn => {
        btn.classList.toggle('is-active', btn.dataset.filter === summary.activeFilter);
    });

    if (isNodeListOpen) renderNodeList();

    hud.classList.remove('hidden');
}

function hideNetHud() {
    isNodeListOpen = false;
    document.getElementById('hud-node-list-container')?.classList.add('hidden');
    document.getElementById('hud-node-list-container')?.classList.remove('flex');
    document.getElementById('net-inspector-hud')?.classList.add('hidden');
}

function renderNodeList() {
    const container = document.getElementById('hud-node-list-container');
    if (!container) return;

    const nodes = inspector.inspectedNodes || [];
    if (nodes.length === 0) {
        container.innerHTML = `<div class="text-[11px] text-gray-500 p-2 text-center font-mono">${escapeHtml(t('noSavedBoards', 'No nodes'))}</div>`;
        return;
    }

    container.innerHTML = nodes.map(n => {
        const pinLabel = n.pinNum ? `${n.refDes}.${n.pinNum}` : n.refDes;
        const isShunt = n.role && n.role.startsWith('SHUNT');
        const roleLabel = isShunt ? '⚡ Shunt' : (n.role === 'SOURCE' ? '🟢 Source' : (n.role === 'VIA' ? 'VIA' : 'Node'));
        const roleColor = isShunt ? 'text-red-400' : (n.role === 'SOURCE' ? 'text-green-400' : 'text-gray-400');
        const sideColor = n.side === 'TOP' ? 'text-cyan-400' : 'text-purple-400';

        return `
            <div data-action="flyToNetNode" data-ref="${escapeHtml(n.refDes)}" data-pin="${escapeHtml(n.pinNum)}" data-side="${escapeHtml(n.side)}" class="p-1.5 px-2.5 rounded-xl hover:bg-white/10 transition cursor-pointer flex items-center justify-between text-xs font-mono">
                <div class="flex items-center gap-2">
                    <strong class="text-white">${escapeHtml(pinLabel)}</strong>
                    <span class="text-[10px] ${roleColor}">${escapeHtml(roleLabel)}</span>
                </div>
                <span class="text-[10px] font-bold ${sideColor}">${escapeHtml(formatBoardSide(n.side, { short: true }))}</span>
            </div>
        `;
    }).join('');
}

function openSpotlight() {
    const modal = document.getElementById('viewer-spotlight-modal');
    const input = document.getElementById('spotlight-input');
    if (!modal || !input) return;

    modal.classList.remove('hidden');
    modal.classList.add('flex');
    input.value = '';
    spotlightSelectedIdx = 0;
    renderSpotlightResults('');
    setTimeout(() => input.focus(), 50);
}

function closeSpotlight() {
    const modal = document.getElementById('viewer-spotlight-modal');
    if (modal) {
        modal.classList.add('hidden');
        modal.classList.remove('flex');
    }
}

function openShortcutsModal() {
    const modal = document.getElementById('viewer-shortcuts-modal');
    if (modal) {
        modal.classList.remove('hidden');
        modal.classList.add('flex');
    }
}

function closeShortcutsModal() {
    const modal = document.getElementById('viewer-shortcuts-modal');
    if (modal) {
        modal.classList.add('hidden');
        modal.classList.remove('flex');
    }
}

function setupSpotlightSearch() {
    const input = document.getElementById('spotlight-input');
    const list = document.getElementById('spotlight-results-list');
    if (!input || !list) return;

    input.addEventListener('input', (e) => {
        spotlightSelectedIdx = 0;
        renderSpotlightResults(e.target.value.trim());
    });

    input.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            if (currentSpotlightResults.length > 0) {
                spotlightSelectedIdx = (spotlightSelectedIdx + 1) % currentSpotlightResults.length;
                updateSpotlightSelection();
            }
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            if (currentSpotlightResults.length > 0) {
                spotlightSelectedIdx = (spotlightSelectedIdx - 1 + currentSpotlightResults.length) % currentSpotlightResults.length;
                updateSpotlightSelection();
            }
        } else if (e.key === 'Enter') {
            e.preventDefault();
            if (currentSpotlightResults.length > 0 && currentSpotlightResults[spotlightSelectedIdx]) {
                selectSpotlightItem(currentSpotlightResults[spotlightSelectedIdx]);
            }
        } else if (e.key === 'Escape') {
            closeSpotlight();
        }
    });

    list.addEventListener('click', (e) => {
        const itemEl = e.target.closest('[data-spotlight-idx]');
        if (!itemEl) return;
        const idx = parseInt(itemEl.dataset.spotlightIdx, 10);
        if (currentSpotlightResults[idx]) {
            selectSpotlightItem(currentSpotlightResults[idx]);
        }
    });
}

function renderSpotlightResults(query, forcedCategory = null) {
    const list = document.getElementById('spotlight-results-list');
    const netlist = AppState.boardviewNetlist;
    if (!list) return;

    if (!netlist || (!netlist.components && !netlist.nets)) {
        list.innerHTML = `<div class="text-[11px] text-gray-500 p-4 text-center font-mono">${escapeHtml(t('viewerSpotlightNoResults', 'Boardview is not loaded.'))}</div>`;
        currentSpotlightResults = [];
        return;
    }

    const results = [];

    if (forcedCategory === 'RAILS') {
        if (netlist.nets) {
            for (const [netName, nodes] of Object.entries(netlist.nets)) {
                const u = netName.toUpperCase();
                if (u.includes('+') || u.includes('VCC') || u.includes('VDD') || u.includes('VIN') || u.includes('19V') || u.includes('3V') || u.includes('5V') || u.includes('1V')) {
                    results.push({ type: 'net', netName: netName, count: Array.isArray(nodes) ? nodes.length : 0 });
                    if (results.length >= 25) break;
                }
            }
        }
    } else if (forcedCategory === 'ICS') {
        if (netlist.components) {
            for (const [refDes, comp] of Object.entries(netlist.components)) {
                const u = refDes.toUpperCase();
                if (u.startsWith('PU') || u.startsWith('U') || u.startsWith('UE') || u.startsWith('UA') || (comp.type && comp.type.includes('IC'))) {
                    results.push({ type: 'component', refDes: refDes, role: comp.type || 'IC', side: (comp.side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP' });
                    if (results.length >= 25) break;
                }
            }
        }
    } else if (forcedCategory === 'CONNECTORS') {
        if (netlist.components) {
            for (const [refDes, comp] of Object.entries(netlist.components)) {
                const u = refDes.toUpperCase();
                if (u.startsWith('J') || u.startsWith('CN') || (comp.type && comp.type.includes('Connector'))) {
                    results.push({ type: 'component', refDes: refDes, role: comp.type || 'Connector', side: (comp.side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP' });
                    if (results.length >= 25) break;
                }
            }
        }
    } else if (!query) {
        list.innerHTML = `<div class="text-[11px] text-gray-500 p-4 text-center font-mono">${escapeHtml(t('viewerSpotlightPlaceholder', 'Type component (PU401) or net (+3VALW)...'))}</div>`;
        currentSpotlightResults = [];
        return;
    } else {
        const q = query.toUpperCase();

        // 1. Search by components
        if (netlist.components) {
            for (const [refDes, comp] of Object.entries(netlist.components)) {
                if (refDes.toUpperCase().includes(q) || (comp.type && comp.type.toUpperCase().includes(q))) {
                    results.push({
                        type: 'component',
                        refDes: refDes,
                        role: comp.type || 'IC',
                        side: (comp.side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP'
                    });
                    if (results.length >= 10) break;
                }
            }
        }

        // 2. Search by nets
        if (netlist.nets) {
            for (const [netName, nodes] of Object.entries(netlist.nets)) {
                if (netName.toUpperCase().includes(q) && !netName.toUpperCase().startsWith('NC')) {
                    results.push({
                        type: 'net',
                        netName: netName,
                        count: Array.isArray(nodes) ? nodes.length : 0
                    });
                    if (results.length >= 15) break;
                }
            }
        }
    }

    currentSpotlightResults = results;

    if (results.length === 0) {
        list.innerHTML = `<div class="text-[11px] text-gray-500 p-4 text-center font-mono">${escapeHtml(t('viewerSpotlightNoResults', 'No results found.'))}</div>`;
        return;
    }

    list.innerHTML = results.map((item, idx) => {
        const isSelected = (idx === spotlightSelectedIdx);
        if (item.type === 'component') {
            return `
                <div data-spotlight-idx="${idx}" class="p-2.5 rounded-xl transition cursor-pointer flex items-center justify-between ${isSelected ? 'bg-cyan-500/20 border border-cyan-500/40 text-white' : 'hover:bg-white/5 text-gray-300'}">
                    <div class="flex items-center gap-2">
                        <span class="text-[10px] font-bold px-1.5 py-0.5 rounded bg-blue-500/20 text-blue-400 border border-blue-500/30 font-mono">${escapeHtml(t('spotlightCompBadge', 'PART'))}</span>
                        <strong class="text-xs font-mono font-bold">${escapeHtml(item.refDes)}</strong>
                        <span class="text-[10px] text-gray-400">${escapeHtml(item.role)}</span>
                    </div>
                    <span class="text-[10px] font-mono font-bold text-gray-400">${escapeHtml(formatBoardSide(item.side, { short: true }))}</span>
                </div>
            `;
        } else {
            return `
                <div data-spotlight-idx="${idx}" class="p-2.5 rounded-xl transition cursor-pointer flex items-center justify-between ${isSelected ? 'bg-cyan-500/20 border border-cyan-500/40 text-white' : 'hover:bg-white/5 text-gray-300'}">
                    <div class="flex items-center gap-2">
                        <span class="text-[10px] font-bold px-1.5 py-0.5 rounded bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 font-mono">${escapeHtml(t('spotlightNetBadge', 'NET'))}</span>
                        <strong class="text-xs font-mono font-bold text-cyan-300">${escapeHtml(item.netName)}</strong>
                    </div>
                    <span class="text-[10px] font-mono text-gray-400">${item.count} ${escapeHtml(t('colPinNumber', 'nodes'))}</span>
                </div>
            `;
        }
    }).join('');
}

function updateSpotlightSelection() {
    const items = document.querySelectorAll('#spotlight-results-list [data-spotlight-idx]');
    items.forEach((el, idx) => {
        const isSelected = (idx === spotlightSelectedIdx);
        el.classList.toggle('bg-cyan-500/20', isSelected);
        el.classList.toggle('border', isSelected);
        el.classList.toggle('border-cyan-500/40', isSelected);
        el.classList.toggle('text-white', isSelected);
        if (isSelected) el.scrollIntoView({ block: 'nearest' });
    });
}

function selectSpotlightItem(item) {
    closeSpotlight();
    if (!item) return;

    if (item.type === 'component') {
        const curSide = (AppState.activeSide || 'TOP').toUpperCase();
        if (item.side !== curSide) {
            DualSideController.toggleSide(item.side);
            setTimeout(() => {
                CameraNavigator.flyToComponent(item.refDes, engine);
            }, 250);
        } else {
            CameraNavigator.flyToComponent(item.refDes, engine);
        }
    } else if (item.type === 'net') {
        const summary = inspector.selectNet(item.netName, AppState.boardviewNetlist, AppState.activeSide);
        if (summary) {
            updateNetHud(summary);
            engine.render();
        }
    }
}

function setupKeyboardShortcuts() {
    window.addEventListener('keydown', (e) => {
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.isContentEditable) {
            if (e.key === 'Escape' && e.target.id === 'spotlight-input') {
                closeSpotlight();
            }
            return;
        }

        // Zen mode: hide/show entire UI [H]
        if (e.code === 'KeyH' && !e.ctrlKey && !e.metaKey && !e.altKey) {
            e.preventDefault();
            toggleZenMode();
            return;
        }

        if (e.code === 'Escape') {
            if (document.body.classList.contains('zen-mode')) {
                toggleZenMode();
                return;
            }
            closeShortcutsModal();
            if (inspector.selectedNet) {
                inspector.clearSelection();
                hideNetHud();
                engine.render();
            }
            return;
        }

        if (e.code === 'Slash' || ((e.ctrlKey || e.metaKey) && e.code === 'KeyF')) {
            e.preventDefault();
            openSpotlight();
            return;
        }

        if (e.key === '?' || (e.shiftKey && e.code === 'Slash') || e.code === 'F1') {
            e.preventDefault();
            openShortcutsModal();
            return;
        }

        if (e.code === 'Space') {
            e.preventDefault();
            DualSideController.toggleSide();
            return;
        }

        if (e.code === 'KeyR' && !e.ctrlKey && !e.metaKey) {
            e.preventDefault();
            rotateStandaloneCad(90);
            engine.render();
            return;
        }

        if (e.code === 'KeyF' && !e.ctrlKey && !e.metaKey) {
            e.preventDefault();
            flipStandaloneCad();
            engine.render();
            return;
        }

    });
}

function setupActions() {
    const clickHandlers = {
        toggleZenMode: () => toggleZenMode(),
        triggerBoardviewInput: () => document.getElementById('boardview-file-input')?.click(),

        toggleSpotlight: () => openSpotlight(),
        closeSpotlight: () => closeSpotlight(),

        toggleShortcutsModal: () => openShortcutsModal(),
        closeShortcutsModal: () => closeShortcutsModal(),

        spotlightQuickCategory: (el) => {
            const cat = el.dataset.category;
            renderSpotlightResults('', cat);
        },

        toggleBoardSide: () => DualSideController.toggleSide(),

        rotateStandaloneBoard: () => {
            rotateStandaloneCad(90);
            engine.render();
        },

        flipStandaloneBoard: () => {
            flipStandaloneCad();
            engine.render();
        },

        copyNetName: () => {
            if (inspector.selectedNet) {
                navigator.clipboard.writeText(inspector.selectedNet).then(() => {
                    showAppToast(t('toastNetCopied', 'Net name {net} copied to clipboard', { net: inspector.selectedNet }));
                });
            }
        },

        toggleNodeList: () => {
            const container = document.getElementById('hud-node-list-container');
            if (!container) return;
            isNodeListOpen = !isNodeListOpen;
            container.classList.toggle('hidden', !isNodeListOpen);
            container.classList.toggle('flex', isNodeListOpen);
            if (isNodeListOpen) renderNodeList();
        },

        flyToNetNode: (el) => {
            const ref = el.dataset.ref;
            const pin = el.dataset.pin;
            const side = el.dataset.side;
            if (!ref) return;

            const curSide = (AppState.activeSide || 'TOP').toUpperCase();
            if (side && side !== curSide) {
                DualSideController.toggleSide(side);
                setTimeout(() => CameraNavigator.flyToComponent(ref, engine, pin), 250);
            } else {
                CameraNavigator.flyToComponent(ref, engine, pin);
            }
        },

        clearNetSelection: () => {
            inspector.clearSelection();
            hideNetHud();
            engine.render();
        },

        filterNetRole: (el) => {
            inspector.setFilter(el.dataset.filter);
            updateNetHud(inspector.getSummary());
            engine.render();
        },

        flipToOppositeNet: () => {
            DualSideController.toggleSide();
        }
    };

    // 3-finger tap on touchscreen (for phones and tablets)
    window.addEventListener('touchstart', (e) => {
        if (e.touches.length === 3) {
            e.preventDefault();
            toggleZenMode();
        }
    }, { passive: false });

    document.body.addEventListener('click', (e) => {
        if (!e.target.closest('[data-action="toggleLangMenu"], #lang-menu')) {
            document.getElementById('lang-menu')?.classList.add('hidden');
        }

        if (e.target.id === 'viewer-spotlight-modal') {
            closeSpotlight();
        }

        if (e.target.id === 'viewer-shortcuts-modal') {
            closeShortcutsModal();
        }

        const actionEl = e.target.closest('[data-action]');
        if (!actionEl) return;
        const action = clickHandlers[actionEl.dataset.action];
        if (action) action(actionEl, e);
    });

    document.getElementById('boardview-file-input')?.addEventListener('change', async (e) => {
        const file = e.target.files?.[0];
        if (file) await loadBoardview(file);
        e.target.value = '';
    });

    initDesktopLifecycleStream();
}

function initDesktopLifecycleStream() {
    if (typeof EventSource === 'undefined') return;
    const source = new EventSource('/api/lifecycle/stream');
    window.addEventListener('beforeunload', () => source.close());
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initViewer);
else initViewer();