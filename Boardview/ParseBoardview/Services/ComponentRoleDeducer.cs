// ============================================================================
// FILE: Boardview/ParseBoardview/Services/ComponentRoleDeducer.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

using System;
using System.Text.RegularExpressions;

public class ComponentRoleDeducer
{
    private static readonly Regex CompositeTpRegex = new(
        @"^(?:[A-Z0-9]{1,6}_)?(?:[A-Z0-9]{0,2})?TP\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShortTpRegex = new(
        @"^P?T\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SinglePinTpRegex = new(
        @"^(?:TV|IT)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MechHoleRegex = new(
        @"^(?:MH|FID)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SinglePinMechRegex = new(
        @"^(?:H|HICT|FD|SH)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExcludedCapIcRegex = new(
        @"^(?:MEC\d{4}|NPCE\d{3}|SPCE\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PolarizedCapSuffixRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z])?(?:EC|CE)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BypassCapSuffixRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z])?(?:BC|CX)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SubsysCapRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9])C\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnownSubsysCapRegex = new(
        @"^(?:O|W|C)C\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TwoPinCapFamilyRegex = new(
        @"^(?:C[AEFILV]|[A-Z]C)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ResistorArrayRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z])?R[NP][A-Z]?\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SubsysResRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9])R\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnownSubsysResRegex = new(
        @"^[OSABWVPX]R\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TwoPinResFamilyRegex = new(
        @"^R[ACDEFHILV]\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TwoPinInductorRegex = new(
        @"^(?:FB|FL|BLM|L[ACELV])\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ThreePinDiodeRegex = new(
        @"^(?:LED|ZD|TVS|D[AETV])\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PolyfuseRegex = new(
        @"^(?:FU|PTC)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CrystalRegex = new(
        @"^(?:Y[CLV]?|XTAL|OSC)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompositeIcRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z]{1,3})U\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExcludedChargerIcRegex = new(
        @"^BQ\d{4,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompositeFetRegex = new(
        @"^(?:[A-Z0-9]{1,4}_|[0-9][A-Z]|[A-Z][0-9]|[A-Z]{1,2})Q\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SubsysFetRegex = new(
        @"^Q[ACEIV]\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConnectorRegex = new(
        @"^CON(?:N)?\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrictPassiveRefDesRegex = new(
        @"^(?:PC|TC|PL|PF|PR|R|C|L|F)\d{1,5}[A-Z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string DeduceRole(string refDes, string compType)
        => DeduceRole(refDes, compType, pinsCount: 0);

    public string DeduceRole(string refDes, string compType, int pinsCount)
    {
        if (string.IsNullOrWhiteSpace(refDes))
            return string.IsNullOrWhiteSpace(compType) ? "IC" : compType;

        string u = refDes.Trim().ToUpperInvariant();
        int dot = u.IndexOf('.');
        string strictTarget = dot > 0 ? u[..dot] : u;

        // Mechanical assemblies, heatsinks, brackets, shields
        if (u.StartsWith("HEATSINK") || u.StartsWith("BRACKET") || u.StartsWith("FRAME") ||
            u.StartsWith("SHIELD") || u.StartsWith("STANDOFF") || u.StartsWith("STUDFIX"))
            return "Mechanical";

        if (u.StartsWith("PCH") || u.StartsWith("CHIPSET")) return "Chipset_PCH";
        if (u.StartsWith("CPU") || u.StartsWith("GPU") || u.StartsWith("SOC") || u.StartsWith("UAPU") || u.StartsWith("UCPU")) return "Processor_APU";
        if (u.StartsWith("PU")) return "PWM_Power_IC";
        if (u.StartsWith("UA")) return "Audio_Codec";
        if (u.StartsWith("UV")) return "GPU_Video_IC";
        if (u.StartsWith("UE") || u.StartsWith("KBC") || u == "EC" || (u.StartsWith("EC") && pinsCount >= 8))
            return "Embedded_Controller_EC";
        if (u.StartsWith("U") || (u.Length > 2 && u.StartsWith("IC") && char.IsDigit(u[2]))) return "IC";
        if (u.StartsWith("PQ") || u.StartsWith("QA") || (u.Length > 1 && u[0] == 'Q' && char.IsDigit(u[1]))) return "Transistor_MOSFET";

        // Optocouplers (PC817, PC201, PC123, OP, OK, ISO)
        if (u.StartsWith("PC") && (u.StartsWith("PC817") || u.StartsWith("PC123") || u.StartsWith("PC201") || u.StartsWith("PC301") || u.StartsWith("PC101") || u.StartsWith("PC401")))
            return "Optocoupler";
        if (u.StartsWith("OP") || u.StartsWith("OK") || u.StartsWith("ISO")) return "Optocoupler";

        if (pinsCount <= 2 && (u.StartsWith("PC") || u.StartsWith("TC")) && StrictPassiveRefDesRegex.IsMatch(strictTarget)) return "Capacitor_Polarized";
        if (u.StartsWith("JSPK")) return "Speaker_Connector";
        if (u.StartsWith("JHDMI")) return "HDMI_Connector";
        if (u.StartsWith("JEDP") || u.StartsWith("JLVDS")) return "Display_Connector";
        if (u.StartsWith("JDCIN") || u.StartsWith("JDC")) return "DC_IN_Power_Jack";
        if (u.StartsWith("JBAT")) return "Battery_Connector";
        if (u.StartsWith("JUSB")) return "USB_Connector";
        if (u.StartsWith("JLAN") || u.StartsWith("RJ")) return "Ethernet_Connector";
        if (u.StartsWith("J") || u.StartsWith("CN") || ConnectorRegex.IsMatch(u)) return "Connector";
        if (u.StartsWith("PJ") || u.StartsWith("PJP")) return "Jumper";

        // Strict passive prefixes: single-letter prefix must be followed by a digit
        if (u.StartsWith("PD") || (u.Length > 1 && u[0] == 'D' && char.IsDigit(u[1]))) return "Diode";
        if ((u.StartsWith("PL") || (u.Length > 1 && u[0] == 'L' && char.IsDigit(u[1]))) && StrictPassiveRefDesRegex.IsMatch(strictTarget)) return "Inductor";
        if ((u.StartsWith("PF") || (u.Length > 1 && u[0] == 'F' && char.IsDigit(u[1]))) && StrictPassiveRefDesRegex.IsMatch(strictTarget)) return "Fuse";
        if ((u.StartsWith("PR") || (u.Length > 1 && u[0] == 'R' && char.IsDigit(u[1]))) && StrictPassiveRefDesRegex.IsMatch(strictTarget)) return "Resistor";
        if (u.Length > 1 && u[0] == 'C' && char.IsDigit(u[1]) && StrictPassiveRefDesRegex.IsMatch(strictTarget)) return "Capacitor";

        // Composite Asus (.fz) and Gigabyte (.tvw) prefixes aware of PinsCount
        if (pinsCount <= 2)
        {
            if (CompositeTpRegex.IsMatch(u) || ShortTpRegex.IsMatch(u) || (pinsCount == 1 && SinglePinTpRegex.IsMatch(u)))
                return "TestPoint";
        }

        if ((pinsCount <= 1 && MechHoleRegex.IsMatch(u)) || (pinsCount == 1 && SinglePinMechRegex.IsMatch(u)))
            return "Mechanical";

        if (pinsCount <= 2 && PolyfuseRegex.IsMatch(u))
            return "Fuse";

        if (pinsCount <= 2 && !ExcludedCapIcRegex.IsMatch(u))
        {
            if (PolarizedCapSuffixRegex.IsMatch(u))
                return "Capacitor_Polarized";
            if (BypassCapSuffixRegex.IsMatch(u) || SubsysCapRegex.IsMatch(u) || KnownSubsysCapRegex.IsMatch(u) ||
                (pinsCount > 0 && TwoPinCapFamilyRegex.IsMatch(u)))
                return "Capacitor";
        }

        if ((pinsCount <= 16 && ResistorArrayRegex.IsMatch(u)) ||
            (pinsCount <= 2 && (SubsysResRegex.IsMatch(u) || KnownSubsysResRegex.IsMatch(u) || (pinsCount > 0 && TwoPinResFamilyRegex.IsMatch(u)))))
            return "Resistor";

        if (pinsCount > 0 && pinsCount <= 2 && TwoPinInductorRegex.IsMatch(u))
            return "Inductor";

        if (pinsCount > 0 && pinsCount <= 3 && ThreePinDiodeRegex.IsMatch(u))
            return "Diode";

        if (pinsCount <= 6 && CrystalRegex.IsMatch(u))
            return "Crystal";

        if ((pinsCount == 0 || pinsCount >= 3) && !PolyfuseRegex.IsMatch(u) && CompositeIcRegex.IsMatch(u))
            return "IC";

        if (!ExcludedChargerIcRegex.IsMatch(u))
        {
            if (pinsCount <= 8 && CompositeFetRegex.IsMatch(u))
                return "Transistor_MOSFET";
            if ((pinsCount == 0 || (pinsCount >= 2 && pinsCount <= 8)) && SubsysFetRegex.IsMatch(u))
                return "Transistor_MOSFET";
        }

        return string.IsNullOrWhiteSpace(compType) ? "IC" : compType;
    }

    public bool IsPassiveRefDes(string? refDes)
    {
        if (string.IsNullOrWhiteSpace(refDes)) return false;
        string token = refDes.Trim();
        int dot = token.IndexOf('.');
        if (dot > 0) token = token[..dot];
        string u = token.ToUpperInvariant();

        if (u.StartsWith("PC") && (u.StartsWith("PC817") || u.StartsWith("PC123") || u.StartsWith("PC201") || u.StartsWith("PC301") || u.StartsWith("PC101") || u.StartsWith("PC401")))
            return false;

        return IsPassiveRole(DeduceRole(token, string.Empty, pinsCount: 0));
    }

    public bool IsStrictPassiveRefDes(string? refDes)
    {
        if (string.IsNullOrWhiteSpace(refDes)) return false;
        string token = refDes.Trim();
        int dot = token.IndexOf('.');
        if (dot > 0) token = token[..dot];
        string u = token.ToUpperInvariant();

        if (u.StartsWith("PC") && (u.StartsWith("PC817") || u.StartsWith("PC123") || u.StartsWith("PC201") || u.StartsWith("PC301") || u.StartsWith("PC101") || u.StartsWith("PC401")))
            return false;

        if (u.StartsWith("HEATSINK") || u.StartsWith("BRACKET") || u.StartsWith("FRAME") ||
            u.StartsWith("SHIELD") || u.StartsWith("STANDOFF") || u.StartsWith("STUDFIX"))
            return true;

        if (MechHoleRegex.IsMatch(u))
            return true;

        return StrictPassiveRefDesRegex.IsMatch(u);
    }

    public string RefineRoleWithPinCount(string refDes, string? currentRole, int pinsCount)
    {
        if (string.IsNullOrWhiteSpace(refDes) || pinsCount <= 0)
            return string.IsNullOrWhiteSpace(currentRole) ? "SMD" : currentRole;

        string deduced = DeduceRole(refDes, "SMD", pinsCount);
        if (!deduced.Equals("SMD", StringComparison.OrdinalIgnoreCase))
            return deduced;

        if (!string.IsNullOrWhiteSpace(currentRole)
            && !currentRole.Equals("SMD", StringComparison.OrdinalIgnoreCase)
            && !currentRole.Equals("TH", StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentRole, DeduceRole(refDes, "SMD", pinsCount: 0), StringComparison.OrdinalIgnoreCase))
        {
            return "SMD";
        }

        return string.IsNullOrWhiteSpace(currentRole) ? "SMD" : currentRole;
    }

    public static bool IsPassiveRole(string? role) =>
        ComponentKinds.TryParse(role, out var kind) && ComponentKinds.IsPassive(kind);
}