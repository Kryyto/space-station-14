using System.Numerics;
using Content.Shared.Rot.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.Rot.Overlays;

public sealed class RotTendrilOverlay(IEntityManager entMan) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly IEntityManager _entMan = entMan;
    private Texture? _tendrilTexture;
    private static readonly SpriteSpecifier.Texture TendrilSprite = new(new ResPath("/Textures/Effects/Rot/tendril.rsi/tendril.png"));

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entMan.EntityQueryEnumerator<RotTendrilVisualComponent, TransformComponent>();
        var xformSys = _entMan.System<TransformSystem>();

        var baseColor = new Color(180, 180, 180, 255);
        while (query.MoveNext(out _, out var vis, out var xform))
        {
            if (!vis.Active || vis.Target is null)
                continue;

            if (!_entMan.TryGetComponent<TransformComponent>(vis.Target.Value, out var tXform))
                continue;

            var start = xformSys.GetWorldPosition(xform);
            var end = xformSys.GetWorldPosition(tXform);

            // Ensure texture loaded
            _tendrilTexture ??= _entMan.System<SpriteSystem>().Frame0(TendrilSprite);

            // Segmented tendril: draw textured quads along the path.
            var delta = end - start;
            var length = delta.Length();
            if (length <= 0.001f)
                continue;

            var dir = delta / length;
            var normal = new Vector2(-dir.Y, dir.X);
            // Visual parameters
            var segmentLen = 0.80f; // tiles per segment (matches 1 tile)
            var thickness = 0.80f;  // tiles thickness

            // Only draw up to TravelMeters from owner towards target
            var drawLength = MathF.Min(length, vis.TravelMeters);
            if (drawLength <= 0f)
                continue;

            // Curved path helpers
            float OffsetAt(float s)
            {
                // Organic bend using networked parameters with end taper
                var amp = vis.CurveAmplitudeMeters;
                var freq = vis.CurveFrequency; // waves along full length
                var phase = vis.CurvePhase;
                var flip = vis.CurveFlip ? -1f : 1f;
                var taper = 1f - MathF.Pow(2f * s - 1f, 2f); // bell-shaped [0..1]
                return flip * amp * MathF.Sin(MathF.PI * freq * s + phase) * taper;
            }

            Vector2 PointAt(float s)
            {
                var baseP = start + dir * (drawLength * s);
                return baseP + normal * OffsetAt(s);
            }

            // Iterate segments along parametric s based on straight-line spacing
            var totalSegments = (int)MathF.Ceiling(drawLength / segmentLen);
            for (var i = 0; i < totalSegments; i++)
            {
                var s0 = MathF.Min(1f, i * segmentLen / drawLength);
                var s1 = MathF.Min(1f, (i + 1) * segmentLen / drawLength);
                if (s1 <= s0 + 0.0001f)
                    break;

                var p0 = PointAt(s0);
                var p1 = PointAt(s1);
                var tangent = p1 - p0;
                var segWorldLen = tangent.Length();
                if (segWorldLen <= 0.0001f)
                    continue;

                var center = (p0 + p1) * 0.5f;
                var angle = Angle.FromWorldVec(tangent) + Angle.FromDegrees(90);

                // Build world quad using true world length to avoid stretching
                var half = new Vector2(segWorldLen, thickness) * 0.5f;
                var quad = new Box2(center - half, center + half);

                // Texture sub-region: clamp last partial segment from the target side
                var texSize = _tendrilTexture!.Size;
                var u = (s1 - s0) * (drawLength / segmentLen); // fraction of a full segment this slice represents
                var px = MathF.Round(texSize.X * MathHelper.Clamp(u, 0f, 1f));
                var sub = new UIBox2((int)(texSize.X - px), 0, texSize.X, texSize.Y);

                handle.DrawTextureRectRegion(_tendrilTexture!, new Box2Rotated(quad, angle, center), baseColor, sub);
            }
        }
    }
}
