using System.Numerics;

namespace Content.Server.Rot.Components;

/// <summary>
/// Server-only runtime state for rot audio (movement SFX cadence and last position).
/// </summary>
[RegisterComponent]
public sealed partial class RotAudioStateComponent : Component
{
    [DataField]
    public Vector2 LastWorldPos;

    [DataField]
    public float MoveSfxTimer;
}
