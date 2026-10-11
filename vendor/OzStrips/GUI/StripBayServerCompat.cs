using System;
using System.Globalization;
using MaxRumsey.OzStripsPlugin.GUI.Shared;

namespace MaxRumsey.OzStripsPlugin.GUI;

/// <summary>
/// Keeps NZ-only bay state compatible with the hosted OzStrips server.
/// </summary>
internal static class StripBayServerCompat
{
    private const string Marker = "OZNZBAY";

    public static StripDTO ToServerSafe(Strip strip)
    {
        StripDTO dto = strip;
        return ToServerSafe(dto);
    }

    public static StripDTO ToServerSafe(StripDTO dto)
    {
        var originalBay = dto.bay;
        if (!RequiresEncoding(originalBay) && !HasFreePosition(dto))
        {
            return dto;
        }

        dto.subbay = string.Join(
            "|",
            Marker,
            originalBay.ToString(),
            dto.FreeBayX?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            dto.FreeBayY?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        dto.bay = ToServerSafeBay(originalBay);

        return dto;
    }

    public static void ApplyClientState(StripDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.subbay))
        {
            return;
        }

        var parts = dto.subbay.Split('|');
        if (parts.Length < 2 || !string.Equals(parts[0], Marker, StringComparison.Ordinal))
        {
            return;
        }

        if (Enum.TryParse(parts[1], out StripBay bay))
        {
            dto.bay = bay;
        }

        if (parts.Length > 3 &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            dto.FreeBayX = x;
            dto.FreeBayY = y;
        }
    }

    public static StripBay ToServerSafeBay(StripBay bay)
    {
        return bay switch
        {
            StripBay.BAY_VFR_PENDING => StripBay.BAY_PREA,
            StripBay.BAY_VFR_WEST_NORTH => StripBay.BAY_CIRCUIT,
            StripBay.BAY_VFR_EAST_SOUTH => StripBay.BAY_CIRCUIT,
            _ => bay,
        };
    }

    public static bool ShouldCoordinateWhenMoved(StripBay bay)
    {
        return bay is StripBay.BAY_CLEARED or
            StripBay.BAY_COORDINATOR or
            StripBay.BAY_PUSHED or
            StripBay.BAY_TAXI or
            StripBay.BAY_HOLDSHORT or
            StripBay.BAY_RUNWAY or
            StripBay.BAY_OUT or
            StripBay.BAY_ARRIVAL or
            StripBay.BAY_CIRCUIT;
    }

    private static bool RequiresEncoding(StripBay bay)
    {
        return bay is StripBay.BAY_VFR_PENDING or
            StripBay.BAY_VFR_WEST_NORTH or
            StripBay.BAY_VFR_EAST_SOUTH;
    }

    private static bool HasFreePosition(StripDTO dto)
    {
        return dto.FreeBayX.HasValue && dto.FreeBayY.HasValue;
    }
}
