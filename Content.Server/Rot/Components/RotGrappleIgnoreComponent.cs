namespace Content.Server.Rot.Components;

/// <summary>
/// Marker: targets with this component should never be selected by rot tendril again.
/// Added when a previously grabbed living target dies.
/// </summary>
[RegisterComponent]
public sealed partial class RotGrappleIgnoreComponent : Component
{
}
