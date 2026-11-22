// ============================================================================
// FILE: wwwroot/js/engine/ComponentKind.js (VIZO BoardViewer)
// Unified canonical mapping of 19 BoardView component classes & net classifier
// ============================================================================

export const COMPONENT_KINDS = Object.freeze({
    UNKNOWN: 'Unknown',
    RESISTOR: 'Resistor',
    CAPACITOR: 'Capacitor',
    INDUCTOR: 'Inductor',
    TRANSFORMER: 'Transformer',
    DIODE: 'Diode',
    LED: 'Led',
    TRANSISTOR: 'Transistor',
    IC: 'Ic',
    POWER_IC: 'PowerIc',
    OPTOCOUPLER: 'Optocoupler',
    CRYSTAL: 'Crystal',
    FUSE: 'Fuse',
    CONNECTOR: 'Connector',
    SWITCH: 'Switch',
    JUMPER: 'Jumper',
    BATTERY: 'Battery',
    TEST_POINT: 'TestPoint',
    MECHANICAL: 'Mechanical'
});

const CANONICAL_MAP = new Map([
    ['unknown', COMPONENT_KINDS.UNKNOWN],
    ['resistor', COMPONENT_KINDS.RESISTOR],
    ['resistor_or_array', COMPONENT_KINDS.RESISTOR],
    ['capacitor', COMPONENT_KINDS.CAPACITOR],
    ['capacitor_polarized', COMPONENT_KINDS.CAPACITOR],
    ['inductor', COMPONENT_KINDS.INDUCTOR],
    ['inductor_ferrite', COMPONENT_KINDS.INDUCTOR],
    ['transformer', COMPONENT_KINDS.TRANSFORMER],
    ['diode', COMPONENT_KINDS.DIODE],
    ['diode_zener_tvs', COMPONENT_KINDS.DIODE],
    ['led', COMPONENT_KINDS.LED],
    ['transistor', COMPONENT_KINDS.TRANSISTOR],
    ['transistor_mosfet', COMPONENT_KINDS.TRANSISTOR],
    ['mosfet', COMPONENT_KINDS.TRANSISTOR],
    ['ic', COMPONENT_KINDS.IC],
    ['ic_active', COMPONENT_KINDS.IC],
    ['chipset_pch', COMPONENT_KINDS.IC],
    ['processor_apu', COMPONENT_KINDS.IC],
    ['embedded_controller_ec', COMPONENT_KINDS.IC],
    ['audio_codec', COMPONENT_KINDS.IC],
    ['gpu_video_ic', COMPONENT_KINDS.IC],
    ['poweric', COMPONENT_KINDS.POWER_IC],
    ['pwm_power_ic', COMPONENT_KINDS.POWER_IC],
    ['optocoupler', COMPONENT_KINDS.OPTOCOUPLER],
    ['crystal', COMPONENT_KINDS.CRYSTAL],
    ['crystal_oscillator', COMPONENT_KINDS.CRYSTAL],
    ['fuse', COMPONENT_KINDS.FUSE],
    ['fuse_ptc', COMPONENT_KINDS.FUSE],
    ['connector', COMPONENT_KINDS.CONNECTOR],
    ['connector_switch', COMPONENT_KINDS.CONNECTOR],
    ['speaker_connector', COMPONENT_KINDS.CONNECTOR],
    ['hdmi_connector', COMPONENT_KINDS.CONNECTOR],
    ['display_connector', COMPONENT_KINDS.CONNECTOR],
    ['dc_in_power_jack', COMPONENT_KINDS.CONNECTOR],
    ['battery_connector', COMPONENT_KINDS.CONNECTOR],
    ['usb_connector', COMPONENT_KINDS.CONNECTOR],
    ['ethernet_connector', COMPONENT_KINDS.CONNECTOR],
    ['switch', COMPONENT_KINDS.SWITCH],
    ['jumper', COMPONENT_KINDS.JUMPER],
    ['battery', COMPONENT_KINDS.BATTERY],
    ['testpoint', COMPONENT_KINDS.TEST_POINT],
    ['testpoint_jumper', COMPONENT_KINDS.TEST_POINT],
    ['mechanical', COMPONENT_KINDS.MECHANICAL],
    ['mechanical_mount', COMPONENT_KINDS.MECHANICAL],
    ['smd', COMPONENT_KINDS.UNKNOWN],
    ['th', COMPONENT_KINDS.UNKNOWN],
    ['other', COMPONENT_KINDS.UNKNOWN],
    ['other_or_unknown', COMPONENT_KINDS.UNKNOWN]
]);

