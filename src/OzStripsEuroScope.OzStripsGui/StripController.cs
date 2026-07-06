using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MaxRumsey.OzStripsPlugin.GUI.Controls;
using MaxRumsey.OzStripsPlugin.GUI.Shared;
using SkiaSharp;
using vatsys;
using static vatsys.FDP2;

namespace MaxRumsey.OzStripsPlugin.GUI;

/// <summary>
/// Controls strip UI elements and actions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="StripController"/> class.
/// </remarks>
public class StripController
{
    // private string _rtetooltiptext = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="StripController"/> class.
    /// </summary>
    /// <param name="stripController">The Strip Controller.</param>
    public StripController(Strip stripController)
    {
        Strip = stripController;
    }

    /// <summary>
    /// Gets a value indicating whether or not the CFL tooltip should be shown.
    /// </summary>
    public bool ShowCFLToolTip { get; private set; }

    /// <summary>
    /// Gets the strip controller.
    /// </summary>
    protected Strip Strip { get; }

    /// <summary>
    /// Gets the flight data record.
    /// </summary>
    protected FDP2.FDR FDR
    {
        get
        {
            return Strip.FDR;
        }
    }

    /// <summary>
    /// Gets or sets the pick toggle control.
    /// </summary>
    protected Panel? PickToggleControl { get; set; }

    /// <summary>
    /// Changes the cock level.
    /// </summary>
    /// <param name="cockLevel">The new cock level.</param>
    /// <param name="sync">If the cock level should be synced.</param>
    /// <param name="update">If the cock level should update.</param>
    public void Cock(int cockLevel, bool sync = true, bool update = true)
    {
        if (cockLevel == -1)
        {
            cockLevel = Strip.CockLevel + 1;
            if (cockLevel >= 2)
            {
                cockLevel = 0;
            }
        }

        if (update)
        {
            Strip.CockLevel = cockLevel;
        }

        if (sync)
        {
            _ = Strip.SyncStrip();
        }
    }

    /// <summary>
    /// Sets the strip to cross.
    /// </summary>
    /// <param name="sync">If the cross should be synced.</param>
    public void SetCross(bool sync = true)
    {
        if (sync)
        {
            _ = Strip.SyncStrip();
        }
    }

    /// <summary>
    /// toggles if the HMI is picked.
    /// </summary>
    /// <param name="picked">If the value is picked or not.</param>
    public void HMI_TogglePick(bool picked)
    {
        if (PickToggleControl != null)
        {
            var color = Color.Empty;
            if (picked)
            {
                color = Color.Silver;
            }

            PickToggleControl.BackColor = color;
        }
    }

    /// <summary>
    /// Opens the CFL window.
    /// </summary>
    public void OpenCFLWindow()
    {
        DropDown.ShowCFLDropDown(Strip);
    }

    /// <summary>
    /// Opens the RFL drop down.
    /// </summary>
    public void OpenRFLWindow()
    {
        DropDown.ShowRFLDropDown(Strip);
    }

    /// <summary>
    /// Determines whether the CFL alert should be active.
    /// </summary>
    /// <returns>Active.</returns>
    public bool CFLAlertActive()
    {
        if (Strip.StripType == StripType.DEPARTURE && FDR.RFL == 14000)
        {
            return true;
        }

        if (AerodromeManager.UseNose)
        {
            return CFLAlertActiveNOSE();
        }

        return CFLAlertActiveCore();
    }

    private bool CFLAlertActiveNOSE()
    {
        return CFLAlertActiveCore();
    }

    private bool CFLAlertActiveCore()
    {
        var (first, last) = GetLevelRulePositions();

        if (first is null ||
            last is null ||
            SamePosition(first, last) ||
            Strip.StripType != StripType.DEPARTURE)
        {
            return false;
        }

        var filedRfl = Strip.FiledRFL;
        if (filedRfl.Length < 2)
        {
            return false;
        }

        var requiresEvenLevel = RequiresEvenLevel(first, last);
        var shouldBeEven = int.Parse(filedRfl[1].ToString(), CultureInfo.InvariantCulture) % 2 == 0;

        if (FDR.RFL >= 41000)
        {
            return !IsValidHighLevel(FDR.RFL, requiresEvenLevel);
        }

        return requiresEvenLevel != shouldBeEven && FDR.RFL >= 3000;
    }

