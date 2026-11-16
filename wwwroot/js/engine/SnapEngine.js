// ============================================================================
// FILE: wwwroot/js/engine/SnapEngine.js (VIZO 2.0 - Strict Component Priority)
// ============================================================================
import { AppState } from '../state/AppState.js';
import { transformPoint } from './HomographySolver.js';
import { getEffectiveMatrix } from './BoardCorners.js';

export const SNAP_RADIUS_PIN_PX = 40;
export const SNAP_RADIUS_ARROW_PX = 96;

function isVirtualShortPin(refDes, comp) {
    if (!refDes) return false;
    const u = refDes.toUpperCase();
    if (u.startsWith('SHORTPIN') || u.startsWith('SLN') || u.startsWith('SLP') || u.startsWith('NBP_SHORTPIN') || u.startsWith('NET_SHORT')) {
        return true;
    }
    const desc = (comp?.description || comp?.partNumber || comp?.type || '').toUpperCase();
    return desc.includes('SHORTPIN') || desc.includes('SHORT_PIN') || desc.includes('NET_TIE');
}

function matchSide(itemSide, activeSide) {
    const s = String(itemSide || 'TOP').toUpperCase();
    if (s === 'BOTH' || s === 'ALL') return true;
    const norm = s.startsWith('B') ? 'BOTTOM' : 'TOP';
    return norm === activeSide;
}

export function findNearestCadComponent(normX, normY, maxScreenSnapPx = 40, imgRect, scale, options = {}) {
    const netlist = AppState.boardviewNetlist;
    const currentSide = (AppState.activeSide || "TOP").toUpperCase().includes("BOT") ? "BOTTOM" : "TOP";
    const matrix = getEffectiveMatrix(netlist, currentSide, AppState.getActiveMatrix());

    if (!netlist?.components || !matrix) {
        return null;
    }

    const renderW = Math.max(1, imgRect?.renderWidth || 1);
    const renderH = Math.max(1, imgRect?.renderHeight || 1);
    const zoom = Number.isFinite(scale) && scale > 0 ? scale : 1;
    const allowBody = options.allowBody !== false;

    let bestPinMatch = null;
    let minPinDist = Infinity;

    let bestBodyMatch = null;
    let minBodyDist = Infinity;

    let bestShortMatch = null;
    let minShortDist = Infinity;

    const screenDist = (pt) => Math.hypot((pt.x - normX) * renderW * zoom, (pt.y - normY) * renderH * zoom);

    for (const [refDes, comp] of Object.entries(netlist.components)) {
        if (!comp) continue;
        if (!matchSide(comp.side, currentSide)) continue;

        const isShort = isVirtualShortPin(refDes, comp);
        const compValue = comp.value || null;
        const compDesc = comp.description || null;
        const compPart = comp.partNumber || null;
        const compPkg = comp.package || null;

        // 1. Search for contact (pin/ball)
        if (comp.pins && typeof comp.pins === 'object') {
            for (const [pinNum, pinData] of Object.entries(comp.pins)) {
                if (!pinData) continue;
                if (!matchSide(pinData.layer || comp.side, currentSide)) continue;

                const pinPos = transformPoint(matrix, pinData.x, pinData.y);
                const distScreen = screenDist(pinPos);

                if (distScreen <= maxScreenSnapPx) {
                    if (!isShort && distScreen < minPinDist) {
                        minPinDist = distScreen;
                        // Collect list of all component pins for multi-line card
                        const allPinsList = [];
                        if (comp.pins) {
                            for (const [pKey, pVal] of Object.entries(comp.pins)) {
                                allPinsList.push({
                                    pin: pKey,
                                    name: pVal.name || pKey,
                                    net: pVal.net || 'UNCONNECTED'
                                });
                            }
                        }

                        bestPinMatch = {
                            refDes,
                            type: comp.type || "IC",
                            side: comp.side || currentSide,
                            snappedPin: pinNum,
                            pinName: pinData.name || pinNum,
                            net: pinData.net || '',
                            snappedX: pinPos.x,
                            snappedY: pinPos.y,
                            isPinSnapped: true,
                            isTestPoint: false,
                            value: compValue,
                            description: compDesc,
                            partNumber: compPart,
                            package: compPkg,
                            allPins: allPinsList
                        };
                    } else if (isShort && distScreen < minShortDist) {
                        minShortDist = distScreen;
                        bestShortMatch = {
                            refDes,
                            type: comp.type || "IC",
                            side: comp.side || currentSide,
                            snappedPin: pinNum,
                            pinName: pinData.name || pinNum,
                            net: pinData.net || '',
                            snappedX: pinPos.x,
                            snappedY: pinPos.y,
                            isPinSnapped: true,
                            isTestPoint: false,
                            value: compValue,
                            description: compDesc,
                            partNumber: compPart,
                            package: compPkg
                        };
                    }
                }
            }
        }

        // 2. Search for component body (stored separately; pins are never overridden by body)
        if (!allowBody || typeof comp.x !== 'number' || typeof comp.y !== 'number') continue;

        const center = transformPoint(matrix, comp.x, comp.y);
        const distCenter = screenDist(center);
        const inBody = isInsideComponentBody(matrix, comp, normX, normY);
        const bodyRadius = maxScreenSnapPx * (options.bodyRadiusScale || 1.5);

        if (inBody || distCenter <= bodyRadius) {
            const score = inBody ? distCenter - 5 : distCenter;
            if (!isShort && score < minBodyDist) {
                minBodyDist = score;
                bestBodyMatch = {
                    refDes,
                    type: comp.type || "Component",
                    side: comp.side || currentSide,
                    snappedPin: null,
                    pinName: null,
                    net: null,
                    snappedX: center.x,
                    snappedY: center.y,
                    isPinSnapped: false,
                    isTestPoint: false,
                    value: compValue,
                    description: compDesc,
                    partNumber: compPart,
                    package: compPkg
                };
            }
        }
    }

    if (Array.isArray(netlist.testPoints)) {
        for (const tp of netlist.testPoints) {
            if (!tp || typeof tp.x !== 'number' || typeof tp.y !== 'number') continue;
            if (!matchSide(tp.side, currentSide)) continue;

            const tpPos = transformPoint(matrix, tp.x, tp.y);
            const distScreen = screenDist(tpPos);
            const score = distScreen - 6;

            if (distScreen <= maxScreenSnapPx && distScreen < minPinDist) {
                minPinDist = distScreen;
                const nailName = tp.nail || (tp.isVia ? 'VIA' : 'TP');
                bestPinMatch = {
                    refDes: nailName,
                    type: tp.isVia ? 'VIA' : 'TESTPAD',
                    side: tp.side || currentSide,
                    snappedPin: null,
                    pinName: null,
                    net: tp.net && !tp.net.toUpperCase().startsWith('NC') ? tp.net : '',
                    snappedX: tpPos.x,
                    snappedY: tpPos.y,
                    isPinSnapped: true,
                    isTestPoint: true,
                    isVia: !!tp.isVia,
                    value: tp.probeLabel || null,
                    description: tp.isVia ? 'Via' : 'Test Point',
                    partNumber: null,
                    package: null
                };
            }
        }
    }
    const bestRealMatch = bestPinMatch || bestBodyMatch;
    if (bestRealMatch) {
        const alternates = [];
        for (const [refDes, comp] of Object.entries(netlist.components)) {
            if (refDes === bestRealMatch.refDes || isVirtualShortPin(refDes, comp)) continue;
            if (!matchSide(comp.side, currentSide)) continue;

            const distCenter = screenDist(transformPoint(matrix, comp.x, comp.y));
            if (distCenter <= 12) {
                alternates.push(refDes);
            }
        }

        if (alternates.length > 0) {
            bestRealMatch.alternateRefs = alternates;
            bestRealMatch.displayRefDes = `${bestRealMatch.refDes} / ${alternates.join(' / ')}`;
        } else {
            bestRealMatch.displayRefDes = bestRealMatch.refDes;
        }

        return bestRealMatch;
    }
    return bestShortMatch;
}

