using Robust.Shared.GameStates;

namespace Content.Shared.Rot.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RotTendrilVisualComponent : Component
{
    // Whether to render the tendril.
    [DataField, AutoNetworkedField]
    public bool Active;

    // The current grapple target, if any.
    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    // How far from the owner toward the target to render the tendril (in tiles/meters).
    [DataField, AutoNetworkedField]
    public float TravelMeters;

    // Curve amplitude in world meters for visual bend.
    [DataField, AutoNetworkedField]
    public float CurveAmplitudeMeters = 0.1f;

    // Curve frequency in waves across the full length.
    [DataField, AutoNetworkedField]
    public float CurveFrequency = 0.5f;

    // Curve phase in radians.
    [DataField, AutoNetworkedField]
    public float CurvePhase = 0f;

    // If true, flip the normal direction for the bend.
    [DataField, AutoNetworkedField]
    public bool CurveFlip = false;
}
