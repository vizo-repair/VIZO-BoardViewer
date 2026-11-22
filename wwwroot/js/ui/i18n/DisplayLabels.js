// ============================================================================
// FILE: wwwroot/js/ui/i18n/DisplayLabels.js
// Human-readable display labels for board sides, corners, and 19 component kinds
// ============================================================================
import { t } from './I18nStore.js';

const ROLE_LABELS = {
    'test point': ['roleTestPoint', 'Test Point'],
    testpoint: ['roleTestPoint', 'Test Point'],
    testpad: ['roleTestPoint', 'Test Point'],
    via: ['roleVia', 'Via'],
    resistor: ['roleResistor', 'Resistor'],
    capacitor: ['roleCapacitor', 'Capacitor'],
    capacitor_polarized: ['roleCapacitorPol', 'Polarized Capacitor'],
    inductor: ['roleInductor', 'Inductor'],
    transformer: ['roleTransformer', 'Transformer'],
    diode: ['roleDiode', 'Diode'],
    led: ['roleLed', 'LED / Indicator'],
    crystal: ['roleCrystal', 'Crystal / Oscillator'],
    fuse: ['roleFuse', 'Fuse'],
    jumper: ['roleJumper', 'Jumper'],
    switch: ['roleSwitch', 'Switch / Button'],
    battery: ['roleBattery', 'Battery'],
    transistor: ['roleMosfet', 'MOSFET'],
    transistor_mosfet: ['roleMosfet', 'MOSFET'],
    ic: ['roleIc', 'IC'],
    chipset_pch: ['roleIc', 'IC'],
    processor_apu: ['roleIc', 'IC'],
    audio_codec: ['roleIc', 'IC'],
    gpu_video_ic: ['roleIc', 'IC'],
    embedded_controller_ec: ['roleIc', 'IC'],
    optocoupler: ['roleOptocoupler', 'Optocoupler'],
    speaker_connector: ['roleConnector', 'Connector'],
    hdmi_connector: ['roleConnector', 'Connector'],
    display_connector: ['roleConnector', 'Connector'],
    dc_in_power_jack: ['roleConnector', 'Connector'],
    battery_connector: ['roleConnector', 'Connector'],
    usb_connector: ['roleConnector', 'Connector'],
    ethernet_connector: ['roleConnector', 'Connector'],
    poweric: ['rolePwmIc', 'PWM / Power IC'],
    pwm_power_ic: ['rolePwmIc', 'PWM / Power IC'],
    connector: ['roleConnector', 'Connector'],
    mechanical: ['roleMechanical', 'Mechanical'],
    unknown: ['roleUnknown', 'Component']
};

export function formatComponentRole(role) {
    const raw = String(role || '').trim();
    if (!raw) return '';
    const entry = ROLE_LABELS[raw.toLowerCase()];
    return entry ? t(entry[0], entry[1]) : raw;
}

export function formatBoardSide(side, { short = false } = {}) {
    const s = String(side || 'TOP').toUpperCase();
    const isBottom = s === 'BOTTOM' || s === 'BOT';
    if (short) return isBottom ? 'BOT' : 'TOP';
    return isBottom ? 'BOTTOM' : 'TOP';
}

export function formatCorner(code) {
    return String(code || 'TL').toUpperCase();
}
