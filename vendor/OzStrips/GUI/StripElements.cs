using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaxRumsey.OzStripsPlugin.GUI;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable SA1602 // Enumeration items should be documented
public static class StripElements
{
    /// <summary>
    /// List of possible values.
    /// </summary>
    public enum Values
    {
        EOBT,
        ACID,
        SSR,
        TYPE,
        FRUL,
        ATIS,
        FIRST_WPT,
        SID,
        ADES,
        CFL,
        HDG,
        CLX,
        STAND,
        REMARK,
        TOT,
        RFL,
        READY,
        GLOP,
        PDC_INDICATOR,
        RWY,
        WTC,
        ROUTE,
        DEPFREQ,
        ADEP,
        DEP_CHANGED,
        WAYPOINT_1,
        WAYPOINT_1_ETA,
        WAYPOINT_2,
        WAYPOINT_2_ETA,
        WAYPOINT_3,
        WAYPOINT_3_ETA,
        WAYPOINT_4,
        WAYPOINT_4_ETA,
        NONE,
    }

    public enum Actions
    {
        NONE,
        SHOW_ROUTE,
        OPEN_HDG_ALT,
        OPEN_FDR,
        PICK,
        ASSIGN_SSR,
        MOD_SID,
        OPEN_REROUTE,
        MOD_RWY,
        MOD_CFL,
        MOD_RFL,
        MOD_CLX,
        MOD_STD,
        MOD_ALLOCATOR_STD,
        MOD_GLOP,
        MOD_REMARK,
        COCK,
        SID_TRIGGER,
        SET_READY,
        ACK_ATIS,
        SET_TOT,
        OPEN_PDC,
        OPEN_PM,
        OPEN_CDM,
        WAKE_TIMER,
        INHIBIT_ROUTE,
        INHIBIT_RFL,
        INHIBIT_HDG,
        INHIBIT_SID,
        INHIBIT_SSR,
        INHIBIT_READY,
        INHIBIT_DEPCHANGED,
        MOD_WAYPOINT_1_ETA,
        MOD_WAYPOINT_2_ETA,
        MOD_WAYPOINT_3_ETA,
        MOD_WAYPOINT_4_ETA,
    }

    public enum HoverActions
    {
        NONE,
        ROUTE_WARNING,
        RFL_WARNING,
        SSR_WARNING,
        SID_TRIGGER,
    }
}
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
#pragma warning restore SA1602 // Enumeration items should be documented
