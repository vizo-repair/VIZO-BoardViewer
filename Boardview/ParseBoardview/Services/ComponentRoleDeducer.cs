// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Services/ComponentRoleDeducer.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class ComponentRoleDeducer
{
    public string DeduceRole(string refDes, string compType)
    {
        string u = refDes.ToUpperInvariant();
        // Mechanical parts, heatsinks, brackets, frames, shields
        if (u.StartsWith("HEATSINK") || u.StartsWith("BRACKET") || u.StartsWith("FRAME") ||
            u.StartsWith("SHIELD") || u.StartsWith("STANDOFF") || u.StartsWith("STUDFIX"))
            return "Mechanical";

        if (u.StartsWith("PCH") || u.StartsWith("CHIPSET")) return "Chipset_PCH";
        if (u.StartsWith("CPU") || u.StartsWith("GPU") || u.StartsWith("SOC") || u.StartsWith("UAPU") || u.StartsWith("UCPU")) return "Processor_APU";
        if (u.StartsWith("PU")) return "PWM_Power_IC";
        if (u.StartsWith("UA")) return "Audio_Codec";
        if (u.StartsWith("UV")) return "GPU_Video_IC";
        if (u.StartsWith("UE") || u.StartsWith("KBC") || u.StartsWith("EC")) return "Embedded_Controller_EC";
        if (u.StartsWith("U")) return "IC";
        if (u.StartsWith("PQ") || u.StartsWith("QA") || (u.Length > 1 && u[0] == 'Q' && char.IsDigit(u[1]))) return "Transistor_MOSFET";

        // Optocouplers (PC817, PC201, PC123, OP, OK, ISO)
        if (u.StartsWith("PC") && (u.StartsWith("PC817") || u.StartsWith("PC123") || u.StartsWith("PC201") || u.StartsWith("PC301") || u.StartsWith("PC101") || u.StartsWith("PC401")))
            return "Optocoupler";
        if (u.StartsWith("OP") || u.StartsWith("OK") || u.StartsWith("ISO")) return "Optocoupler";

        if (u.StartsWith("PC") || u.StartsWith("TC")) return "Capacitor_Polarized";
        if (u.StartsWith("JSPK")) return "Speaker_Connector";
        if (u.StartsWith("JHDMI")) return "HDMI_Connector";
        if (u.StartsWith("JEDP") || u.StartsWith("JLVDS")) return "Display_Connector";
        if (u.StartsWith("JDCIN") || u.StartsWith("JDC")) return "DC_IN_Power_Jack";
        if (u.StartsWith("JBAT")) return "Battery_Connector";
        if (u.StartsWith("JUSB")) return "USB_Connector";
        if (u.StartsWith("JLAN") || u.StartsWith("RJ")) return "Ethernet_Connector";
        if (u.StartsWith("J") || u.StartsWith("CN")) return "Connector";
        if (u.StartsWith("PJ") || u.StartsWith("PJP")) return "Jumper";

        // Passives are strictly checked: a 1-letter prefix MUST be followed by a digit!
        if (u.StartsWith("PD") || (u.Length > 1 && u[0] == 'D' && char.IsDigit(u[1]))) return "Diode";
        if (u.StartsWith("PL") || (u.Length > 1 && u[0] == 'L' && char.IsDigit(u[1]))) return "Inductor";
        if (u.StartsWith("PF") || (u.Length > 1 && u[0] == 'F' && char.IsDigit(u[1]))) return "Fuse";
        if (u.StartsWith("PR") || (u.Length > 1 && u[0] == 'R' && char.IsDigit(u[1]))) return "Resistor";
        if (u.Length > 1 && u[0] == 'C' && char.IsDigit(u[1])) return "Capacitor";

        return string.IsNullOrWhiteSpace(compType) ? "IC" : compType;
    }

    public bool IsPassiveRefDes(string? refDes)
    {
        if (string.IsNullOrWhiteSpace(refDes)) return false;
        string token = refDes.Trim();
        int dot = token.IndexOf('.');
        if (dot > 0) token = token[..dot];
        string u = token.ToUpperInvariant();

        // If this is an IC part number (LAF0001, LD7575, RT8205, LM358, CS4270), it is NOT a passive!
        if (u.Length > 1 && !char.IsDigit(u[1]) && !u.StartsWith("PR") && !u.StartsWith("PC") && !u.StartsWith("PL") && !u.StartsWith("PF") && !u.StartsWith("PD") && !u.StartsWith("TC"))
            return false;

        // An optocoupler is an active chip, not a passive capacitor!
        if (u.StartsWith("PC") && (u.StartsWith("PC817") || u.StartsWith("PC123") || u.StartsWith("PC201") || u.StartsWith("PC301") || u.StartsWith("PC101") || u.StartsWith("PC401")))
            return false;

        return IsPassiveRole(DeduceRole(token, string.Empty));
    }

    public static bool IsPassiveRole(string? role) => role is
        "Resistor" or "Capacitor" or "Capacitor_Polarized" or "Inductor" or "Fuse" or "Mechanical";
}