using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaxRumsey.OzStripsPlugin.GUI;
using MaxRumsey.OzStripsPlugin.GUI.DTO;
using MaxRumsey.OzStripsPlugin.GUI.Properties;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using vatsys;

namespace MaxRumsey.OzStripsPlugin.GUI;

internal class BayRenderController(Bay bay) : IDisposable
{
    public const int StripHeight = 64;
    public const int BarHeight = 30;
    public const int StripWidth = 420;
    public const int CockOffset = 30;

    public static float Scale => OzStripsSettings.Default.StripScale;

    public StripElements.HoverActions? HoveredItem { get; set; }

    public Bay Bay { get; } = bay;

    public ToolTip? ToolTip { get; private set; }

    internal SKControl? SkControl { get; private set; }

    private StripListItem? _dragItem;
    private SKPoint _dragOffset;
    private bool _dragMoved;

    public void Dispose()
    {
        ToolTip?.RemoveAll();
        ToolTip?.Dispose();
        SkControl?.Dispose();
    }

    public void Setup()
    {
        if (StripElementList.Instance is null)
        {
            StripElementList.Load();
        }

        SkControl = new()
        {
            Size = new System.Drawing.Size(10, 1),
        };
        SkControl.PaintSurface += Paint;
        SkControl.Click += Click;
        SkControl.DoubleClick += Click;
        SkControl.MouseDown += MouseDown;
        SkControl.MouseUp += MouseUp;
        SkControl.MouseMove += Hover;
        SkControl.Name = "StripBoard";
        SkControl.BackColor = Color.Wheat;
        SkControl.Dock = DockStyle.Top;

        // SkControl.MouseMove += MouseMoved;
        Bay.ChildPanel.ChildPanel.Controls.Add(SkControl);
        SkControl.Show();

        ToolTip = new ToolTip();
    }

    private void MouseMoved(object sender, MouseEventArgs e)
    {
        SkControl?.Focus();
    }

    public void SetHeight()
    {
        if (SkControl is null)
        {
            return;
        }

        var y = GetHeightPreScale();

        SkControl.Size = new Size(SkControl.Size.Width, (int)(y * Scale));
    }

    public int GetHeightPreScale()
    {
        if (Bay.FreeMove)
        {
            var visibleHeight = SkControl?.Parent?.ClientSize.Height ?? 0;
            return Math.Max((int)(visibleHeight / Scale), StripHeight);
        }

        var y = 0;

        foreach (var item in Bay.Strips)
        {
            if (item.Type == GUI.StripItemType.STRIP)
            {
                y += StripHeight;
            }
            else
            {
                y += BarHeight;
            }
        }

        return y;
    }

    public int GetStripPosPreScale(Strip strip)
    {
        if (Bay.FreeMove && strip.FreeBayY.HasValue)
        {
            return strip.FreeBayY.Value;
        }

        var y = 0;
        var list = Bay.Strips.ToList();
        list.Reverse();
        foreach (var item in list)
        {
            if (item.Type == GUI.StripItemType.STRIP)
            {
                if (item.Strip == strip)
                {
                    return y;
                }

                y += StripHeight;
            }
            else
            {
                y += BarHeight;
            }
        }

        return y;
    }

    public void Redraw()
    {
        SkControl?.Refresh();
    }

    private void Paint(object sender, SKPaintSurfaceEventArgs e)
    {
        try
        {
            if (SkControl is null)
            {
                return;
            }

            var canvas = e.Surface.Canvas;

            canvas.Scale(OzStripsSettings.Default.StripScale);

            // make sure the canvas is blank
            canvas.Clear(SKColor.Parse("404040"));

            if (Bay.FreeMove)
            {
                PaintFreeMoveBay(canvas);
                canvas.Flush();
                return;
            }

            var total = Bay.Strips.Count - 1;
            var y = 0;
            for (var i = total; i >= 0; i--)
            {
                if (Bay.Strips[i].Type == GUI.StripItemType.QUEUEBAR && Bay.Strips[i].BarText is not null)
                {
                    var count = 0;
                    for (var j = i; j >= 0; j--)
                    {
                        if (Bay.Strips[j].Type == GUI.StripItemType.STRIP)
                        {
                            count++;
                        }
                    }

                    Bay.Strips[i].BarText = $"Queue ({count})";
                }

                var stripView = Bay.Strips[i].RenderedStripItem;

                if (stripView is not null)
                {
                    var strip = Bay.Strips[i]?.Strip;
                    var cocked = false;
                    if (strip?.CockLevel == 1)
                    {
                        cocked = true;
                    }

                    stripView.Origin = new SKPoint(cocked ? CockOffset : 0, y);
                    try
                    {
                        stripView.Render(canvas);
                    }
                    catch (Exception ex)
                    {
                        Util.LogError(ex, $"Ozstrips Renderer - Strip {Bay.Strips[i]?.Strip?.FDR.Callsign}");
                    }
                }

                y += Bay.Strips[i].Type == GUI.StripItemType.STRIP ? StripHeight : BarHeight;
            }

            canvas.Flush();
        }
        catch (Exception ex)
        {
            Util.LogError(ex, "Ozstrips Renderer");
        }
    }

