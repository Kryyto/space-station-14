namespace Content.Server.Rot.Components;

/// <summary>
/// Server-side AI timer for rot tendril auto-use when idle.
/// </summary>
[RegisterComponent]
public sealed partial class RotGrappleAiStateComponent : Component
{
    // Next time in seconds until trying to auto-use tendril on a target.
    [DataField]
    public float AutoUseTimer;
}
