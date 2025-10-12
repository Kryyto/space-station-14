using Content.Client.Rot.Overlays;
using Robust.Client.Graphics;

namespace Content.Client.Rot;

public sealed class RotClientSystem : EntitySystem
{
    private IOverlayManager? _overlayMan;
    private RotTendrilOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();
        _overlayMan = IoCManager.Resolve<IOverlayManager>();
        _overlay = new RotTendrilOverlay(EntityManager);
        _overlayMan.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_overlay != null)
            _overlayMan?.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
