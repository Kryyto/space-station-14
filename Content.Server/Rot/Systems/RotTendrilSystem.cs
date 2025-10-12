using System.Numerics;
using Content.Server.Rot.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Rot;
using Content.Shared.Rot.Components;
using Content.Shared.CombatMode;
using Content.Server.Weapons.Melee;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server.Rot.Systems;

public sealed class RotTendrilSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly MeleeWeaponSystem _melee = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RotGrappleComponent, RotTendrilActionEvent>(OnTendril);
    }

    private void OnTendril(EntityUid uid, RotGrappleComponent comp, RotTendrilActionEvent args)
    {
        args.Handled = true;
        _logManager.GetSawmill("entry").Info($"[RotTendril] Action received by {ToPrettyString(uid)} (range={comp.GrappleRange})");

        // Find nearest valid target within range (simple scan over transforms for now).
        if (!TryComp(uid, out TransformComponent? xform))
            return;

        var range = comp.GrappleRange;
        EntityUid? bestAlive = null;
        var bestAliveDist = float.MaxValue;
        var origin = _xform.GetWorldPosition(xform);
        var originMap = _xform.GetMapId((uid, xform));

        var enumerator = EntityQueryEnumerator<TransformComponent>();
        while (enumerator.MoveNext(out var other, out var oXform))
        {
            if (other == uid)
                continue;
            if (oXform.Anchored)
                continue;
            // Must be on same map as the performer.
            if (_xform.GetMapId((other, oXform)) != originMap)
                continue;
            // Skip parts of self (descendants of performer in transform tree)
            if (IsDescendant(other, uid))
                continue;
            // Must be a living mob target, not machinery, and not an action entity.
            if (!HasComp<MobStateComponent>(other))
                continue;
            if (HasComp<Content.Shared.Actions.Components.ActionComponent>(other))
                continue;
            if (HasComp<Content.Server.Rot.Components.RotGrappleIgnoreComponent>(other))
                continue;
            if (HasComp<RotGrappleComponent>(other))
                continue;
            var pos = _xform.GetWorldPosition(oXform);
            var dist = Vector2.Distance(pos, origin);
            if (dist > range)
                continue;

            // Only select if not dead
            if (_mobState.IsDead(other))
            {
                EnsureComp<Content.Server.Rot.Components.RotGrappleIgnoreComponent>(other);
                continue;
            }

            if (dist < bestAliveDist)
            {
                bestAliveDist = dist;
                bestAlive = other;
            }
        }
        enumerator.Dispose();

        var best = bestAlive;
        if (best is null)
        {
            _logManager.GetSawmill("entry").Info("[RotTendril] No valid target found in range.");
            return;
        }

        _logManager.GetSawmill("entry").Info($"[RotTendril] Latching target {ToPrettyString(best.Value)}");
        // Begin grapple state.
        var state = EnsureComp<RotGrappleStateComponent>(uid);
        state.Target = best;
        state.RemainingSeconds = comp.ResistSeconds;
        state.ElapsedSeconds = 0f;
        state.AttackTimer = comp.AttackInterval;

        // Visuals stay active during grapple.
        EnsureComp<RotTendrilVisualComponent>(uid);
        // Start tendril travel from 0 towards target.
        var vis = Comp<RotTendrilVisualComponent>(uid);
        vis.Active = true;
        vis.Target = best;
        vis.TravelMeters = 0f;
        // Randomize curve parameters for an organic look per grapple
        vis.CurveAmplitudeMeters = _random.NextFloat(0.4f, 1.0f);
        vis.CurveFrequency = _random.NextFloat(0.8f, 1.6f);
        vis.CurvePhase = _random.NextFloat(0f, MathF.Tau);
        vis.CurveFlip = _random.Prob(0.5f);
        Dirty(uid, vis);
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Movement SFX for all rot mobs (independent of grapple state)
        var moveQuery = EntityQueryEnumerator<RotGrappleComponent, TransformComponent>();
        while (moveQuery.MoveNext(out var muid, out var _, out var mxform))
        {
            var maudio = EnsureComp<RotAudioStateComponent>(muid);
            var mpos = _xform.GetWorldPosition(mxform);
            maudio.MoveSfxTimer = MathF.Max(0f, maudio.MoveSfxTimer - frameTime);
            var moved = (mpos - maudio.LastWorldPos).Length() > 0.1f;
            if (moved && maudio.MoveSfxTimer <= 0f)
            {
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Mobs/rot_move.ogg"), muid, AudioParams.Default.WithVolume(2f));
                maudio.MoveSfxTimer = _random.NextFloat(10f, 15f);
            }
            maudio.LastWorldPos = mpos;
        }

        // AI auto-use: for rot entities not currently grappling, periodically try to use tendril.
        var autoQuery = EntityQueryEnumerator<RotGrappleComponent, TransformComponent>();
        while (autoQuery.MoveNext(out var auid, out var acomp, out _))
        {
            // If already grappling, skip
            if (TryComp<RotGrappleStateComponent>(auid, out var st) && st.Target != null)
                continue;

            // Only allow AI (no player actor attached) to auto-use tendril.
            if (HasComp<ActorComponent>(auid))
                continue;

            var ai = EnsureComp<Content.Server.Rot.Components.RotGrappleAiStateComponent>(auid);
            ai.AutoUseTimer -= frameTime;
            if (ai.AutoUseTimer <= 0f)
            {
                ai.AutoUseTimer += MathF.Max(0.2f, acomp.AutoUseInterval);
                // Perform the granted Rot tendril action like a user would.
                TryPerformTendrilAction(auid);
            }
        }

        // Iterate grapples
        var query = EntityQueryEnumerator<RotGrappleComponent, RotGrappleStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var state, out var xform))
        {
            if (state.Target is null || Deleted(state.Target.Value))
            {
                StopGrapple(uid);
                continue;
            }

            // Auto-break only if exceeded max grapple duration
            state.ElapsedSeconds += frameTime;
            if (state.ElapsedSeconds >= comp.MaxGrappleSeconds)
            {
                StopGrapple(uid);
                continue;
            }

            // Validate target
            if (!TryComp(state.Target.Value, out TransformComponent? tXform) || tXform.Anchored ||
                !HasComp<MobStateComponent>(state.Target.Value) ||
                HasComp<Content.Shared.Actions.Components.ActionComponent>(state.Target.Value))
            {
                StopGrapple(uid);
                continue;
            }

            if (HasComp<RotGrappleComponent>(state.Target.Value))
            {
                StopGrapple(uid);
                continue;
            }

            // If target died during grapple, mark ignore and stop
            if (_mobState.IsDead(state.Target.Value))
            {
                EnsureComp<Content.Server.Rot.Components.RotGrappleIgnoreComponent>(state.Target.Value);
                StopGrapple(uid);
                continue;
            }

            // Also ensure same map during grapple.
            if (_xform.GetMapId((state.Target.Value, tXform)) != _xform.GetMapId((uid, xform)))
            {
                StopGrapple(uid);
                continue;
            }

            // Target should not be a descendant (e.g., torso) of the performer.
            if (IsDescendant(state.Target.Value, uid))
            {
                StopGrapple(uid);
                continue;
            }

            var start = _xform.GetWorldPosition(xform);
            var end = _xform.GetWorldPosition(tXform);
            var delta = end - start;
            var dist = delta.Length();

            // Enforce max tendril length: break and retract if exceeded.
            if (dist > comp.GrappleRange)
            {
                StopGrapple(uid);
                continue;
            }

            // Update tendril travel: grow towards target at 5 tiles/sec until reaching current distance.
            var v = Comp<RotTendrilVisualComponent>(uid);
            var travelSpeed = 5f;
            if (v.TravelMeters < dist)
            {
                v.TravelMeters = MathF.Min(dist, v.TravelMeters + travelSpeed * frameTime);
                Dirty(uid, v);
            }

            // Tick burst cooldown regardless of phase so it's ready on arrival
            state.AttackTimer = MathF.Max(0f, state.AttackTimer - frameTime);

            // If the tendril hasn't reached the target yet, don't reel or deal damage.
            if (v.TravelMeters < dist)
                continue;

            // Reel target toward mob until within threshold.
            var wasConsume = state.InConsume;
            state.InConsume = dist <= comp.ConsumeThreshold;
            if (dist > comp.ConsumeThreshold)
            {
                var dir = delta / MathF.Max(dist, 0.0001f);
                var step = comp.ReelSpeed * frameTime;
                var newPos = end - dir * MathF.Min(step, dist - comp.ConsumeThreshold);
                _xform.SetWorldPosition((state.Target.Value, tXform), newPos);
            }
            else
            {
                // Consume: apply DPS while in threshold.
                var spec = new DamageSpecifier();
                spec.DamageDict["Brute"] = FixedPoint2.New(comp.DpsBrute * frameTime);
                spec.DamageDict["Cellular"] = FixedPoint2.New(comp.DpsCellular * frameTime);
                _damage.TryChangeDamage(state.Target.Value, spec);

                // Devour SFX on entering consume, and periodically thereafter.
                state.DevourSfxTimer = MathF.Max(0f, state.DevourSfxTimer - frameTime);
                if (!wasConsume || state.DevourSfxTimer <= 0f)
                {
                    _audio.PlayPvs(new SoundPathSpecifier("/Audio/Mobs/rot_devour.ogg"), uid, AudioParams.Default.WithVolume(2f));
                    state.DevourSfxTimer = _random.NextFloat(2f, 4f);
                }

                // Melee swing at configured rate while grabbing (default 2 Hz).
                state.SwingTimer -= frameTime;
                if (state.SwingTimer <= 0f)
                {
                    state.SwingTimer += MathF.Max(0.05f, 1f / MathF.Max(0.1f, comp.AttackRateWhileGrab));

                    // Ensure combat mode for attack attempts
                    _combat.SetInCombatMode(uid, true);

                    // Attempt light attack with whatever melee this entity has
                    if (_melee.TryGetWeapon(uid, out var weaponUid, out var weapon))
                    {
                        _melee.AttemptLightAttack(uid, weaponUid, weapon, state.Target.Value);
                    }
                }
            }

            // Keep visual updated
            SetVisual(uid, state.Target.Value, true);
        }
    }

    private void StopGrapple(EntityUid uid)
    {
        if (TryComp<RotGrappleStateComponent>(uid, out var state))
        {
            state.Target = null;
            state.RemainingSeconds = 0f;
        }

        // Clear visual
        SetVisual(uid, null, false);
    }

    private void SetVisual(EntityUid owner, EntityUid? target, bool active)
    {
        EnsureComp<RotTendrilVisualComponent>(owner);
        var vis = Comp<RotTendrilVisualComponent>(owner);
        vis.Active = active;
        vis.Target = target;
        Dirty(owner, vis);
    }

    private bool IsDescendant(EntityUid child, EntityUid possibleAncestor)
    {
        if (child == possibleAncestor)
            return true;

        if (!TryComp(child, out TransformComponent? xform))
            return false;

        var guard = 0;
        while (xform.ParentUid.IsValid() && guard++ < 256)
        {
            if (xform.ParentUid == possibleAncestor)
                return true;

            if (!TryComp(xform.ParentUid, out xform))
                break;
        }

        return false;
    }

    private bool TryPerformTendrilAction(EntityUid user)
    {
        if (!TryComp<ActionsComponent>(user, out var acts))
            return false;

        foreach (var action in _actions.GetActions(user, acts))
        {
            // We want the InstantAction whose event is RotTendrilActionEvent
            if (TryComp<InstantActionComponent>(action.Owner, out var instant) && instant.Event is RotTendrilActionEvent)
            {
                _actions.PerformAction((user, acts), action);
                return true;
            }
        }

        return false;
    }
}