    private (LatLong? First, LatLong? Last) GetLevelRulePositions()
    {
        if (IsNzDomesticFlight())
        {
            var departure = Airspace2.GetAirport(FDR.DepAirport)?.LatLong;
            var destination = Airspace2.GetAirport(FDR.DesAirport)?.LatLong;

            if (IsUsablePosition(departure) && IsUsablePosition(destination))
            {
                return (departure, destination);
            }
        }

        var routePositions = FDR.ParsedRoute.Select(x => x.Intersection.LatLong).Where(IsUsablePosition).ToList();
        return (routePositions.FirstOrDefault(), routePositions.LastOrDefault());
    }

    private bool RequiresEvenLevel(LatLong first, LatLong last)
    {
        if (IsNzDomesticFlight())
        {
            return last.Latitude <= first.Latitude;
        }

        var variation = LogicalPositions.Positions.FirstOrDefault(e => e.Name == Strip.ParentAerodrome)?.MagneticVariation ?? 0;
        var track = NormalizeTrack(Conversions.CalculateTrack(first, last) + variation);
        return track is >= 180 and < 360;
    }

    private bool IsNzDomesticFlight()
    {
        return FDR.DepAirport.StartsWith("NZ", StringComparison.OrdinalIgnoreCase) &&
            FDR.DesAirport.StartsWith("NZ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidHighLevel(int level, bool requiresEvenLevel)
    {
        int[] oddDirectionRvsm = [41000, 45000, 49000];
        int[] evenDirectionRvsm = [43000, 47000, 51000];
        return requiresEvenLevel ? evenDirectionRvsm.Contains(level) : oddDirectionRvsm.Contains(level);
    }

    private static bool IsUsablePosition(LatLong? position)
    {
        return position is not null && (Math.Abs(position.Latitude) > 0.000001 || Math.Abs(position.Longitude) > 0.000001);
    }

    private static double NormalizeTrack(double track)
    {
        track %= 360;
        return track < 0 ? track + 360 : track;
    }

    private static bool SamePosition(LatLong first, LatLong second)
    {
        return Math.Abs(first.Latitude - second.Latitude) < 0.000001 &&
            Math.Abs(first.Longitude - second.Longitude) < 0.000001;
    }

    /// <summary>
    /// Opens the HDG window.
    /// </summary>
    public void OpenHDGWindow()
    {
        OpenCLXBayModal("freq");
    }

    /// <summary>
    /// Opens the RWY window.
    /// </summary>
    public void OpenRWYWindow(Point? position = null)
    {
        DropDown.ShowRunwayDropDown(Strip, position ?? Cursor.Position);
    }

    /// <summary>
    /// Opens the Reroute window.
    /// </summary>
    public void OpenRerouteMenu()
    {
        var modalChild = new RerouteControl(Strip);
        var bm = new BaseModal(modalChild, "Reroute :: " + Strip.FDR.Callsign);

        // modalChild.BaseModal = bm;
        bm.Show(MainForm.MainFormInstance);
    }

    /// <summary>
    /// Opens the CDM window.
    /// </summary>
    public void OpenCDM()
    {
        if (Strip.CDMResult is null)
        {
            return;
        }

        var modalChild = new CDMAircraftControl(Strip.CDMResult, Strip.ParentAerodrome + Strip.Server);
        var bm = new BaseModal(modalChild, "CDM Details :: " + Strip.FDR.Callsign);

        bm.Show(MainForm.MainFormInstance);
    }

    /// <summary>
    /// Opens the SID window.
    /// </summary>
    public void OpenSIDWindow(Point? position = null)
    {
        DropDown.ShowSIDDropDown(Strip, position);
    }

    /// <summary>
    /// Opens the Clearance bya modal.
    /// </summary>
    /// <param name="labelName">Label Name.</param>
    public void OpenCLXBayModal(string labelName)
    {
        switch (labelName)
        {
            case "std":
                DropDown.ShowGateDropDown(Strip);
                return;
            case "clx":
                DropDown.ShowCLXDropDown(Strip);
                return;
            case "glop":
                DropDown.ShowGlopDropDown(Strip);
                return;
            case "remark":
                DropDown.ShowRmkDropDown(Strip);
                return;
            case "freq":
                DropDown.ShowFreqDropDown(Strip);
                return;
        }
    }

    /// <summary>
    /// Opens the Hoppies PDC window.
    /// </summary>
    public void OpenPDCWindow()
    {
        var modalChild = new PDCControl(Strip);
        var bm = new BaseModal(modalChild, "PDC :: " + Strip.FDR.Callsign);

        bm.ReturnEvent += PDCReturned;
        bm.Show(MainForm.MainFormInstance);
    }

    private void PDCReturned(object sender, ModalReturnArgs e)
    {
        var control = (PDCControl)e.Child;

        Strip.SendPDC(control.PDCText);
    }

    /// <summary>
    /// Opens the VatSys PDC window.
    /// </summary>
    public void OpenVatSysPDCWindow()
    {
        MMI.OpenCPDLCWindow(FDR, null, CPDLC.MessageCategories.FirstOrDefault(x => x.Name == "PDC"));
    }

    /// <summary>
    /// Assigns a squawk.
    /// </summary>
    public void AssignSSR()
    {
        if (FDP2.IsDefaultSquawk(Strip.FDR.AssignedSSRCode))
        {
            FDP2.SetASSR(Strip.FDR);
            _ = Strip.SyncStrip();
            return;
        }

        DropDown.ShowSSRDropDown(Strip);
    }

    /// <summary>
    /// Toggles strip ready status.
    /// </summary>
    public void ToggleReady()
    {
        Strip.Ready = !Strip.Ready;
        _ = Strip.SyncStrip();
    }

    /*
    public void UpdateStrip()
    {
        if (FDR == null)
        {
            return;
        }

        SetLabel("eobt", Strip.Time);

        SetLabel("acid", FDR.Callsign);
        SetLabel("ssr", Strip.DisplaySSR);
        SetLabel("type", FDR.AircraftType);
        SetLabel("frul", FDR.FlightRules);

        SetLabel("route", Strip.FirstWpt);
        SetBackColour("route", DetermineRouteBackColour());

        if (StripToolTips.ContainsKey("routetooltip"))
        {
            if (Strip.DodgyRoute)
            {
                var routes = new List<string>();
                Array.ForEach(Strip.ValidRoutes, x => routes.Add("(" + x.AircraftType + ") " + x.RouteText));
                var str = Strip.Route +
                                "\n---\nPotentially non-compliant route detected! Accepted Routes:\n" + string.Join("\n", routes) + "\nParsed Route: " + Strip.CondensedRoute;
                if (str != _rtetooltiptext)
                {
                    StripToolTips["routetooltip"].SetToolTip(StripElements["route"], str);
                    _rtetooltiptext = str;
                }
            }
            else
            {
                var str = Strip.Route;
                if (str != _rtetooltiptext)
                {
                    StripToolTips["routetooltip"].SetToolTip(StripElements["route"], str);
                    _rtetooltiptext = str;
                }
            }
        }

        SetLabel("sid", Strip.SID);

        SetLabel("ades", FDR.DesAirport);
        SetLabel("CFL", Strip.CFL);

        try
        {
            if (Strip.ArrDepType == StripArrDepType.DEPARTURE)
            {
                var colour = DetermineCFLBackColour();
                SetBackColour("CFL", colour);
            }
        }
        catch
        {
        }

        SetLabel("HDG", string.IsNullOrEmpty(Strip.HDG) ? string.Empty : "H" + Strip.HDG);

        SetLabel("CLX", Strip.CLX);

        SetLabel("stand", Strip.Gate);

        SetLabel("remark", Strip.Remark);

        if (Strip.TakeOffTime != null)
        {
            var diff = (TimeSpan)(DateTime.UtcNow - Strip.TakeOffTime);
            SetLabel("tot", diff.ToString(@"mm\:ss", CultureInfo.InvariantCulture));
            SetForeColour("tot", Color.Green);
        }
        else
        {
            SetLabel("tot", "00:00");
            SetForeColour("tot", Color.Black);
        }

        SetLabel("rfl", Strip.RFL);

        SetLabel("ready", Strip.Ready ? "RDY" : string.Empty);

        if (!Strip.Ready && (Strip.CurrentBay == StripBay.BAY_HOLDSHORT || Strip.CurrentBay == StripBay.BAY_RUNWAY) && Strip.ArrDepType == StripArrDepType.DEPARTURE)
        {
            SetBackColour("ready", Color.Orange);
        }
        else
        {
            SetBackColour("ready", Color.Empty);
        }

        SetLabel("glop", FDR.GlobalOpData);

        if (Strip.SquawkCorrect)
        {
            SetLabel("ssrsymbol", "*");
        }
        else
        {
            SetLabel("ssrsymbol", string.Empty);
        }

        SetCross(false);
        Cock(0, false, false);

        SetLabel("rwy", Strip.RWY);
        SetLabel("wtc", FDR.AircraftWake);

        ResumeLayout();
    }
    */
}
