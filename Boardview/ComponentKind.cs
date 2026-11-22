// ============================================================================
// FILE: Boardview/ComponentKind.cs (VIZO BoardViewer)
// Unified canonical dictionary of 19 electronic component classes
// ============================================================================
namespace VIZO.BoardViewer.Boardview;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComponentKind
{
    Unknown = 0,
    Resistor = 1,
    Capacitor = 2,
    Inductor = 3,
    Transformer = 4,
    Diode = 5,
    Led = 6,
    Transistor = 7,
    Ic = 8,
    PowerIc = 9,
    Optocoupler = 10,
    Crystal = 11,
    Fuse = 12,
    Connector = 13,
    Switch = 14,
    Jumper = 15,
    Battery = 16,
    TestPoint = 17,
    Mechanical = 18
}

public static class ComponentKinds
{
    private static readonly Dictionary<string, ComponentKind> ParseMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 19 canonical values
        ["Unknown"] = ComponentKind.Unknown,
        ["Resistor"] = ComponentKind.Resistor,
        ["Capacitor"] = ComponentKind.Capacitor,
        ["Inductor"] = ComponentKind.Inductor,
        ["Transformer"] = ComponentKind.Transformer,
        ["Diode"] = ComponentKind.Diode,
        ["Led"] = ComponentKind.Led,
        ["Transistor"] = ComponentKind.Transistor,
        ["Ic"] = ComponentKind.Ic,
        ["PowerIc"] = ComponentKind.PowerIc,
        ["Optocoupler"] = ComponentKind.Optocoupler,
        ["Crystal"] = ComponentKind.Crystal,
        ["Fuse"] = ComponentKind.Fuse,
        ["Connector"] = ComponentKind.Connector,
        ["Switch"] = ComponentKind.Switch,
        ["Jumper"] = ComponentKind.Jumper,
        ["Battery"] = ComponentKind.Battery,
        ["TestPoint"] = ComponentKind.TestPoint,
        ["Mechanical"] = ComponentKind.Mechanical,

