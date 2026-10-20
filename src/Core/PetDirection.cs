namespace PetTracker;

// Transient presentation input, never collection data. A navigation producer must
// first validate position and nearest live entity. Metres are optional and need
// their own calibration. Bearing is
// clockwise from CHARACTER facing by default, or map north when NorthUp is set.
// SkillBased identifies the opt-in last-skill-facing test producer; it is not
// continuous facing and must not be presented as camera-relative guidance.
public sealed record PetDirection(
    double? DistanceMetres,
    DateTimeOffset PositionUtc,
    double? BearingDegrees = null,
    DateTimeOffset? FacingUtc = null,
    bool SkillBased = false,
    bool NorthUp = false)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(2);
    public bool HasDistance(DateTimeOffset now) => DistanceMetres is double metres && double.IsFinite(metres) && metres >= 0 && Fresh(PositionUtc, now);
    public double? Bearing(DateTimeOffset now) => Fresh(PositionUtc, now) && (DistanceMetres == null || HasDistance(now) && DistanceMetres > 0) && BearingDegrees is double angle && double.IsFinite(angle) && (NorthUp || FacingUtc is { } facing && Fresh(facing, now)) ? (angle % 360 + 360) % 360 : null;
    static bool Fresh(DateTimeOffset measured, DateTimeOffset now) => measured <= now && now - measured < Lifetime;
}
