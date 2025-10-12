using Robust.Shared.GameStates;

namespace Content.Shared.Rot.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class RotGrappleComponent : Component
{
    // Max tendril range in tiles
    [DataField] public float GrappleRange = 1f;

    // Reel speed in tiles/sec
    [DataField] public float ReelSpeed = 4f;

    // Distance to start consume phase
    [DataField] public float ConsumeThreshold = 0.75f;

    // Damage per second while consuming
    [DataField] public float DpsBrute = 7f;
    [DataField] public float DpsCellular = 3f;

    // Resist time (seconds) used for resist logic (does NOT auto-break by itself)
    [DataField] public float ResistSeconds = 2f;

    // Maximum grapple duration in seconds before the tendril auto-breaks (independent of resist)
    [DataField] public float MaxGrappleSeconds = 20f;

    // Burst attack while target is within threshold
    [DataField] public float AttackInterval = 0.5f; // seconds between burst attacks
    [DataField] public float AttackBrute = 3f;      // brute per burst
    [DataField] public float AttackCellular = 2f;   // cellular per burst

    // While grabbing, swing normal melee attacks at this rate (attacks per second)
    [DataField] public float AttackRateWhileGrab = 2.0f;

    // How often the AI should try to use the tendril action when idle (seconds)
    [DataField] public float AutoUseInterval = 1.0f;
}
