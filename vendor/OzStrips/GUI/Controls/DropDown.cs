using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaxRumsey.OzStripsPlugin.GUI.DTO.XML;
using vatsys;

namespace MaxRumsey.OzStripsPlugin.GUI.Controls;

/// <summary>
/// Drop down form.
/// </summary>
public partial class DropDown : BaseForm
{
    private const int ItemWidth = 100;
    private const int ItemHeight = 28;
    private const int HeaderHeight = 25;
    private const int MaxVisibleItems = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropDown"/> class.
    /// </summary>
    /// <param name="items">List of drop down items.</param>
    /// <param name="title">Element title.</param>
    public DropDown(DropDownItem[] items, string title)
    {
        InitializeComponent();
        Setup();
        Text = title;

        foreach (var item in items)
        {
            AddElement(item);
        }

        PerformLayout();
        var visibleItems = Math.Max(1, Math.Min(items.Length, MaxVisibleItems));
        flowLayoutPanel1.AutoScroll = items.Length > MaxVisibleItems;
        flowLayoutPanel1.WrapContents = false;
        flowLayoutPanel1.Size = new(ItemWidth + (flowLayoutPanel1.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0), ItemHeight * visibleItems);
        ClientSize = new(flowLayoutPanel1.Width, flowLayoutPanel1.Height + HeaderHeight);
        MinimumSize = new(ItemWidth, ItemHeight);
        MaximumSize = ClientSize;
        StartPosition = FormStartPosition.Manual;
        Location = Cursor.Position;

        MMI.EnsureWindowVisible(this);

        foreach (var control in flowLayoutPanel1.Controls)
        {
            var textbox = control as TextBox;
            textbox?.Select();
        }
    }

    /// <summary>
    /// Called when the drop down is completed, and will return a value.
    /// </summary>
    public event EventHandler<string>? Complete;

    private void Setup()
    {
        MiddleClickClose = true;
    }

    private void AddElement(DropDownItem item)
    {
        Control element;
        switch (item.Type)
        {
            case DropDownItem.DropDownItemType.BUTTON:
                element = new GenericButton();
                element.Text = item.Text;
                element.Size = new(ItemWidth, ItemHeight);
                element.MouseUp += Element_MouseUp;
                break;
            case DropDownItem.DropDownItemType.FREETEXT:
                var tb = new TextBox();
                element = tb;
                tb.Text = item.Text;
                tb.Size = new(ItemWidth, ItemHeight);
                tb.MaxLength = item.MaxLen;
                tb.CharacterCasing = CharacterCasing.Upper;
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

    private static void CreateDropDown(DropDownItem[] items, string title, Action<string> result)
    {
        if (DropDownAlreadyOpen())
        {
            return;
        }

        var dropdown = new DropDown(items, title);
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
    /// Shows the UTC estimate input for one of the strip's route waypoints.
    /// </summary>
    public static void ShowWaypointEtaDropDown(Strip strip, int waypointIndex)
    {
        if (waypointIndex is < 0 or > 3)
        {
            return;
        }

        CreateDropDown([new(DropDownItem.DropDownItemType.FREETEXT, strip.GetWaypointEta(waypointIndex), 4)], strip.FDR.Callsign + " UTC", value =>
        {
            strip.SetWaypointEta(waypointIndex, value);
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

            var standMap = options
                .Select(x => x.StandId?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x!.ToUpperInvariant(), x => x!, StringComparer.OrdinalIgnoreCase);
            if (standMap.Count == 0)
            {
                ShowGateDropDown(strip);
                return;
            }

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
        if (string.IsNullOrWhiteSpace(standId))
        {
            return;
        }

        try
        {
            var response = await StandAllocatorService.Instance.ReassignStandAsync(strip, standId);
            var assignedStand = response.Assignment?.StandId;
            var selectedStand = standId;
            if (!string.IsNullOrWhiteSpace(assignedStand))
            {
                selectedStand = assignedStand!.Trim();
            }

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
            if (!Network.Me.IsRealATC)
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
}
