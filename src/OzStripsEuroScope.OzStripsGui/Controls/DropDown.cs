using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaxRumsey.OzStripsPlugin.GUI.DTO.XML;
using MaxRumsey.OzStripsPlugin.GUI.Shared;
using vatsys;

namespace MaxRumsey.OzStripsPlugin.GUI.Controls;

/// <summary>
/// Drop down form.
/// </summary>
public partial class DropDown : BaseForm
{
    private const int MinItemWidth = 155;
    private const int MaxItemWidth = 260;
    private const int ItemHeight = 26;
    private const int TitleHeight = 24;
    private const int MaxVisibleItems = 10;
    private readonly int _itemWidth;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropDown"/> class.
    /// </summary>
    /// <param name="items">List of drop down items.</param>
    /// <param name="title">Element title.</param>
    public DropDown(DropDownItem[] items, string title, Point? anchor = null)
    {
        InitializeComponent();
        Setup();
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(45, 45, 45);
        TopMost = true;
        _itemWidth = CalculateItemWidth(items);

        foreach (var item in items)
        {
            AddElement(item);
        }

        PerformLayout();
        var visibleItems = Math.Max(1, Math.Min(items.Length, MaxVisibleItems));
        flowLayoutPanel1.AutoScroll = items.Length > MaxVisibleItems;
        flowLayoutPanel1.WrapContents = false;
        flowLayoutPanel1.BackColor = Color.FromArgb(38, 38, 38);
        flowLayoutPanel1.Location = new(3, TitleHeight);
        flowLayoutPanel1.Size = new(_itemWidth + (flowLayoutPanel1.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0), ItemHeight * visibleItems);
        ClientSize = new(flowLayoutPanel1.Width + 6, TitleHeight + flowLayoutPanel1.Height + 3);
        MinimumSize = new(_itemWidth, ItemHeight + TitleHeight);
        MaximumSize = ClientSize;
        AddTitleBar(title);
        StartPosition = FormStartPosition.Manual;
        Location = GetDropDownLocation(anchor ?? Cursor.Position);

        MMI.EnsureWindowVisible(this);

        foreach (var control in flowLayoutPanel1.Controls)
        {
            var textbox = control as TextBox;
            textbox?.Select();
        }
    }

    private static int CalculateItemWidth(DropDownItem[] items)
    {
        using var font = new Font("Consolas", 10F, FontStyle.Bold);
        var widest = items
            .Select(x => TextRenderer.MeasureText(x.Text ?? string.Empty, font).Width + 28)
            .DefaultIfEmpty(MinItemWidth)
            .Max();

        return Math.Max(MinItemWidth, Math.Min(MaxItemWidth, widest));
    }

    private Point GetDropDownLocation(Point anchor)
    {
        var location = new Point(anchor.X - (ClientSize.Width / 2), anchor.Y - TitleHeight);
        var workingArea = Screen.FromPoint(anchor).WorkingArea;

        if (location.X < workingArea.Left)
        {
            location.X = workingArea.Left;
        }
        else if (location.X + ClientSize.Width > workingArea.Right)
        {
            location.X = workingArea.Right - ClientSize.Width;
        }

        if (location.Y < workingArea.Top)
        {
            location.Y = workingArea.Top;
        }
        else if (location.Y + ClientSize.Height > workingArea.Bottom)
        {
            location.Y = Math.Max(workingArea.Top, anchor.Y - ClientSize.Height);
        }

        return location;
    }

    /// <summary>
    /// Called when the drop down is completed, and will return a value.
    /// </summary>
    public event EventHandler<string>? Complete;

    private void Setup()
    {
        MiddleClickClose = true;
    }

    private void AddTitleBar(string title)
    {
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Location = new(3, 1),
            Size = new(ClientSize.Width - 27, TitleHeight - 2),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(145, 145, 145),
        };

