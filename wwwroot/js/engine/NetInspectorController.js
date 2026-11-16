// ============================================================================
// FILE: wwwroot/js/engine/NetInspectorController.js (VIZO 2.0 CAD Inspector)
// Physically accurate analysis: Shunts to GND, Power Sources, Load
// ============================================================================
import { AppState } from '../state/AppState.js';
import { transformPoint, resolvePinCadCoords } from './HomographySolver.js';
import { formatBoardSide } from '../ui/i18n/DisplayLabels.js';
import { t } from '../ui/i18n/I18nStore.js';

const FONT_FAMILY = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

export class NetInspectorController {
    constructor() {
        this.selectedNet = null;
        this.activeFilter = 'ALL';
        this.inspectedNodes = [];
        this.oppositeNodesCount = 0;
        this.selectedRefDes = null;
        this.selectedPinNum = null;
    }

    clearSelection() {
        this.selectedNet = null;
        this.activeFilter = 'ALL';
        this.inspectedNodes = [];
        this.oppositeNodesCount = 0;
        this.selectedRefDes = null;
        this.selectedPinNum = null;
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

        // Build a fast Set of all board pins connected to any ground net
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

            // Physical node role calculation checking platform prefixes and second pin to GND
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

        return this.getSummary();
    }

    /**
     * Circuit-level role deduction for a pin
     */
    deduceNodeRole(refDes, comp, pinNum, netName, gndPinsSet = null) {
        const uRef = refDes.toUpperCase();
        const uNet = (netName || '').toUpperCase();
        const pinData = comp?.pins?.[pinNum];
        const pinName = (pinData?.name || pinNum || '').toUpperCase();

        // 1. TRUE SHUNT TO GROUND (GND)
        // If the selected net itself is ground, there can be no shunts
        const isNetGnd = this.isGroundNet(uNet);
        const hasGndPin = !isNetGnd && this.hasGroundPin(uRef, comp, pinNum, gndPinsSet);

        const pinCount = comp?.pinsCount > 0 ? comp.pinsCount : Object.keys(comp?.pins || {}).length;
        const isConnector = (comp?.type && /connector|socket|jack|header/i.test(comp.type)) ||
            uRef.startsWith('J') || uRef.startsWith('CN') || uRef.startsWith('CON') || uRef.startsWith('BAT');

        // Exactly 2 pins, not a connector, and the other end is on GND
        if (hasGndPin && pinCount === 2 && !isConnector) {
            return 'SHUNT_CAP';
        }

        // 2. POWER AND SIGNAL SOURCES (SOURCES)
        // External power, battery, and Type-C connectors
        if (uRef.startsWith('JDC') || uRef.startsWith('DCIN') || uRef.startsWith('JBAT') ||
            ((uRef.startsWith('J') || uRef.startsWith('CN')) &&
                (uNet.includes('VIN') || uNet.includes('VBUS') || uNet.includes('DC') || uNet.includes('BAT')))) {
            return 'SOURCE';
        }

        // Power inductors (PL, L, HL):
        if (this.isInductor(uRef, comp)) {
            if (this.isInductorPowerSource(comp, pinNum, uNet)) {
                return 'SOURCE';
            }
        }

        // Power MOSFETs (PQ, Q, HQ): source or drain switching power
        if (this.isTransistor(uRef, comp)) {
            if (pinName !== '4' && pinName !== 'G' && !pinName.includes('GATE')) {
                if (this.isMosfetPowerSource(comp, pinNum, uNet)) {
                    return 'SOURCE';
                }
            }
        }

        // PWM and LDO ICs (PU, U, HU): output voltage pins
        if (this.isIc(uRef, comp)) {
            if (pinName.includes('OUT') || pinName.includes('VREG') || pinName.includes('LDO') ||
                pinName.includes('SW') || pinName.includes('LX') || pinName.includes('PHASE')) {
                return 'SOURCE';
            }
        }

        // 3. ACTIVE LOAD (LOAD)
        if (this.isIc(uRef, comp) || uRef.startsWith('PCH') || uRef.startsWith('CPU') || uRef.startsWith('GPU') || uRef.startsWith('SOC')) {
            return 'LOAD';
        }

        // 4. Other pass-through passive components
        return 'PASSIVE';
    }

    /**
     * Checks whether ANY OTHER pin of this component is connected to a GND net
     */
    hasGroundPin(uRef, comp, currentPinNum, gndPinsSet = null) {
        const curPinStr = String(currentPinNum || '').trim();

        // 1. Check via comp.pins dictionary
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

        // 2. Fallback check for 2-pin parts using the board net registry
        if (gndPinsSet) {
            const otherPinNum = curPinStr === '1' ? '2' : (curPinStr === '2' ? '1' : null);
            if (otherPinNum && gndPinsSet.has(`${uRef}.${otherPinNum}`)) {
                return true;
            }
        }

        return false;
    }

    /**
     * Checks whether the inductor forms this power rail (PWM phase on the other side)
     */
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

    /**
     * Checks whether the transistor switches power from a primary rail
     */
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

    // Universal regexes supporting platform prefixes (H, P, V, M, S, A, T, E, B)