export function normalizeComponentKind(rawType) {
    if (!rawType || typeof rawType !== 'string') return COMPONENT_KINDS.UNKNOWN;
    const key = rawType.trim().toLowerCase();
    if (!key) return COMPONENT_KINDS.UNKNOWN;

    const exact = CANONICAL_MAP.get(key);
    if (exact) return exact;

    if (key.includes('connector') || key.includes('jack') || key.includes('socket') || key.includes('header')) {
        return COMPONENT_KINDS.CONNECTOR;
    }
    if (key.includes('transistor') || key.includes('mosfet')) {
        return COMPONENT_KINDS.TRANSISTOR;
    }
    if (key.includes('pwm')) {
        return COMPONENT_KINDS.POWER_IC;
    }
    if (key.endsWith('_ic') || key.startsWith('ic_') || key.includes('controller') || key.includes('codec') || key.includes('processor') || key.includes('chipset')) {
        return COMPONENT_KINDS.IC;
    }
    return COMPONENT_KINDS.UNKNOWN;
}

export function getComponentKind(comp) {
    return normalizeComponentKind(comp?.type || comp?.role || '');
}

export function isIcKind(kind) {
    return kind === COMPONENT_KINDS.IC || kind === COMPONENT_KINDS.POWER_IC;
}

export function isTransistorKind(kind) {
    return kind === COMPONENT_KINDS.TRANSISTOR;
}

export function isInductorKind(kind) {
    return kind === COMPONENT_KINDS.INDUCTOR || kind === COMPONENT_KINDS.TRANSFORMER;
}

export function isConnectorKind(kind) {
    return kind === COMPONENT_KINDS.CONNECTOR;
}

export function isMechanicalKind(kind) {
    return kind === COMPONENT_KINDS.MECHANICAL;
}

export function isTestPointKind(kind) {
    return kind === COMPONENT_KINDS.TEST_POINT;
}

export function isNonShuntKind(kind) {
    return kind === COMPONENT_KINDS.CONNECTOR
        || kind === COMPONENT_KINDS.BATTERY
        || kind === COMPONENT_KINDS.SWITCH
        || kind === COMPONENT_KINDS.JUMPER
        || kind === COMPONENT_KINDS.IC
        || kind === COMPONENT_KINDS.POWER_IC
        || kind === COMPONENT_KINDS.TRANSISTOR
        || kind === COMPONENT_KINDS.TRANSFORMER
        || kind === COMPONENT_KINDS.OPTOCOUPLER;
}

export function isPassiveKind(kind) {
    return kind === COMPONENT_KINDS.RESISTOR
        || kind === COMPONENT_KINDS.CAPACITOR
        || kind === COMPONENT_KINDS.INDUCTOR
        || kind === COMPONENT_KINDS.FUSE
        || kind === COMPONENT_KINDS.MECHANICAL
        || kind === COMPONENT_KINDS.TEST_POINT;
}

export function isActiveComponentKind(kind) {
    return kind === COMPONENT_KINDS.IC
        || kind === COMPONENT_KINDS.POWER_IC
        || kind === COMPONENT_KINDS.TRANSISTOR
        || kind === COMPONENT_KINDS.DIODE
        || kind === COMPONENT_KINDS.OPTOCOUPLER
        || kind === COMPONENT_KINDS.CRYSTAL
        || kind === COMPONENT_KINDS.TRANSFORMER;
}