        // Backward compatibility with legacy parser / role deducer labels
        ["Resistor_Or_Array"] = ComponentKind.Resistor,
        ["Capacitor_Polarized"] = ComponentKind.Capacitor,
        ["Inductor_Ferrite"] = ComponentKind.Inductor,
        ["Diode_Zener_TVS"] = ComponentKind.Diode,
        ["Transistor_MOSFET"] = ComponentKind.Transistor,
        ["MOSFET"] = ComponentKind.Transistor,
        ["IC_Active"] = ComponentKind.Ic,
        ["Chipset_PCH"] = ComponentKind.Ic,
        ["Processor_APU"] = ComponentKind.Ic,
        ["Embedded_Controller_EC"] = ComponentKind.Ic,
        ["Audio_Codec"] = ComponentKind.Ic,
        ["GPU_Video_IC"] = ComponentKind.Ic,
        ["PWM_Power_IC"] = ComponentKind.PowerIc,
        ["Crystal_Oscillator"] = ComponentKind.Crystal,
        ["Fuse_PTC"] = ComponentKind.Fuse,
        ["Connector_Switch"] = ComponentKind.Connector,
        ["Speaker_Connector"] = ComponentKind.Connector,
        ["HDMI_Connector"] = ComponentKind.Connector,
        ["Display_Connector"] = ComponentKind.Connector,
        ["DC_IN_Power_Jack"] = ComponentKind.Connector,
        ["Battery_Connector"] = ComponentKind.Connector,
        ["USB_Connector"] = ComponentKind.Connector,
        ["Ethernet_Connector"] = ComponentKind.Connector,
        ["TestPoint_Jumper"] = ComponentKind.TestPoint,
        ["Mechanical_Mount"] = ComponentKind.Mechanical,
        ["Other_Or_Unknown"] = ComponentKind.Unknown,
        ["SMD"] = ComponentKind.Unknown,
        ["TH"] = ComponentKind.Unknown,
        ["OTHER"] = ComponentKind.Unknown,
        ["PART"] = ComponentKind.Unknown,
        ["COMP"] = ComponentKind.Unknown
    };

    public static string ToCanonicalName(this ComponentKind kind) => kind switch
    {
        ComponentKind.Resistor => "Resistor",
        ComponentKind.Capacitor => "Capacitor",
        ComponentKind.Inductor => "Inductor",
        ComponentKind.Transformer => "Transformer",
        ComponentKind.Diode => "Diode",
        ComponentKind.Led => "Led",
        ComponentKind.Transistor => "Transistor",
        ComponentKind.Ic => "Ic",
        ComponentKind.PowerIc => "PowerIc",
        ComponentKind.Optocoupler => "Optocoupler",
        ComponentKind.Crystal => "Crystal",
        ComponentKind.Fuse => "Fuse",
        ComponentKind.Connector => "Connector",
        ComponentKind.Switch => "Switch",
        ComponentKind.Jumper => "Jumper",
        ComponentKind.Battery => "Battery",
        ComponentKind.TestPoint => "TestPoint",
        ComponentKind.Mechanical => "Mechanical",
        _ => "Unknown"
    };

    public static bool TryParse(string? raw, out ComponentKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            kind = ComponentKind.Unknown;
            return false;
        }

        string trimmed = raw.Trim();
        if (ParseMap.TryGetValue(trimmed, out kind))
            return true;

        if (trimmed.Contains("Connector", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Jack", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Header", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Socket", StringComparison.OrdinalIgnoreCase))
        {
            kind = ComponentKind.Connector;
            return true;
        }

        if (trimmed.Contains("Transistor", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("MOSFET", StringComparison.OrdinalIgnoreCase))
        {
            kind = ComponentKind.Transistor;
            return true;
        }

        if (trimmed.Contains("PWM", StringComparison.OrdinalIgnoreCase))
        {
            kind = ComponentKind.PowerIc;
            return true;
        }

        if (trimmed.EndsWith("_IC", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("IC_", StringComparison.OrdinalIgnoreCase))
        {
            kind = ComponentKind.Ic;
            return true;
        }

        kind = ComponentKind.Unknown;
        return false;
    }

    public static bool IsCanonicalName(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && ParseMap.TryGetValue(raw.Trim(), out var k)
        && k != ComponentKind.Unknown
        && string.Equals(raw.Trim(), k.ToCanonicalName(), StringComparison.Ordinal);

    public static bool IsPassive(ComponentKind kind) => kind is
        ComponentKind.Resistor or
        ComponentKind.Capacitor or
        ComponentKind.Inductor or
        ComponentKind.Fuse or
        ComponentKind.Mechanical or
        ComponentKind.TestPoint;

    public static bool IsDriver(ComponentKind kind) => kind is
        ComponentKind.Ic or
        ComponentKind.PowerIc;

    public static bool IsSwitch(ComponentKind kind) => kind == ComponentKind.Transistor;

    public static bool IsSeriesBridge(ComponentKind kind) => kind is
        ComponentKind.Resistor or
        ComponentKind.Inductor or
        ComponentKind.Transformer or
        ComponentKind.Fuse or
        ComponentKind.Diode or
        ComponentKind.Jumper;

    public static bool IsEndpoint(ComponentKind kind) => kind is
        ComponentKind.Connector or
        ComponentKind.Crystal or
        ComponentKind.Jumper or
        ComponentKind.Switch or
        ComponentKind.Battery;

    public static bool IsExcludedFromCleanNetlist(ComponentKind kind) => kind is
        ComponentKind.Mechanical or
        ComponentKind.TestPoint or
        ComponentKind.Capacitor;

    public static bool IsExcludedFromLandmarks(ComponentKind kind) => kind is
        ComponentKind.Mechanical or
        ComponentKind.TestPoint or
        ComponentKind.Jumper or
        ComponentKind.Connector or
        ComponentKind.Switch or
        ComponentKind.Battery or
        ComponentKind.Led or
        ComponentKind.Fuse;
}