    isInductor(uRef, comp) {
        if (comp?.type && /inductor|choke|coil/i.test(comp.type)) return true;
        // Supports: L1, PL1, HL1, FL1, FB1, BLM1...
        return /^(?:[A-Z]{0,2})?(?:PL|HL|VL|ML|SL|FL|FB|BL|L)\d+/i.test(uRef);
    }

    isTransistor(uRef, comp) {
        if (comp?.type && /transistor|mosfet|fet/i.test(comp.type)) return true;
        // Supports: Q1, PQ1, HQ1, VQ1, MQ1, QA1, VT1...
        return /^(?:[A-Z]{0,2})?(?:PQ|HQ|VQ|MQ|SQ|QA|FET|VT|Q)\d+/i.test(uRef);
    }

    isIc(uRef, comp) {
        if (comp?.type && /ic|controller|pwm|codec|processor/i.test(comp.type)) return true;
        // Supports: U1, PU1, HU1, VU1, MU1, EU1, UA1, UE1, UI1...
        return /^(?:[A-Z]{0,2})?(?:PU|HU|VU|MU|SU|EU|UA|UE|UI|IC|U)\d+/i.test(uRef);
    }

    isShuntNode(role) {
        return !!(role && role.startsWith('SHUNT'));
    }

    isGroundNet(netName) {
        if (!netName) return false;
        const u = netName.toUpperCase().trim();
        // Exact match for Allegro / OrCAD / CST / GenCAD nets
        if (u === '0' || u === '$0' || u === '0V' || u === 'GROUND' || u === 'GND' || u === 'EARTH') return true;

        const tokens = u.split(/[_/\-. +]/);
        return tokens.some(t =>
            t === 'GND' || t === 'GROUND' || t === 'AGND' || t === 'PGND' ||
            t === 'DGND' || t === 'SGND' || t === 'CGND' ||
            t.startsWith('VSS') || t.endsWith('GND')
        );
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
        const u = refDes.toUpperCase();

        if (u.startsWith('VIA') || u.startsWith('TP') || u.startsWith('TEST') || u.startsWith('$') ||
            u.startsWith('FID') || /^M?H\d+$/.test(u)) {
            return false;
        }

        if (u.startsWith('J') || u.startsWith('CN') || u.startsWith('CONN') || u.startsWith('USB')) {
            return false;
        }

        const role = (comp?.type || comp?.role || '').toUpperCase();
        if (role.includes('RESISTOR') || role.includes('CAPACITOR') ||
            role.includes('INDUCTOR') || role.includes('FUSE') ||
            role.includes('MECHANICAL') || role.includes('JUMPER')) {
            return false;
        }

        return true;
    }

    setFilter(filter) {
        this.activeFilter = filter;
    }

    renderNetOverlay(ctx, scale, toX, toY, matrix, requestRender = null) {
        if (!this.selectedNet || !this.inspectedNodes.length || !matrix) return;

        const curSide = (AppState.activeSide || 'TOP').toUpperCase();
        const isShuntFilter = (this.activeFilter === 'SHUNTS');
        const now = Date.now();
        const pulse = Math.sin((now % 1000) / 1000 * Math.PI * 2);

        ctx.save();
        this.inspectedNodes.forEach(node => {
            // Filter strictly by deduced role
            if (isShuntFilter && !this.isShuntNode(node.role)) return;
            if (this.activeFilter === 'SOURCES' && node.role !== 'SOURCE') return;
            if (node.side !== curSide) return;

            const norm = transformPoint(matrix, node.cadX, node.cadY);
            const px = toX(norm.x);
            const py = toY(norm.y);

            const isClickedPin = this.selectedRefDes &&
                node.refDes.toUpperCase() === this.selectedRefDes &&
                (!this.selectedPinNum || String(node.pinNum) === this.selectedPinNum);

            const isShunt = this.isShuntNode(node.role);
            const isSource = (node.role === 'SOURCE');
            const isVia = (node.role === 'VIA');

            // SELECTED PIN UNDER CURSOR (GOLD RETICLE)
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

                return;
            }

            // OTHER NET NODES (Shunts to ground = RED, Sources = GREEN)
            let glowColor = isShunt ? '#ef4444' : (isSource ? '#22c55e' : '#00e5ff');
            let radius = (isVia ? 5.5 : 7.0) / scale;

            if (isShuntFilter && isShunt) {
                radius = (8.5 + pulse * 1.5) / scale;
            }

            ctx.shadowBlur = 0;
            ctx.fillStyle = '#07090e';
            ctx.beginPath();
            ctx.arc(px, py, radius + (1.0 / scale), 0, Math.PI * 2);
            ctx.fill();

            ctx.shadowColor = glowColor;
            ctx.shadowBlur = (isShuntFilter && isShunt ? 18 : 12) / scale;
            ctx.fillStyle = glowColor;
            ctx.beginPath();
            ctx.arc(px, py, radius, 0, Math.PI * 2);
            ctx.fill();

            ctx.shadowBlur = 0;
            ctx.fillStyle = '#ffffff';
            ctx.beginPath();
            ctx.arc(px, py, 2.0 / scale, 0, Math.PI * 2);
            ctx.fill();
        });
        ctx.restore();

        if (requestRender) {
            requestAnimationFrame(() => requestRender());
        }
    }
}