    private void PaintFreeMoveBay(SKCanvas canvas)
    {
        for (var i = 0; i < Bay.Strips.Count; i++)
        {
            var item = Bay.Strips[i];
            var stripView = item.RenderedStripItem;
            if (stripView is null)
            {
                continue;
            }

            stripView.Origin = GetFreeOrigin(item, i);
            try
            {
                stripView.Render(canvas);
            }
            catch (Exception ex)
            {
                Util.LogError(ex, $"Ozstrips Renderer - Strip {item.Strip?.FDR.Callsign}");
            }
        }
    }

    private void Click(object sender, EventArgs e)
    {
        if (_dragMoved)
        {
            _dragMoved = false;
            Redraw();
            return;
        }

        var args = (MouseEventArgs)e;
        var x = (int)(args.X / Scale);
        var y = (int)(args.Y / Scale);
        var strip = DetermineStripAtPos(x, y);

        if (strip is not null)
        {
            args = new MouseEventArgs(args.Button, args.Clicks, x, y, args.Delta);
            strip.RenderedStripItem?.HandleClick(args);
        }
        else if (Bay.FreeMove && Bay.BayManager.PickedStrip is not null)
        {
            _ = Bay.BayManager.DropStrip(Bay, GetDropOrigin(x, y));
        }
        else
        {
            _ = Bay.BayManager.DropStrip(Bay);
        }

        Redraw();
    }

    private void Hover(object sender, EventArgs e)
    {
        var point = SkControl?.PointToClient(Cursor.Position);
        if (point is null)
        {
            return;
        }

        point = new Point((int)(point.Value.X / Scale), (int)(point.Value.Y / Scale));

        if (_dragItem is not null)
        {
            var origin = ClampFreeOrigin((int)(point.Value.X - _dragOffset.X), (int)(point.Value.Y - _dragOffset.Y));
            _dragItem.Strip?.SetFreeBayPosition((int)origin.X, (int)origin.Y);
            _dragMoved = true;
            Redraw();
            return;
        }

        var strip = DetermineStripAtPos(point.Value.X, point.Value.Y);

        strip?.RenderedStripItem?.HandleHover(point.Value);
    }

    private void MouseDown(object? sender, MouseEventArgs e)
    {
        if (!Bay.FreeMove || e.Button != MouseButtons.Left)
        {
            return;
        }

        var x = (int)(e.X / Scale);
        var y = (int)(e.Y / Scale);
        var item = DetermineStripAtPos(x, y);
        if (item?.Strip is null || Bay.BayManager.PickedStrip != item.Strip)
        {
            return;
        }

        var origin = GetFreeOrigin(item, Bay.Strips.IndexOf(item));
        _dragItem = item;
        _dragOffset = new SKPoint(x - origin.X, y - origin.Y);
        _dragMoved = false;
    }

    private void MouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragItem is null)
        {
            return;
        }

        var strip = _dragItem.Strip;
        _dragItem = null;

        if (strip is not null)
        {
            _ = strip.SyncStrip();
        }
    }

    private SKPoint GetFreeOrigin(StripListItem item, int index)
    {
        var x = item.Strip?.FreeBayX ?? 0;
        var y = item.Strip?.FreeBayY ?? index * StripHeight;
        return ClampFreeOrigin(x, y);
    }

    private Point GetDropOrigin(int x, int y)
    {
        var origin = ClampFreeOrigin(x - (StripWidth / 2), y - (StripHeight / 2));
        return new Point((int)origin.X, (int)origin.Y);
    }

    private SKPoint ClampFreeOrigin(int x, int y)
    {
        var maxX = Math.Max(0, (int)(((SkControl?.ClientSize.Width ?? StripWidth) / Scale) - StripWidth - 4));
        var maxY = Math.Max(0, (int)(((SkControl?.ClientSize.Height ?? StripHeight) / Scale) - StripHeight - 4));

        if (x < 0)
        {
            x = 0;
        }
        else if (x > maxX)
        {
            x = maxX;
        }

        if (y < 0)
        {
            y = 0;
        }
        else if (y > maxY)
        {
            y = maxY;
        }

        return new SKPoint(x, y);
    }

    private StripListItem? DetermineStripAtPos(int x, int y)
    {
        if (Bay.FreeMove)
        {
            for (var i = Bay.Strips.Count - 1; i >= 0; i--)
            {
                var item = Bay.Strips[i];
                if (item.Type != GUI.StripItemType.STRIP)
                {
                    continue;
                }

                var origin = GetFreeOrigin(item, i);
                var width = StripWidth + (2 * CockOffset);
                if (origin.X <= x && x < origin.X + width &&
                    origin.Y <= y && y < origin.Y + StripHeight)
                {
                    return item;
                }
            }

            return null;
        }

        var total = Bay.Strips.Count - 1;
        var jy = 0;
        for (var i = total; i >= 0; i--)
        {
            var y_offset = BarHeight;
            if (Bay.Strips[i].Type == GUI.StripItemType.STRIP)
            {
                y_offset = StripHeight;
            }

            if (jy <= y && y < (jy + y_offset))
            {
                return Bay.Strips[i];
            }

            jy += y_offset;
        }

        return null;
    }
}