const DEFAULT_KIND_STYLE = Object.freeze({
    fg: 'rgba(140, 152, 168, 0.42)',
    fill: 'rgba(255, 255, 255, 0.012)',
    text: '#bac4d0',
    watermark: 'rgba(186, 196, 208, 0.24)',
    width: 0.85,
    dashed: false,
    isKey: false,
    hideText: false
});

export const COMPONENT_KIND_STYLES = Object.freeze({
    [COMPONENT_KINDS.POWER_IC]: Object.freeze({
        fg: 'rgba(234, 138, 62, 0.78)',
        fill: 'rgba(234, 138, 62, 0.045)',
        text: '#f5b982',
        watermark: 'rgba(245, 185, 130, 0.30)',
        width: 1.3,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.IC]: Object.freeze({
        fg: 'rgba(226, 188, 68, 0.72)',
        fill: 'rgba(226, 188, 68, 0.035)',
        text: '#efe098',
        watermark: 'rgba(239, 224, 152, 0.28)',
        width: 1.25,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.TRANSISTOR]: Object.freeze({
        fg: 'rgba(88, 174, 218, 0.68)',
        fill: 'rgba(88, 174, 218, 0.04)',
        text: '#9bd2ec',
        watermark: 'rgba(155, 210, 236, 0.28)',
        width: 1.15,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.INDUCTOR]: Object.freeze({
        fg: 'rgba(152, 138, 212, 0.64)',
        fill: 'rgba(152, 138, 212, 0.04)',
        text: '#c2b6ea',
        watermark: 'rgba(194, 182, 234, 0.28)',
        width: 1.15,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.TRANSFORMER]: Object.freeze({
        fg: 'rgba(152, 138, 212, 0.70)',
        fill: 'rgba(152, 138, 212, 0.05)',
        text: '#c2b6ea',
        watermark: 'rgba(194, 182, 234, 0.30)',
        width: 1.25,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.DIODE]: Object.freeze({
        fg: 'rgba(214, 122, 166, 0.64)',
        fill: 'rgba(214, 122, 166, 0.04)',
        text: '#e8aac8',
        watermark: 'rgba(232, 170, 200, 0.28)',
        width: 1.1,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.OPTOCOUPLER]: Object.freeze({
        fg: 'rgba(214, 122, 166, 0.70)',
        fill: 'rgba(214, 122, 166, 0.05)',
        text: '#e8aac8',
        watermark: 'rgba(232, 170, 200, 0.30)',
        width: 1.2,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.FUSE]: Object.freeze({
        fg: 'rgba(239, 88, 88, 0.86)',
        fill: 'rgba(239, 68, 68, 0.075)',
        text: '#fca5a5',
        watermark: 'rgba(252, 165, 165, 0.36)',
        width: 1.35,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.CONNECTOR]: Object.freeze({
        fg: 'rgba(74, 186, 124, 0.64)',
        fill: 'rgba(74, 186, 124, 0.035)',
        text: '#98ddb4',
        watermark: 'rgba(152, 221, 180, 0.26)',
        width: 1.15,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.SWITCH]: Object.freeze({
        fg: 'rgba(74, 186, 124, 0.68)',
        fill: 'rgba(74, 186, 124, 0.045)',
        text: '#98ddb4',
        watermark: 'rgba(152, 221, 180, 0.28)',
        width: 1.15,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.BATTERY]: Object.freeze({
        fg: 'rgba(74, 186, 124, 0.68)',
        fill: 'rgba(74, 186, 124, 0.045)',
        text: '#98ddb4',
        watermark: 'rgba(152, 221, 180, 0.26)',
        width: 1.2,
        dashed: false,
        isKey: true,
        hideText: false
    }),
    [COMPONENT_KINDS.CRYSTAL]: Object.freeze({
        fg: 'rgba(76, 186, 174, 0.64)',
        fill: 'rgba(76, 186, 174, 0.045)',
        text: '#9be2da',
        watermark: 'rgba(155, 226, 218, 0.28)',
        width: 1.15,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.LED]: Object.freeze({
        fg: 'rgba(154, 204, 86, 0.64)',
        fill: 'rgba(154, 204, 86, 0.045)',
        text: '#cde8a4',
        watermark: 'rgba(205, 232, 164, 0.28)',
        width: 1.1,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.JUMPER]: Object.freeze({
        fg: 'rgba(212, 170, 62, 0.62)',
        fill: 'rgba(212, 170, 62, 0.03)',
        text: '#e6ca7a',
        watermark: 'rgba(230, 202, 122, 0.28)',
        width: 1.05,
        dashed: true,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.CAPACITOR]: Object.freeze({
        fg: 'rgba(122, 148, 176, 0.44)',
        fill: 'rgba(122, 148, 176, 0.018)',
        text: '#b6c4d4',
        watermark: 'rgba(182, 196, 212, 0.24)',
        width: 0.85,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.RESISTOR]: Object.freeze({
        fg: 'rgba(148, 144, 138, 0.40)',
        fill: 'rgba(148, 144, 138, 0.015)',
        text: '#c2beb8',
        watermark: 'rgba(194, 190, 184, 0.22)',
        width: 0.85,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.TEST_POINT]: Object.freeze({
        fg: 'rgba(186, 132, 210, 0.48)',
        fill: 'rgba(168, 85, 247, 0.04)',
        text: '#d8b4e8',
        watermark: 'rgba(216, 180, 232, 0.24)',
        width: 0.85,
        dashed: false,
        isKey: false,
        hideText: false
    }),
    [COMPONENT_KINDS.MECHANICAL]: Object.freeze({
        fg: 'rgba(100, 116, 139, 0.22)',
        fill: null,
        text: null,
        watermark: null,
        width: 0.7,
        dashed: false,
        isKey: false,
        hideText: true
    }),
    [COMPONENT_KINDS.UNKNOWN]: DEFAULT_KIND_STYLE
});

export function getComponentKindStyle(kind) {
    return COMPONENT_KIND_STYLES[kind] || DEFAULT_KIND_STYLE;
}

export function isNcNet(netName) {
    if (!netName || typeof netName !== 'string') return true;
    const u = netName.trim().toUpperCase();
    if (!u) return true;
    return /^(?:NC|N\/C|N\.C\.?|UNCONNECTED|DUMMY|NO_?NET|UNUSED|OPEN)(?:[_\-\s0-9]|$)/.test(u);
}

export function isGroundNet(netName) {
    if (!netName || typeof netName !== 'string') return false;
    const u = netName.toUpperCase().trim();
    if (u === '0' || u === '$0' || u === '0V' || u === 'GROUND' || u === 'GND' || u === 'EARTH') return true;

    const tokens = u.split(/[_/\-. +]/);
    return tokens.some(tok =>
        tok === 'GND' || tok === 'GROUND' || tok === 'AGND' || tok === 'PGND' ||
        tok === 'DGND' || tok === 'SGND' || tok === 'CGND' ||
        tok.startsWith('VSS') || tok.endsWith('GND')
    );
}

export function isPowerRailNet(netName) {
    if (!netName || typeof netName !== 'string') return false;
    if (isNcNet(netName) || isGroundNet(netName)) return false;
    const u = netName.toUpperCase().trim();
    return u.startsWith('+') || u.startsWith('PP') || u === 'B+' ||
        /(?:^|[_/\-\s])(?:VCC|VDD|VIN|VBUS|VBAT|VSYS|VREG|VALW|VSUS|VCORE|VGFX|VPP|VTT|DCIN|19V|12V|5V|3V3|3V|1V8|1V5|1V2|1V0|0V\d)/.test(u);
}

export function classifyNetKind(netName) {
    if (isNcNet(netName)) return 'NC';
    if (isGroundNet(netName)) return 'GND';
    if (isPowerRailNet(netName)) return 'POWER';
    return 'SIGNAL';
}

