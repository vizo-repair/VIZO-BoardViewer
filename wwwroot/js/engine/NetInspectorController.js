// ============================================================================
// FILE: wwwroot/js/engine/NetInspectorController.js (VIZO BoardViewer CAD Inspector)
// Shunts to GND, Power Sources, Active Loads, Real Tracks, Ratsnest, X-Ray Ghost
// ============================================================================
import { AppState } from '../state/AppState.js';
import { CanvasStore } from '../state/CanvasStore.js';
import { transformPoint, resolvePinCadCoords } from './HomographySolver.js';
import {
    COMPONENT_KINDS,
    getComponentKind,
    isIcKind,
    isTransistorKind,
    isInductorKind,
    isConnectorKind,
    isNonShuntKind,
    isActiveComponentKind,
    isGroundNet
} from './ComponentKind.js';

const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
const MAX_RATSNEST_NODES = 45;
const MAX_GND_XRAY_NODES = 80;

export class NetInspectorController {
    constructor() {
        this.selectedNet = null;
        this.activeFilter = 'ALL';
        this.inspectedNodes = [];
        this.netTracks = [];
        this.oppositeNodesCount = 0;
        this.selectedRefDes = null;
        this.selectedPinNum = null;
    }

    clearSelection() {
        this.selectedNet = null;
        this.activeFilter = 'ALL';
        this.inspectedNodes = [];
        this.netTracks = [];
        this.oppositeNodesCount = 0;
        this.selectedRefDes = null;
        this.selectedPinNum = null;
        CanvasStore.clearInspectedNet();
    }

    syncCanvasStore() {
        if (!this.selectedNet) {
            CanvasStore.clearInspectedNet();
            return;
        }
        const componentRefs = new Set();
        const isShuntFilter = this.activeFilter === 'SHUNTS';
        const isSourceFilter = this.activeFilter === 'SOURCES';

        for (const node of this.inspectedNodes) {
            if (isShuntFilter && !this.isShuntNode(node.role)) continue;
            if (isSourceFilter && node.role !== 'SOURCE') continue;
            if (node.refDes) componentRefs.add(node.refDes.toUpperCase());
        }
        if (this.selectedRefDes) {
            componentRefs.add(this.selectedRefDes.toUpperCase());
        }

        CanvasStore.setInspectedNet({
            netName: this.selectedNet,
            isGnd: this.isGroundNet(this.selectedNet),
            activeFilter: this.activeFilter,
            componentRefs
        });
    }

