using System.Linq;
using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Radar;

/// <summary>
/// The shape of the radar blip.
/// </summary>
[Serializable, NetSerializable]
public enum RadarBlipShape
{
    /// <summary>Circle shape.</summary>
    Circle,
    /// <summary>Square shape.</summary>
    Square,
    /// <summary>Triangle shape.</summary>
    Triangle,
    /// <summary>Star shape.</summary>
    Star,
    /// <summary>Diamond shape.</summary>
    Diamond,
    /// <summary>Hexagon shape.</summary>
    Hexagon,
    /// <summary>Arrow shape.</summary>
    Arrow,
    /// <summary>Ring shape (custom/Mono).</summary>
    Ring
}

/// <summary>
/// Event sent from the server to the client containing radar blip and hitscan line data.
/// </summary>
[Serializable, NetSerializable]
public sealed class GiveBlipsEvent : EntityEventArgs
{
    /// <summary>
    /// Blips are now (grid entity, position, scale, color, shape).
    /// If grid entity is null, position is in world coordinates.
    /// If grid entity is not null, position is in grid-local coordinates.
    /// </summary>
    public readonly List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> Blips;

    /// <summary>
    /// Hitscan lines to display on the radar as (grid entity, start position, end position, thickness, color).
    /// If grid entity is null, positions are in world coordinates.
    /// If grid entity is not null, positions are in grid-local coordinates.
    /// </summary>
    public readonly List<(NetEntity? Grid, Vector2 Start, Vector2 End, float Thickness, Color Color)> HitscanLines;

    /// <summary>
    /// Backwards-compatible constructor for legacy blip format.
    /// </summary>
    /// <param name="blips">List of blips as (position, scale, color).</param>
    public GiveBlipsEvent(List<(Vector2, float, Color)> blips)
    {
        Blips = blips.Select(b => ((NetEntity?)null, b.Item1, b.Item2, b.Item3, RadarBlipShape.Circle)).ToList();
        HitscanLines = new();
    }

    /// <summary>
    /// Constructor for the full blip format.
    /// </summary>
    /// <param name="blips">List of blips as (grid, position, scale, color, shape).</param>
    public GiveBlipsEvent(List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> blips)
    {
        Blips = blips;
        HitscanLines = new();
    }

    /// <summary>
    /// Constructor for full format with hitscan lines.
    /// </summary>
    /// <param name="blips">List of blips as (grid, position, scale, color, shape).</param>
    /// <param name="hitscans">List of hitscan lines as (grid, start, end, thickness, color).</param>
    public GiveBlipsEvent(
        List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> blips,
        List<(NetEntity? Grid, Vector2 Start, Vector2 End, float Thickness, Color Color)> hitscans)
    {
        Blips = blips;
        HitscanLines = hitscans;
    }
}

/// <summary>
/// A request for radar blips around a given entity.
/// Entity must have the RadarConsoleComponent to receive a response.
/// Requests are rate-limited server-side, unhandled messages will not receive a response.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestBlipsEvent : EntityEventArgs
{
    /// <summary>
    /// The radar entity for which blips are being requested.
    /// </summary>
    public readonly NetEntity Radar;

    /// <summary>
    /// Constructor for RequestBlipsEvent.
    /// </summary>
    /// <param name="radar">The radar entity.</param>
    public RequestBlipsEvent(NetEntity radar)
    {
        Radar = radar;
    }
}