export function findNearestCadForArrow(normX, normY, imgRect, scale) {
    return findNearestCadComponent(normX, normY, SNAP_RADIUS_ARROW_PX, imgRect, scale, {
        allowBody: true,
        bodyRadiusScale: 1.6
    });
}

function isInsideComponentBody(matrix, comp, normX, normY) {
    const w = Number(comp.width) || Math.abs((comp.maxX ?? 0) - (comp.minX ?? 0));
    const h = Number(comp.height) || Math.abs((comp.maxY ?? 0) - (comp.minY ?? 0));
    if (!(w > 0) || !(h > 0)) return false;
    const minX = (comp.maxX > comp.minX) ? comp.minX : comp.x - w / 2;
    const maxX = (comp.maxX > comp.minX) ? comp.maxX : comp.x + w / 2;
    const minY = (comp.maxY > comp.minY) ? comp.minY : comp.y - h / 2;
    const maxY = (comp.maxY > comp.minY) ? comp.maxY : comp.y + h / 2;
    const corners = [
        transformPoint(matrix, minX, minY),
        transformPoint(matrix, maxX, minY),
        transformPoint(matrix, minX, maxY),
        transformPoint(matrix, maxX, maxY)
    ];
    const left = Math.min(...corners.map(p => p.x));
    const right = Math.max(...corners.map(p => p.x));
    const top = Math.min(...corners.map(p => p.y));
    const bottom = Math.max(...corners.map(p => p.y));
    return normX >= left && normX <= right && normY >= top && normY <= bottom;
}