    selectNet(netName, netlist, activeSide = 'TOP', originPin = null) {
        if (!netName || !netlist?.nets) {
            this.clearSelection();
            return null;
        }

        const cleanNet = netName.trim();
        const upperNet = cleanNet.toUpperCase();

        if (upperNet.startsWith('NC') || upperNet === 'UNCONNECTED') {
            this.clearSelection();
            return null;
        }

        let rawPins = netlist.nets[cleanNet];
        if (!rawPins) {
            const matchedKey = Object.keys(netlist.nets).find(k => k.equalsIgnoreCase(cleanNet));
            if (matchedKey) rawPins = netlist.nets[matchedKey];
        }

        if (!rawPins || !rawPins.length) {
            this.clearSelection();
            return null;
        }

        this.selectedNet = cleanNet;
        this.inspectedNodes = [];
        this.netTracks = [];
        this.oppositeNodesCount = 0;

        if (originPin) {
            this.selectedRefDes = originPin.refDes ? originPin.refDes.toUpperCase() : null;
            this.selectedPinNum = originPin.snappedPin != null ? String(originPin.snappedPin) : (originPin.pinNum != null ? String(originPin.pinNum) : null);
        } else {
            this.selectedRefDes = null;
            this.selectedPinNum = null;
        }

        const curSide = (activeSide || 'TOP').toUpperCase();
        const seenKeys = new Set();
        const isSelectedNetGnd = this.isGroundNet(cleanNet);

        // Build fast lookup of all pins connected to any GND net
        const gndPinsSet = new Set();
        if (netlist.nets) {
            for (const [nName, pins] of Object.entries(netlist.nets)) {
                if (this.isGroundNet(nName) && Array.isArray(pins)) {
                    for (const p of pins) {
                        gndPinsSet.add(p.toUpperCase().replace('-', '.'));
                    }
                }
            }
        }

        rawPins.forEach(pinRef => {
            const pinInfo = resolvePinCadCoords(netlist, pinRef);
            if (!pinInfo?.refDes) return;

            const nodeKey = `${pinInfo.refDes.toUpperCase()}.${pinInfo.pinNum}`;
            if (seenKeys.has(nodeKey)) return;
            seenKeys.add(nodeKey);

            const compSide = (pinInfo.side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP';
            const isOpposite = (compSide !== curSide);

            if (isOpposite) this.oppositeNodesCount++;

            const comp = netlist.components?.[pinInfo.refDes] ||
                Object.entries(netlist.components || {}).find(([k]) => k.equalsIgnoreCase(pinInfo.refDes))?.[1];

            const role = this.deduceNodeRole(pinInfo.refDes, comp, pinInfo.pinNum, cleanNet, gndPinsSet);

            this.inspectedNodes.push({
                refDes: pinInfo.refDes,
                pinNum: pinInfo.pinNum,
                cadX: pinInfo.cadX,
                cadY: pinInfo.cadY,
                side: compSide,
                isOpposite,
                role,
                isGnd: isSelectedNetGnd
            });
        });

        if (Array.isArray(netlist.testPoints)) {
            netlist.testPoints.forEach(tp => {
                if (!tp?.net || !tp.net.equalsIgnoreCase(cleanNet)) return;

                const tpName = (tp.nail || 'TP').toUpperCase();
                if (seenKeys.has(tpName) || seenKeys.has(`${tpName}.1`)) return;
                seenKeys.add(tpName);

                const tpSide = (tp.side || 'TOP').toUpperCase().startsWith('B') ? 'BOTTOM' : 'TOP';
                const isOpposite = (tpSide !== curSide);
                if (isOpposite) this.oppositeNodesCount++;

                this.inspectedNodes.push({
                    refDes: tp.nail || 'TP',
                    pinNum: '',
                    cadX: tp.x,
                    cadY: tp.y,
                    side: tpSide,
                    isOpposite,
                    role: tp.isVia ? 'VIA' : 'TESTPAD',
                    isGnd: isSelectedNetGnd
                });
            });
        }

        // Collect real copper traces for this net if present in Boardview
        if (!isSelectedNetGnd && Array.isArray(netlist.tracks) && netlist.tracks.length > 0) {
            for (const tr of netlist.tracks) {
                if (!tr?.net || !Array.isArray(tr.path) || tr.path.length < 2) continue;
                if (tr.net.trim().toUpperCase() === upperNet) {
                    this.netTracks.push(tr);
                }
            }
        }

        this.syncCanvasStore();
        return this.getSummary();
    }

    deduceNodeRole(refDes, comp, pinNum, netName, gndPinsSet = null) {
        const uRef = refDes.toUpperCase();
        const uNet = (netName || '').toUpperCase();
        const pinData = comp?.pins?.[pinNum];
        const pinName = (pinData?.name || pinNum || '').toUpperCase();

        // 1. Shunt to GND
        const isNetGnd = this.isGroundNet(uNet);
        const hasGndPin = !isNetGnd && this.hasGroundPin(uRef, comp, pinNum, gndPinsSet);

        const pinCount = comp?.pinsCount > 0 ? comp.pinsCount : Object.keys(comp?.pins || {}).length;
        const kind = getComponentKind(comp);
        const isConnector = kind !== COMPONENT_KINDS.UNKNOWN
            ? isConnectorKind(kind)
            : (uRef.startsWith('J') || uRef.startsWith('CN') || uRef.startsWith('CON') || uRef.startsWith('BAT'));
        const isNonShunt = kind !== COMPONENT_KINDS.UNKNOWN
            ? isNonShuntKind(kind)
            : isConnector;

        if (hasGndPin && pinCount === 2 && !isNonShunt) {
            return 'SHUNT_CAP';
        }

        // 2. Power / Signal Sources
        if (uRef.startsWith('JDC') || uRef.startsWith('DCIN') || uRef.startsWith('JBAT') || uRef.startsWith('EATXPWR') ||
            (isConnector &&
                (uNet.includes('VIN') || uNet.includes('VBUS') || uNet.includes('DC') || uNet.includes('BAT') || uNet.includes('12V') || uNet.includes('19V')))) {
            return 'SOURCE';
        }

        if (this.isInductor(uRef, comp)) {
            if (this.isInductorPowerSource(comp, pinNum, uNet)) {
                return 'SOURCE';
            }
        }

        if (this.isTransistor(uRef, comp)) {
            if (pinName !== '4' && pinName !== 'G' && !pinName.includes('GATE')) {
                if (this.isMosfetPowerSource(comp, pinNum, uNet)) {
                    return 'SOURCE';
                }
            }
        }

        if (this.isIc(uRef, comp)) {
            if (pinName.includes('OUT') || pinName.includes('VREG') || pinName.includes('LDO') ||
                pinName.includes('SW') || pinName.includes('LX') || pinName.includes('PHASE')) {
                return 'SOURCE';
            }
        }

        // 3. Active Load
        if (this.isIc(uRef, comp) || uRef.startsWith('PCH') || uRef.startsWith('CPU') || uRef.startsWith('GPU') || uRef.startsWith('SOC')) {
            return 'LOAD';
        }

        // 4. Other passives
        return 'PASSIVE';
    }

    hasGroundPin(uRef, comp, currentPinNum, gndPinsSet = null) {
        const curPinStr = String(currentPinNum || '').trim();

        if (comp?.pins) {
            for (const [pKey, pVal] of Object.entries(comp.pins)) {
                if (pKey === curPinStr || (curPinStr && pKey.equalsIgnoreCase(curPinStr))) continue;

                const otherNet = pVal?.net;
                if (otherNet && this.isGroundNet(otherNet)) {
                    return true;
                }

                if (gndPinsSet && (gndPinsSet.has(`${uRef}.${pKey.toUpperCase()}`) || gndPinsSet.has(`${uRef}.${pKey}`))) {
                    return true;
                }
            }
        }

        if (gndPinsSet) {
            const otherPinNum = curPinStr === '1' ? '2' : (curPinStr === '2' ? '1' : null);
            if (otherPinNum && gndPinsSet.has(`${uRef}.${otherPinNum}`)) {
                return true;
            }
        }

        return false;
    }

    isInductorPowerSource(comp, currentPinNum, currentNet) {
        if (!comp?.pins) return false;
        const curPinStr = String(currentPinNum);

        for (const [pKey, pVal] of Object.entries(comp.pins)) {
            if (pKey === curPinStr || pKey.equalsIgnoreCase(curPinStr)) continue;
            const otherNet = (pVal?.net || '').toUpperCase();

            if (otherNet.includes('PHASE') || otherNet.includes('LX') || otherNet.includes('SW') || otherNet.includes('UGATE')) {
                return true;
            }
            if (currentNet.includes('+') || currentNet.includes('VALW') || currentNet.includes('VSUS') || currentNet.includes('VCC')) {
                return true;
            }
        }
        return false;
    }

    isMosfetPowerSource(comp, currentPinNum, currentNet) {
        if (!comp?.pins) return false;
        const curPinStr = String(currentPinNum);

        for (const [pKey, pVal] of Object.entries(comp.pins)) {
            if (pKey === curPinStr || pKey.equalsIgnoreCase(curPinStr)) continue;
            const otherNet = (pVal?.net || '').toUpperCase();

            if (otherNet.includes('VIN') || otherNet.includes('19V') || otherNet.includes('B+') ||
                otherNet.includes('VBAT') || otherNet.includes('VBUS') || otherNet.includes('DC_IN')) {
                return true;
            }
        }
        return false;
    }

    isInductor(uRef, comp) {
        const kind = getComponentKind(comp);
        if (kind !== COMPONENT_KINDS.UNKNOWN) return isInductorKind(kind);
        return /^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9])?(?:FB|BLM|FL|PL|HL|VL|ML|SL|L)\d+/i.test(uRef);
    }

    isTransistor(uRef, comp) {
        const kind = getComponentKind(comp);
        if (kind !== COMPONENT_KINDS.UNKNOWN) return isTransistorKind(kind);
        return /^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z]{0,2})?(?:PQ|HQ|VQ|MQ|SQ|QA|QC|QE|QI|QV|FET|VT|Q)\d+/i.test(uRef);
    }

    isIc(uRef, comp) {
        const kind = getComponentKind(comp);
        if (kind !== COMPONENT_KINDS.UNKNOWN) return isIcKind(kind);
        if (/^FU\d+/i.test(uRef)) return false;
        const pinCount = comp?.pinsCount > 0 ? comp.pinsCount : Object.keys(comp?.pins || {}).length;
        if (pinCount > 0 && pinCount <= 2) return false;
        return /^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z]{0,2})?(?:PU|HU|VU|MU|SU|EU|UA|UE|UI|DAU|CU|IC|U)\d+/i.test(uRef);
    }

    isShuntNode(role) {
        return !!(role && role.startsWith('SHUNT'));
    }

    isGroundNet(netName) {
        return isGroundNet(netName);
    }

    getSummary() {
        if (!this.selectedNet) return null;

        const shuntsCount = this.inspectedNodes.filter(n => this.isShuntNode(n.role)).length;
        const sourcesCount = this.inspectedNodes.filter(n => n.role === 'SOURCE').length;
        const totalCount = this.inspectedNodes.length;

        const comp = this.selectedRefDes && (
            AppState.boardviewNetlist?.components?.[this.selectedRefDes] ||
            AppState.boardviewNetlist?.components?.[this.selectedRefDes.toUpperCase()] ||
            Object.entries(AppState.boardviewNetlist?.components || {}).find(([k]) => k.equalsIgnoreCase(this.selectedRefDes))?.[1]
        );
        const isTargetActive = this.isActiveComponent(this.selectedRefDes, comp);
        const isGnd = this.isGroundNet(this.selectedNet);

        return {
            netName: this.selectedNet,
            totalCount,
            shuntsCount,
            sourcesCount,
            oppositeNodesCount: this.oppositeNodesCount,
            activeFilter: this.activeFilter,
            isTargetActive,
            isGnd
        };
    }

    isActiveComponent(refDes, comp = null) {
        if (!refDes) return false;
        const kind = getComponentKind(comp);
        if (kind !== COMPONENT_KINDS.UNKNOWN) {
            return isActiveComponentKind(kind);
        }

        const u = refDes.toUpperCase();
        const pinCount = comp?.pinsCount > 0 ? comp.pinsCount : Object.keys(comp?.pins || {}).length;

        if (u.startsWith('VIA') || u.startsWith('TP') || u.startsWith('TEST') || u.startsWith('$') ||
            u.startsWith('FID') || u.startsWith('TB_TP') || /^M?H\d+$/.test(u) ||
            (pinCount <= 2 && /^P?T\d+[A-Z]?$/.test(u))) {
            return false;
        }

        if (u.startsWith('J') || u.startsWith('CN') || u.startsWith('CONN') || u.startsWith('USB')) {
            return false;
        }

        if (pinCount > 0 && pinCount <= 2) {
            return false;
        }

        return true;
    }

    setFilter(filter) {
        this.activeFilter = filter;
        this.syncCanvasStore();
    }

    renderNetOverlay(ctx, scale, toX, toY, matrix, requestRender = null) {
        if (!this.selectedNet || !this.inspectedNodes.length || !matrix) return;

        const curSide = (AppState.activeSide || 'TOP').toUpperCase();
        const isShuntFilter = (this.activeFilter === 'SHUNTS');
        const isSourceFilter = (this.activeFilter === 'SOURCES');
        const isGnd = this.isGroundNet(this.selectedNet);
        const now = Date.now();
        const pulse = Math.sin((now % 1000) / 1000 * Math.PI * 2);

        const dpr = window.devicePixelRatio || 1;
        const margin = 40 / scale;
        const viewLeft = -CanvasStore.panX / scale - margin;
        const viewRight = (ctx.canvas.width / dpr - CanvasStore.panX) / scale + margin;
        const viewTop = -CanvasStore.panY / scale - margin;
        const viewBottom = (ctx.canvas.height / dpr - CanvasStore.panY) / scale + margin;

        const filteredNodes = [];
        const sameSideProjected = [];
        const oppositeSideProjected = [];
        let originProjected = null;

        for (let i = 0; i < this.inspectedNodes.length; i++) {
            const node = this.inspectedNodes[i];
            if (isShuntFilter && !this.isShuntNode(node.role)) continue;
            if (isSourceFilter && node.role !== 'SOURCE') continue;

            const norm = transformPoint(matrix, node.cadX, node.cadY);
            const px = toX(norm.x);
            const py = toY(norm.y);
            const isClickedPin = !!(this.selectedRefDes &&
                node.refDes.toUpperCase() === this.selectedRefDes &&
                (!this.selectedPinNum || String(node.pinNum) === this.selectedPinNum));

            const proj = { node, px, py, isClickedPin };
            filteredNodes.push(proj);
            if (isClickedPin && !originProjected) originProjected = proj;

            if (node.side === curSide) sameSideProjected.push(proj);
            else oppositeSideProjected.push(proj);
        }

        ctx.save();
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';

        // 1. Real copper tracks or Ratsnest airwires for non-GND nets
        if (!isGnd) {
            if (this.netTracks.length > 0) {
                this.renderRealNetTracks(ctx, scale, toX, toY, matrix, curSide);
            } else if (filteredNodes.length >= 2 && filteredNodes.length <= MAX_RATSNEST_NODES) {
                this.renderRatsnestAirwires(ctx, scale, filteredNodes, originProjected, curSide);
            }
        }

        // 2. X-Ray Ghost nodes on the opposite side of the board
        const shouldDrawXray = oppositeSideProjected.length > 0 &&
            !(isGnd && this.activeFilter === 'ALL' && oppositeSideProjected.length > MAX_GND_XRAY_NODES);

        if (shouldDrawXray) {
            const showGhostLabels = !isGnd && oppositeSideProjected.length <= 24 && scale >= 3.5;
            ctx.shadowBlur = 0;

            for (const item of oppositeSideProjected) {
                const { node, px, py } = item;
                if (px < viewLeft || px > viewRight || py < viewTop || py > viewBottom) continue;

                const isShunt = this.isShuntNode(node.role);
                const isVia = (node.role === 'VIA');
                let ringR = (isVia ? 5.5 : 7.4) / scale;
                if (isShuntFilter && isShunt) ringR = (8.5 + pulse * 1.4) / scale;

                const ringColor = isShunt ? 'rgba(232, 121, 249, 0.88)' : 'rgba(192, 132, 252, 0.82)';

                ctx.fillStyle = isShunt ? 'rgba(217, 70, 239, 0.16)' : 'rgba(168, 85, 247, 0.15)';
                ctx.beginPath();
                ctx.arc(px, py, ringR, 0, Math.PI * 2);
                ctx.fill();

                ctx.strokeStyle = ringColor;
                ctx.lineWidth = 1.6 / scale;
                ctx.setLineDash([3.2 / scale, 2.5 / scale]);
                ctx.beginPath();
                ctx.arc(px, py, ringR, 0, Math.PI * 2);
                ctx.stroke();
                ctx.setLineDash([]);

                ctx.fillStyle = 'rgba(233, 213, 255, 0.88)';
                ctx.beginPath();
                ctx.arc(px, py, 1.8 / scale, 0, Math.PI * 2);
                ctx.fill();

                if (showGhostLabels && node.refDes) {
                    const fs = 8.0 / scale;
                    ctx.font = `bold ${fs}px ${FONT_FAMILY}`;
                    ctx.textAlign = 'center';
                    ctx.textBaseline = 'bottom';
                    ctx.fillStyle = 'rgba(233, 213, 255, 0.82)';
                    const lbl = node.pinNum ? `${node.refDes}.${node.pinNum}` : node.refDes;
                    ctx.fillText(lbl, px, py - ringR - (2.0 / scale));
                }
            }
        }

        // 3. Nodes on the active side
        const useHeavyGlow = sameSideProjected.length <= 120;

        for (const item of sameSideProjected) {
            const { node, px, py, isClickedPin } = item;
            if (px < viewLeft || px > viewRight || py < viewTop || py > viewBottom) continue;

            const isShunt = this.isShuntNode(node.role);
            const isSource = (node.role === 'SOURCE');
            const isVia = (node.role === 'VIA');

            // Selected pin (gold target)
            if (isClickedPin) {
                const ringRadius = (11.0 + pulse * 2.0) / scale;

                ctx.shadowBlur = 0;
                ctx.fillStyle = '#07090e';
                ctx.beginPath();
                ctx.arc(px, py, ringRadius + (1.5 / scale), 0, Math.PI * 2);
                ctx.fill();

                ctx.strokeStyle = '#facc15';
                ctx.lineWidth = 2.4 / scale;
                ctx.shadowColor = '#facc15';
                ctx.shadowBlur = 18 / scale;
                ctx.beginPath();
                ctx.arc(px, py, ringRadius, 0, Math.PI * 2);
                ctx.stroke();

                ctx.fillStyle = '#facc15';
                ctx.beginPath();
                ctx.arc(px, py, 6.5 / scale, 0, Math.PI * 2);
                ctx.fill();

                ctx.shadowBlur = 0;
                ctx.fillStyle = '#ffffff';
                ctx.beginPath();
                ctx.arc(px, py, 2.5 / scale, 0, Math.PI * 2);
                ctx.fill();

                continue;
            }

            // Remaining net nodes (Shunts = red, Sources = green, Default = cyan)
            const glowColor = isShunt ? '#ef4444' : (isSource ? '#22c55e' : '#00e5ff');
            let radius = (isVia ? 5.5 : 7.0) / scale;

            if (isShuntFilter && isShunt) {
                radius = (8.5 + pulse * 1.5) / scale;
            }

            ctx.shadowBlur = 0;
            ctx.fillStyle = '#07090e';
            ctx.beginPath();
            ctx.arc(px, py, radius + (1.0 / scale), 0, Math.PI * 2);
            ctx.fill();

            if (useHeavyGlow) {
                ctx.shadowColor = glowColor;
                ctx.shadowBlur = (isShuntFilter && isShunt ? 18 : 12) / scale;
            } else {
                ctx.shadowBlur = 0;
            }
            ctx.fillStyle = glowColor;
            ctx.beginPath();
            ctx.arc(px, py, radius, 0, Math.PI * 2);
            ctx.fill();

            ctx.shadowBlur = 0;
            ctx.fillStyle = '#ffffff';
            ctx.beginPath();
            ctx.arc(px, py, 2.0 / scale, 0, Math.PI * 2);
            ctx.fill();
        }
        ctx.restore();

        if (requestRender) {
            requestAnimationFrame(() => requestRender());
        }
    }

    renderRealNetTracks(ctx, scale, toX, toY, matrix, curSide) {
        ctx.save();
        ctx.shadowBlur = 0;

        for (const tr of this.netTracks) {
            const layerRaw = String(tr.layer || curSide).toUpperCase();
            const isTopLayer = layerRaw.startsWith('T') || layerRaw === 'L1' || layerRaw === '1';
            const isBotLayer = layerRaw.startsWith('B');
            const isSameSide = (curSide === 'TOP' && isTopLayer) || (curSide === 'BOTTOM' && isBotLayer) || layerRaw === 'BOTH';
            const isOppositeSide = (curSide === 'TOP' && isBotLayer) || (curSide === 'BOTTOM' && isTopLayer);

            ctx.beginPath();
            const first = transformPoint(matrix, tr.path[0].x, tr.path[0].y);
            ctx.moveTo(toX(first.x), toY(first.y));
            for (let i = 1; i < tr.path.length; i++) {
                const pt = transformPoint(matrix, tr.path[i].x, tr.path[i].y);
                ctx.lineTo(toX(pt.x), toY(pt.y));
            }

            if (isSameSide) {
                ctx.setLineDash([]);
                ctx.strokeStyle = 'rgba(34, 211, 238, 0.55)';
                ctx.lineWidth = 2.6 / scale;
                ctx.stroke();

                ctx.strokeStyle = 'rgba(165, 243, 252, 0.85)';
                ctx.lineWidth = 1.1 / scale;
                ctx.stroke();
            } else if (isOppositeSide) {
                ctx.setLineDash([4.0 / scale, 3.0 / scale]);
                ctx.strokeStyle = 'rgba(192, 132, 252, 0.45)';
                ctx.lineWidth = 1.6 / scale;
                ctx.stroke();
                ctx.setLineDash([]);
            } else {
                // Inner PCB copper layer
                ctx.setLineDash([2.0 / scale, 3.0 / scale]);
                ctx.strokeStyle = 'rgba(129, 140, 248, 0.45)';
                ctx.lineWidth = 1.4 / scale;
                ctx.stroke();
                ctx.setLineDash([]);
            }
        }

        ctx.restore();
    }

    renderRatsnestAirwires(ctx, scale, projectedNodes, originProjected, curSide) {
        const edges = [];

        if (originProjected) {
            // Star topology from the clicked pin to every node on the net
            for (const target of projectedNodes) {
                if (target === originProjected) continue;
                edges.push([originProjected, target]);
            }
        } else {
            // Minimum Spanning Tree (Prim's algorithm) when selected from search
            const n = projectedNodes.length;
            const visited = new Uint8Array(n);
            const minDist = new Float64Array(n).fill(Infinity);
            const parent = new Int32Array(n).fill(-1);
            minDist[0] = 0;

            for (let step = 0; step < n; step++) {
                let u = -1;
                let best = Infinity;
                for (let i = 0; i < n; i++) {
                    if (!visited[i] && minDist[i] < best) {
                        best = minDist[i];
                        u = i;
                    }
                }
                if (u === -1) break;
                visited[u] = 1;
                if (parent[u] !== -1) {
                    edges.push([projectedNodes[parent[u]], projectedNodes[u]]);
                }
                const pu = projectedNodes[u];
                for (let v = 0; v < n; v++) {
                    if (visited[v]) continue;
                    const pv = projectedNodes[v];
                    const d = Math.hypot(pv.px - pu.px, pv.py - pu.py);
                    if (d < minDist[v]) {
                        minDist[v] = d;
                        parent[v] = u;
                    }
                }
            }
        }

        ctx.save();
        ctx.shadowBlur = 0;

        for (const [a, b] of edges) {
            const bothSameSide = (a.node.side === curSide && b.node.side === curSide);
            const isTargetShunt = this.isShuntNode(b.node.role) || this.isShuntNode(a.node.role);
            const isTargetSource = (b.node.role === 'SOURCE' || a.node.role === 'SOURCE');

            if (bothSameSide) {
                ctx.setLineDash([]);
                ctx.strokeStyle = isTargetSource
                    ? 'rgba(74, 222, 128, 0.42)'
                    : (isTargetShunt ? 'rgba(248, 113, 113, 0.36)' : 'rgba(34, 211, 238, 0.36)');
                ctx.lineWidth = 1.25 / scale;
            } else {
                ctx.setLineDash([4.0 / scale, 3.5 / scale]);
                ctx.strokeStyle = 'rgba(192, 132, 252, 0.36)';
                ctx.lineWidth = 1.1 / scale;
            }

            ctx.beginPath();
            ctx.moveTo(a.px, a.py);
            ctx.lineTo(b.px, b.py);
            ctx.stroke();
        }

        ctx.setLineDash([]);
        ctx.restore();
    }
}