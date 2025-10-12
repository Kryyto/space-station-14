using Robust.Shared.GameStates;

namespace Content.Server.Rot.Components;

/// <summary>
/// Server-side grapple state for a rot mob. Tracks the current target and timers.
/// </summary>
[RegisterComponent]
public sealed partial class RotGrappleStateComponent : Component
{
    // Current grapple target.
    [DataField]
    public EntityUid? Target;

    // Deprecated: remaining seconds; not used for auto-break anymore.
    [DataField]
    public float RemainingSeconds;

    // Elapsed grapple time (seconds) for auto-break against MaxGrappleSeconds.
    [DataField]
    public float ElapsedSeconds;

    // Internal cooldown for burst attacks.
    [DataField]
    public float AttackTimer;

    // Internal timer for melee swing cadence while grabbing.
    [DataField]
    public float SwingTimer;

    // Whether we are currently within consume threshold this tick (server-only runtime state).
    [DataField]
    public bool InConsume;

    // Cooldown for devour sfx while consuming.
    [DataField]
    public float DevourSfxTimer;
}