        var closeLabel = new Label
        {
            Text = "X",
            AutoSize = false,
            Location = new(ClientSize.Width - 23, 1),
            Size = new(20, TitleHeight - 2),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 11F, FontStyle.Bold),
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(145, 145, 145),
        };
        closeLabel.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Close();
            }
        };

        Controls.Add(titleLabel);
        Controls.Add(closeLabel);
        titleLabel.BringToFront();
        closeLabel.BringToFront();
    }

    private void AddElement(DropDownItem item)
    {
        Control element;
        switch (item.Type)
        {
            case DropDownItem.DropDownItemType.BUTTON:
                element = new GenericButton();
                element.Text = item.Text;
                element.Size = new(_itemWidth, ItemHeight);
                element.Font = new Font("Consolas", 10F, FontStyle.Bold);
                element.BackColor = Color.FromArgb(31, 31, 31);
                element.ForeColor = Color.FromArgb(145, 145, 145);
                ((Button)element).FlatStyle = FlatStyle.Flat;
                ((Button)element).FlatAppearance.BorderColor = Color.FromArgb(225, 225, 225);
                ((Button)element).FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 58);
                ((Button)element).FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 70, 70);
                element.MouseUp += Element_MouseUp;
                break;
            case DropDownItem.DropDownItemType.FREETEXT:
                var tb = new TextBox();
                element = tb;
                tb.Text = item.Text;
                tb.Size = new(_itemWidth, ItemHeight);
                tb.MaxLength = item.MaxLen;
                tb.CharacterCasing = CharacterCasing.Upper;
                tb.Font = new Font("Consolas", 11F, FontStyle.Bold);
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = Color.FromArgb(31, 31, 31);
                tb.ForeColor = Color.FromArgb(145, 145, 145);
                element.KeyDown += (s, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.A)
                    {
                        return;
                    }
                    else if (e.KeyCode == Keys.Menu)
                    {
                        e.SuppressKeyPress = true;
                        return;
                    }
                    else if ((e.Control && e.KeyCode != Keys.ControlKey) ||
                             (e.Alt && e.KeyCode != Keys.Alt))
                    {
                        if (e.KeyCode == Keys.Back)
                        {
                            return;
                        }

                        e.SuppressKeyPress = true;

                        var key = e.KeyCode.ToString();
                        if (key.Length > 1)
                        {
                            key = key.Substring(key.Length - 1);
                        }

                        var pos = tb.SelectionStart;

                        var newText = tb.Text
                            .Remove(pos, tb.SelectionLength)
                            .Insert(pos, key);

                        if (newText.Length <= tb.MaxLength)
                        {
                            tb.Text = newText;
                            tb.SelectionStart = pos + 1;
                        }
                    }
                };
                element.KeyPress += (s, e) =>
                {
                    if (e.KeyChar == (char)Keys.Escape)
                    {
                        Close();
                    }
                    else if (e.KeyChar == (char)Keys.Enter)
                    {
                        Complete?.Invoke(element, tb.Text);
                        Close();
                    }
                };
                break;
            default:
                throw new InvalidOperationException("Unknown drop down item type");
        }

        element.Margin = new(0);

        flowLayoutPanel1.Controls.Add(element);
    }

    private void Element_MouseUp(object sender, MouseEventArgs e)
    {
        var control = sender as Control ?? throw new Exception("Sender was null.");
        if (e.Button == MouseButtons.Left)
        {
            Complete?.Invoke(this, control.Text);
            Close();
        }
        else if (e.Button == MouseButtons.Middle)
        {
            Close();
        }
    }

    private static bool DropDownAlreadyOpen()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is DropDown)
            {
                return true;
            }
        }

        return false;
    }

    private static void CreateDropDown(DropDownItem[] items, string title, Action<string> result, Point? position = null)
    {
        if (DropDownAlreadyOpen())
        {
            return;
        }

        var dropdown = new DropDown(items, title, position);
        dropdown.Complete += (s, e) =>
        {
            try
            {
                result(e);
            }
            catch (Exception ex)
            {
                Util.LogError(ex);
            }
        };
        dropdown.Show(MainForm.MainFormInstance);
    }

    /// <summary>
    /// Shows the gate drop down for the specified strip.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowGateDropDown(Strip strip)
    {
        CreateDropDown([new(DropDownItem.DropDownItemType.FREETEXT, strip.Gate, 4)], strip.FDR.Callsign, s =>
        {
            strip.Gate = s;
            _ = strip.SyncStrip();
        });
    }

    /// <summary>
    /// Shows a stand allocator drop down for the specified strip, if the current airport is supported.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static async void ShowStandAllocatorDropDown(Strip strip)
    {
        try
        {
            var options = await StandAllocatorService.Instance.GetStandOptionsAsync(strip);
            if (options.Count == 0)
            {
                ShowGateDropDown(strip);
                return;
            }

            var standMap = options.ToDictionary(x => x.StandId.Trim().ToUpperInvariant(), x => x.StandId.Trim(), StringComparer.OrdinalIgnoreCase);
            var items = standMap.Keys.Select(x => new DropDownItem(DropDownItem.DropDownItemType.BUTTON, x)).ToArray();
            CreateDropDown(items, strip.FDR.Callsign, s =>
            {
                if (standMap.TryGetValue(s, out var standId))
                {
                    _ = AssignAllocatorStand(strip, standId);
                }
            });
        }
        catch (Exception ex)
        {
            Util.LogError(ex, "OzStrips Stand Allocator");
            Util.ShowWarnBox("Stand allocator did not return available stands: " + ex.Message);
            ShowGateDropDown(strip);
        }
    }

    private static async Task AssignAllocatorStand(Strip strip, string standId)
    {
        try
        {
            var response = await StandAllocatorService.Instance.ReassignStandAsync(strip, standId);
            var selectedStand = string.IsNullOrWhiteSpace(response.Assignment?.StandId) ? standId : response.Assignment.StandId;
            strip.ApplyStandAllocatorSelection(selectedStand);
            _ = strip.SyncStrip();
        }
        catch (Exception ex)
        {
            Util.LogError(ex, "OzStrips Stand Allocator");
            Util.ShowWarnBox("Stand allocator could not assign the stand: " + ex.Message);
        }
    }

    /// <summary>
    /// Shows the clx drop down for the specified strip.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowCLXDropDown(Strip strip)
    {
        CreateDropDown([new(DropDownItem.DropDownItemType.FREETEXT, strip.CLX)], strip.FDR.Callsign, s =>
        {
            strip.CLX = s;
            _ = strip.SyncStrip();
        });
    }

    /// <summary>
    /// Shows the glop drop down for the specified strip.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowGlopDropDown(Strip strip)
    {
        CreateDropDown([new(DropDownItem.DropDownItemType.FREETEXT, strip.FDR.GlobalOpData, 10)], strip.FDR.Callsign, s =>
        {
            if (!Network.Me.IsRealATC && !MainForm.IsDebug)
            {
                return;
            }

            FDP2.SetGlobalOps(strip.FDR, s);
            _ = strip.SyncStrip();
        });
    }

    /// <summary>
    /// Shows the remark drop down for the specified strip.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowRmkDropDown(Strip strip)
    {
        CreateDropDown([new(DropDownItem.DropDownItemType.FREETEXT, strip.Remark, 10)], strip.FDR.Callsign, s =>
        {
            strip.Remark = s;
            _ = strip.SyncStrip();
        });
    }

    /// <summary>
    /// Shows the dep freq drop down for the specified strip.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowFreqDropDown(Strip strip)
    {
        List<DropDownItem> items = [];

        foreach (var freq in strip.PossibleDepFreqs)
        {
            items.Add(new(DropDownItem.DropDownItemType.BUTTON, freq));
        }

        items.Add(new(DropDownItem.DropDownItemType.FREETEXT, string.Empty, 7));
        CreateDropDown(items.ToArray(), strip.FDR.Callsign, s =>
        {
            strip.DepartureFrequency = s;
            _ = strip.SyncStrip();
        });
    }

    /// <summary>
    /// Shows the requested flight level drop down.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowRFLDropDown(Strip strip)
    {
        CreateDropDown(BuildFlightLevelItems(strip.FDR.RFL), strip.FDR.Callsign, s =>
        {
            strip.RFL = s;
        });
    }

    /// <summary>
    /// Shows the cleared flight level drop down.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowCFLDropDown(Strip strip)
    {
        CreateDropDown(BuildFlightLevelItems(strip.FDR.CFL > 0 ? strip.FDR.CFL : strip.FDR.RFL), strip.FDR.Callsign, s =>
        {
            strip.CFL = s;
        });
    }

    /// <summary>
    /// Shows the departure runway drop down.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowRunwayDropDown(Strip strip, Point? position = null)
    {
        var items = new List<DropDownItem>();
        AddButton(items, strip.RWY);

        foreach (var runway in strip.PossibleDepRunways
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            AddButton(items, runway);
        }

        AddButton(items, "None");
        CreateDropDown([.. items], strip.FDR.Callsign, s =>
        {
            strip.RWY = string.Equals(s, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : s;
            _ = strip.SyncStrip();
        }, position);
    }

    /// <summary>
    /// Shows the SID drop down.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowSIDDropDown(Strip strip, Point? position = null)
    {
        var items = new List<DropDownItem>();
        var options = strip.StripType == StripType.ARRIVAL ? BuildStarOptions(strip) : BuildSidOptions(strip);
        var currentSid = CanonicaliseSidForDropDown(strip, strip.SID);

        AddButton(items, currentSid, preserveCase: true);

        foreach (var label in options.Keys)
        {
            AddButton(items, label, preserveCase: true);
        }

        AddButton(items, "None");
        items.Add(new(DropDownItem.DropDownItemType.FREETEXT, currentSid, 16));
        CreateDropDown([.. items], strip.FDR.Callsign, s =>
        {
            if (string.Equals(s, "None", StringComparison.OrdinalIgnoreCase))
            {
                strip.SID = string.Empty;
            }
            else
            {
                if (options.TryGetValue(s, out var choice))
                {
                    if (!string.IsNullOrWhiteSpace(choice.Runway))
                    {
                        strip.RWY = choice.Runway;
                    }

                    strip.SID = choice.Sid;
                }
                else
                {
                    strip.SID = CanonicaliseSidForDropDown(strip, s);
                }
            }
        }, position);
    }

    private static Dictionary<string, SidChoice> BuildSidOptions(Strip strip)
    {
        var options = new Dictionary<string, SidChoice>(StringComparer.OrdinalIgnoreCase);
        var runways = Airspace2.GetRunways(strip.FDR.DepAirport);
        var selectedRunway = strip.FDR.DepartureRunway?.Name ?? string.Empty;
        var sidMappings = runways
            .Where(x => string.IsNullOrWhiteSpace(selectedRunway) || string.Equals(x.Name, selectedRunway, StringComparison.OrdinalIgnoreCase))
            .SelectMany(runway => runway.SIDs.Select(mapping => new
            {
                Runway = runway.Name,
                mapping.sidStar.Name,
            }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Runway, StringComparer.OrdinalIgnoreCase);

        foreach (var sid in sidMappings)
        {
            var sidName = (sid.Name ?? string.Empty).Trim();
            var runway = (sid.Runway ?? string.Empty).Trim().ToUpperInvariant();
            var label = string.IsNullOrWhiteSpace(runway) ? sidName : sidName + " - " + runway;
            if (string.IsNullOrWhiteSpace(sidName) || options.ContainsKey(label))
            {
                continue;
            }

            options[label] = new SidChoice(sidName, runway);
        }

        return options;
    }

    private static Dictionary<string, SidChoice> BuildStarOptions(Strip strip)
    {
        var options = new Dictionary<string, SidChoice>(StringComparer.OrdinalIgnoreCase);
        var runways = Airspace2.GetRunways(strip.FDR.DesAirport);
        var selectedRunway = strip.FDR.ArrivalRunway?.Name ?? string.Empty;
        var starMappings = runways
            .Where(x => string.IsNullOrWhiteSpace(selectedRunway) || string.Equals(x.Name, selectedRunway, StringComparison.OrdinalIgnoreCase))
            .SelectMany(runway => runway.STARs.Select(mapping => new
            {
                Runway = runway.Name,
                mapping.sidStar.Name,
            }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Runway, StringComparer.OrdinalIgnoreCase);

        foreach (var star in starMappings)
        {
            var starName = (star.Name ?? string.Empty).Trim();
            var runway = (star.Runway ?? string.Empty).Trim().ToUpperInvariant();
            var label = string.IsNullOrWhiteSpace(runway) ? starName : starName + " - " + runway;
            if (string.IsNullOrWhiteSpace(starName) || options.ContainsKey(label))
            {
                continue;
            }

            options[label] = new SidChoice(starName, runway);
        }

        return options;
    }

    private static string CanonicaliseSidForDropDown(Strip strip, string sid)
    {
        sid = (sid ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sid))
        {
            return string.Empty;
        }

        var runways = Airspace2.GetRunways(strip.StripType == StripType.ARRIVAL ? strip.FDR.DesAirport : strip.FDR.DepAirport);
        return runways
            .SelectMany(x => (strip.StripType == StripType.ARRIVAL ? x.STARs : x.SIDs).Select(mapping => mapping.sidStar.Name))
            .FirstOrDefault(x => string.Equals(x, sid, StringComparison.OrdinalIgnoreCase))
            ?? sid;
    }

    private sealed class SidChoice
    {
        public SidChoice(string sid, string runway)
        {
            Sid = sid;
            Runway = runway;
        }

        public string Sid { get; }

        public string Runway { get; }
    }

    /// <summary>
    /// Shows the squawk drop down.
    /// </summary>
    /// <param name="strip">Strip.</param>
    public static void ShowSSRDropDown(Strip strip)
    {
        var current = FDP2.FormatSSR(strip.FDR.AssignedSSRCode);
        var items = new List<DropDownItem>();
        AddButton(items, current == "XXXX" ? string.Empty : current);
        AddButton(items, "AUTO");
        AddButton(items, "XXXX");

        CreateDropDown([.. items], strip.FDR.Callsign, s =>
        {
            if (string.Equals(s, "AUTO", StringComparison.OrdinalIgnoreCase))
            {
                FDP2.SetASSR(strip.FDR);
            }
            else
            {
                FDP2.SetSSR(strip.FDR, string.Equals(s, "XXXX", StringComparison.OrdinalIgnoreCase) ? "2000" : s);
            }

            _ = strip.SyncStrip();
        });
    }

    /// <summary>
    /// Shows a crossing / release bar dropdown.
    /// </summary>
    /// <param name="autoMapAerodrome">Automap aerodrome file.</param>
    /// <param name="type">Crossing or Released.</param>
    /// <param name="bayManager">Bay Manager.</param>
    public static void ShowCrossingOrReleaseDropDown(AutoMapAerodrome autoMapAerodrome, string type, BayManager bayManager)
    {
        List<DropDownItem> items = new();

        var runways = autoMapAerodrome.RunwayPairs;

        foreach (var runway in runways)
        {
            if (runway.Length % 2 != 0)
            {
                continue;
            }

            items.Add(new(DropDownItem.DropDownItemType.BUTTON, runway.Insert(runway.Length / 2, "/")));
        }

        CreateDropDown([.. items], "Runway", s =>
        {
            try
            {
                bayManager.ToggleCrossReleaseBar(s.Replace("/", string.Empty), type);
            }
            catch (Exception ex)
            {
                Util.LogError(ex);
            }
        });
    }

    private static void AddButton(List<DropDownItem> items, string? text, bool preserveCase = false)
    {
        text = (text ?? string.Empty).Trim();
        if (!preserveCase)
        {
            text = text.ToUpperInvariant();
        }

        if (text.Length == 0 || items.Any(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        items.Add(new(DropDownItem.DropDownItemType.BUTTON, text));
    }

    private static DropDownItem[] BuildFlightLevelItems(int referenceFeet)
    {
        var items = new List<DropDownItem>();

        for (var level = 590; level >= 430; level -= 20)
        {
            AddButton(items, level.ToString("000", CultureInfo.InvariantCulture));
        }

        for (var level = 410; level >= 0; level -= 10)
        {
            AddButton(items, level.ToString("000", CultureInfo.InvariantCulture));
        }

        return [.. items];
    }
}
