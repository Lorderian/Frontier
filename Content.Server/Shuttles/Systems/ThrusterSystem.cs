using System.Numerics;
using Content.Server.Audio;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Content.Shared.Temperature;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Shared.Localizations;
using Content.Shared.Power;
using Content.Server.Construction; // Frontier
using Content.Server.Construction.Components; // Frontier
using Content.Shared.DeviceLinking.Events; // Frontier

namespace Content.Server.Shuttles.Systems;

public sealed class ThrusterSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly AmbientSoundSystem _ambient = default!;
    [Dependency] private readonly FixtureSystem _fixtureSystem = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ConstructionSystem _construction = default!; // Frontier

    // Essentially whenever thruster enables we update the shuttle's available impulses which are used for movement.
    // This is done for each direction available.

    public const string BurnFixture = "thruster-burn";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThrusterComponent, ActivateInWorldEvent>(OnActivateThruster);
        SubscribeLocalEvent<ThrusterComponent, ComponentInit>(OnThrusterInit);
        SubscribeLocalEvent<ThrusterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ThrusterComponent, ComponentShutdown>(OnThrusterShutdown);
        SubscribeLocalEvent<ThrusterComponent, PowerChangedEvent>(OnPowerChange);
        SubscribeLocalEvent<ThrusterComponent, AnchorStateChangedEvent>(OnAnchorChange);
        SubscribeLocalEvent<ThrusterComponent, MoveEvent>(OnRotate);
        SubscribeLocalEvent<ThrusterComponent, IsHotEvent>(OnIsHotEvent);
        SubscribeLocalEvent<ThrusterComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ThrusterComponent, EndCollideEvent>(OnEndCollide);

        SubscribeLocalEvent<ThrusterComponent, ExaminedEvent>(OnThrusterExamine);

        SubscribeLocalEvent<ShuttleComponent, TileChangedEvent>(OnShuttleTileChange);

        SubscribeLocalEvent<ThrusterComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<ThrusterComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        SubscribeLocalEvent<ThrusterComponent, SignalReceivedEvent>(OnSignalReceived); // Frontier
    }

    // Frontier: signal handler
    private void OnSignalReceived(EntityUid uid, ThrusterComponent component, ref SignalReceivedEvent args)
    {
        if (args.Port == component.OffPort)
            component.Enabled = false;
        else if (args.Port == component.OnPort)
            component.Enabled = true;
        else if (args.Port == component.TogglePort)
            component.Enabled ^= true;
        else
            return; // Invalid port, don't change the thruster.

        if (!component.Enabled)
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != 0 && apcPower.Load != 1)
                apcPower.Load = 1;
            DisableThruster(uid, component);
        }
        else if (CanEnable(uid, component))
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != apcPower.Load)
                apcPower.Load = component.OriginalLoad;
            EnableThruster(uid, component);
        }
    }
    // End Frontier: signal handler

    private void OnThrusterExamine(EntityUid uid, ThrusterComponent component, ExaminedEvent args)
    {
        // Powered is already handled by other power components
        var enabled = Loc.GetString(component.Enabled ? "thruster-comp-enabled" : "thruster-comp-disabled");

        using (args.PushGroup(nameof(ThrusterComponent)))
        {
            args.PushMarkup(enabled);

            if (component.Type == ThrusterType.Linear &&
                EntityManager.TryGetComponent(uid, out TransformComponent? xform) &&
                xform.Anchored)
            {
                var nozzleLocalization = ContentLocalizationManager.FormatDirection(xform.LocalRotation.Opposite().ToWorldVec().GetDir()).ToLower();
                var nozzleDir = Loc.GetString("thruster-comp-nozzle-direction",
                    ("direction", nozzleLocalization));

                args.PushMarkup(nozzleDir);

                var exposed = NozzleExposed(xform);

                var nozzleText =
                    Loc.GetString(exposed ? "thruster-comp-nozzle-exposed" : "thruster-comp-nozzle-not-exposed");

                args.PushMarkup(nozzleText);
            }
        }
    }

    private void OnIsHotEvent(EntityUid uid, ThrusterComponent component, IsHotEvent args)
    {
        args.IsHot = component.Type != ThrusterType.Angular && component.IsOn;
    }

    // Forge-Change-Start

    private void OnShuttleTileChange(EntityUid uid, ShuttleComponent component, ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            // If the old tile was space but the new one isn't then disable all adjacent thrusters
            if (change.NewTile.IsSpace(_tileDefManager) || !change.OldTile.IsSpace(_tileDefManager))
                continue;

            var tilePos = change.GridIndices;
            var grid = Comp<MapGridComponent>(uid);
            var xformQuery = GetEntityQuery<TransformComponent>();
            var thrusterQuery = GetEntityQuery<ThrusterComponent>();

            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x != 0 && y != 0)
                        continue;

                    var checkPos = tilePos + new Vector2i(x, y);
                    var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(uid, grid, checkPos);

                    while (enumerator.MoveNext(out var ent))
                    {
                        if (!thrusterQuery.TryGetComponent(ent.Value, out var thruster) || !thruster.RequireSpace)
                            continue;

                        // Work out if the thruster is facing this direction
                        var xform = xformQuery.GetComponent(ent.Value);
                        var direction = xform.LocalRotation.ToWorldVec();

                        if (new Vector2i((int)direction.X, (int)direction.Y) != new Vector2i(x, y))
                            continue;

                        DisableThruster(ent.Value, thruster, xform.GridUid);
                    }
                }
            }
        }

    }

    // Forge-Change-End

    private void OnActivateThruster(EntityUid uid, ThrusterComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        component.Enabled ^= true;

        if (!component.Enabled)
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != 0 && apcPower.Load != 1) // Frontier
                apcPower.Load = 1;  // Frontier
            DisableThruster(uid, component);
            args.Handled = true;
        }
        else if (CanEnable(uid, component))
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != apcPower.Load) // Frontier
                apcPower.Load = component.OriginalLoad; // Frontier
            EnableThruster(uid, component);
            args.Handled = true;
        }
    }

    /// <summary>
    /// If the thruster rotates change the direction where the linear thrust is applied
    /// </summary>
    private void OnRotate(EntityUid uid, ThrusterComponent component, ref MoveEvent args)
    {
        // TODO: Disable visualizer for old direction
        // TODO: Don't make them rotatable and make it require anchoring.

        if (!component.Enabled ||
            !EntityManager.TryGetComponent(uid, out TransformComponent? xform) ||
            !EntityManager.TryGetComponent(xform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            return;
        }

        var canEnable = CanEnable(uid, component);

        // If it's not on then don't enable it inadvertantly (given we don't have an old rotation)
        if (!canEnable && !component.IsOn)
            return;

        // Enable it if it was turned off but new tile is valid
        if (!component.IsOn && canEnable)
        {
            EnableThruster(uid, component);
            return;
        }

        // Disable if new tile invalid
        if (component.IsOn && !canEnable)
        {
            DisableThruster(uid, component, args.OldPosition.EntityId, xform, args.OldRotation);
            return;
        }

        var oldDirection = (int)args.OldRotation.GetCardinalDir() / 2;
        var direction = (int)args.NewRotation.GetCardinalDir() / 2;
        var oldShuttleComponent = shuttleComponent;

        if (args.ParentChanged)
        {
            oldShuttleComponent = Comp<ShuttleComponent>(args.OldPosition.EntityId);

            // If no parent change doesn't matter for angular.
            if (component.Type == ThrusterType.Angular)
            {
                oldShuttleComponent.AngularThrust -= component.Thrust;
                DebugTools.Assert(oldShuttleComponent.AngularThrusters.Contains(uid));
                oldShuttleComponent.AngularThrusters.Remove(uid);

                shuttleComponent.AngularThrust += component.Thrust;
                DebugTools.Assert(!shuttleComponent.AngularThrusters.Contains(uid));
                shuttleComponent.AngularThrusters.Add(uid);
                return;
            }
        }

        if (component.Type == ThrusterType.Linear)
        {
            oldShuttleComponent.LinearThrust[oldDirection] -= component.Thrust;
            oldShuttleComponent.BaseLinearThrust[oldDirection] -= component.BaseThrust;
            DebugTools.Assert(oldShuttleComponent.LinearThrusters[oldDirection].Contains(uid));
            oldShuttleComponent.LinearThrusters[oldDirection].Remove(uid);

            shuttleComponent.LinearThrust[direction] += component.Thrust;
            shuttleComponent.BaseLinearThrust[direction] += component.BaseThrust;
            DebugTools.Assert(!shuttleComponent.LinearThrusters[direction].Contains(uid));
            shuttleComponent.LinearThrusters[direction].Add(uid);
        }
    }

    private void OnAnchorChange(EntityUid uid, ThrusterComponent component, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored && CanEnable(uid, component))
        {
            EnableThruster(uid, component);
        }
        else
        {
            DisableThruster(uid, component);
        }
    }

    private void OnThrusterInit(EntityUid uid, ThrusterComponent component, ComponentInit args)
    {
        // Frontier: togglable thrusters
        if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad == 0)
        {
            component.OriginalLoad = apcPower.Load;
        }
        // End Frontier: togglable thrusters

        _ambient.SetAmbience(uid, false);

        if (!component.Enabled)
        {
            return;
        }

        if (CanEnable(uid, component))
        {
            EnableThruster(uid, component);
        }
    }

    private void OnMapInit(Entity<ThrusterComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextFire = _timing.CurTime + ent.Comp.FireCooldown;
        // Frontier: upgradeable parts
        if (TryComp<MachineComponent>(ent, out var machineComp))
            _construction.RefreshParts(ent, machineComp);
        // End Frontier: upgradeable parts
    }

    private void OnThrusterShutdown(EntityUid uid, ThrusterComponent component, ComponentShutdown args)
    {
        DisableThruster(uid, component);
    }

    private void OnPowerChange(EntityUid uid, ThrusterComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered && CanEnable(uid, component))
        {
            EnableThruster(uid, component);
        }
        else
        {
            DisableThruster(uid, component);
        }
    }

    /// <summary>
    /// Tries to enable the thruster and turn it on. If it's already enabled it does nothing.
    /// </summary>
    public void EnableThruster(EntityUid uid, ThrusterComponent component, TransformComponent? xform = null)
    {
        if (component.IsOn ||
            !Resolve(uid, ref xform))
        {
            return;
        }

        component.IsOn = true;

        if (!EntityManager.TryGetComponent(xform.GridUid, out ShuttleComponent? shuttleComponent))
            return;

        // Logger.DebugS("thruster", $"Enabled thruster {uid}");

        switch (component.Type)
        {
            case ThrusterType.Linear:
                var direction = (int)xform.LocalRotation.GetCardinalDir() / 2;

                shuttleComponent.LinearThrust[direction] += component.Thrust;
                shuttleComponent.BaseLinearThrust[direction] += component.BaseThrust;
                DebugTools.Assert(!shuttleComponent.LinearThrusters[direction].Contains(uid));
                shuttleComponent.LinearThrusters[direction].Add(uid);

                // Don't just add / remove the fixture whenever the thruster fires because perf
                if (EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent) &&
                    component.BurnPoly.Count > 0)
                {
                    var shape = new PolygonShape();
                    shape.Set(component.BurnPoly);
                    _fixtureSystem.TryCreateFixture(uid, shape, BurnFixture, hard: false, collisionLayer: (int)CollisionGroup.FullTileMask, body: physicsComponent);
                }

                break;
            case ThrusterType.Angular:
                shuttleComponent.AngularThrust += component.Thrust;
                DebugTools.Assert(!shuttleComponent.AngularThrusters.Contains(uid));
                shuttleComponent.AngularThrusters.Add(uid);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (EntityManager.TryGetComponent(uid, out AppearanceComponent? appearance))
        {
            _appearance.SetData(uid, ThrusterVisualState.State, true, appearance);
        }

        if (_light.TryGetLight(uid, out var pointLightComponent))
        {
            _light.SetEnabled(uid, true, pointLightComponent);
        }

        _ambient.SetAmbience(uid, true);
        RefreshCenter(uid, shuttleComponent);
    }

    /// <summary>
    /// Refreshes the center of thrust for movement calculations.
    /// </summary>
    private void RefreshCenter(EntityUid uid, ShuttleComponent shuttle)
    {
        // TODO: Only refresh relevant directions.
        var center = Vector2.Zero;
        var thrustQuery = GetEntityQuery<ThrusterComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var dir in new[]
                     { Direction.South, Direction.East, Direction.North, Direction.West })
        {
            var index = (int)dir / 2;
            var pop = shuttle.LinearThrusters[index];
            var totalThrust = 0f;

            foreach (var ent in pop)
            {
                if (!thrustQuery.TryGetComponent(ent, out var thruster) || !xformQuery.TryGetComponent(ent, out var xform))
                    continue;

                center += xform.LocalPosition * thruster.Thrust;
                totalThrust += thruster.Thrust;
            }

            center /= pop.Count * totalThrust;
            shuttle.CenterOfThrust[index] = center;
        }
    }

    public void DisableThruster(EntityUid uid, ThrusterComponent component, TransformComponent? xform = null, Angle? angle = null)
    {
        if (!Resolve(uid, ref xform)) return;
        DisableThruster(uid, component, xform.GridUid, xform);
    }

    /// <summary>
    /// Tries to disable the thruster.
    /// </summary>
    public void DisableThruster(EntityUid uid, ThrusterComponent component, EntityUid? gridId, TransformComponent? xform = null, Angle? angle = null)
    {
        if (!component.IsOn ||
            !Resolve(uid, ref xform))
        {
            return;
        }

        component.IsOn = false;

        if (!EntityManager.TryGetComponent(gridId, out ShuttleComponent? shuttleComponent))
            return;

        // Logger.DebugS("thruster", $"Disabled thruster {uid}");

        switch (component.Type)
        {
            case ThrusterType.Linear:
                angle ??= xform.LocalRotation;
                var direction = (int)angle.Value.GetCardinalDir() / 2;

                shuttleComponent.LinearThrust[direction] -= component.Thrust;
                shuttleComponent.BaseLinearThrust[direction] -= component.BaseThrust;
                DebugTools.Assert(shuttleComponent.LinearThrusters[direction].Contains(uid));
                shuttleComponent.LinearThrusters[direction].Remove(uid);
                break;
            case ThrusterType.Angular:
                shuttleComponent.AngularThrust -= component.Thrust;
                DebugTools.Assert(shuttleComponent.AngularThrusters.Contains(uid));
                shuttleComponent.AngularThrusters.Remove(uid);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (EntityManager.TryGetComponent(uid, out AppearanceComponent? appearance))
        {
            _appearance.SetData(uid, ThrusterVisualState.State, false, appearance);
        }

        if (_light.TryGetLight(uid, out var pointLightComponent))
        {
            _light.SetEnabled(uid, false, pointLightComponent);
        }

        _ambient.SetAmbience(uid, false);

        if (EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent))
        {
            _fixtureSystem.DestroyFixture(uid, BurnFixture, body: physicsComponent);
        }

        component.Colliding.Clear();
        RefreshCenter(uid, shuttleComponent);
    }

    public bool CanEnable(EntityUid uid, ThrusterComponent component)
    {
        if (!component.Enabled)
            return false;

        if (component.LifeStage > ComponentLifeStage.Running)
            return false;

        var xform = Transform(uid);

        if (!xform.Anchored || !this.IsPowered(uid, EntityManager))
        {
            return false;
        }

        if (!component.RequireSpace)
            return true;

        return NozzleExposed(xform);
    }

    private bool NozzleExposed(TransformComponent xform)
    {
        if (xform.GridUid == null)
            return true;

        var (x, y) = xform.LocalPosition + xform.LocalRotation.Opposite().ToWorldVec();
        var mapGrid = Comp<MapGridComponent>(xform.GridUid.Value);
        var tile = _mapSystem.GetTileRef(xform.GridUid.Value, mapGrid, new Vector2i((int)Math.Floor(x), (int)Math.Floor(y)));

        return tile.Tile.IsSpace();
    }

    #region Burning

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ThrusterComponent>();
        var curTime = _timing.CurTime;

        while (query.MoveNext(out var comp))
        {
            if (comp.NextFire > curTime)
                continue;

            comp.NextFire += comp.FireCooldown;

            if (!comp.Firing || comp.Colliding.Count == 0 || comp.Damage == null)
                continue;

            foreach (var uid in comp.Colliding.ToArray())
            {
                _damageable.TryChangeDamage(uid, comp.Damage);
            }
        }
    }

    private void OnStartCollide(EntityUid uid, ThrusterComponent component, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != BurnFixture)
            return;

        component.Colliding.Add(args.OtherEntity);
    }

    private void OnEndCollide(EntityUid uid, ThrusterComponent component, ref EndCollideEvent args)
    {
        if (args.OurFixtureId != BurnFixture)
            return;

        component.Colliding.Remove(args.OtherEntity);
    }

    /// <summary>
    /// Considers a thrust direction as being active.
    /// </summary>
    public void EnableLinearThrustDirection(ShuttleComponent component, DirectionFlag direction)
    {
        if ((component.ThrustDirections & direction) != 0x0)
            return;

        component.ThrustDirections |= direction;

        var index = GetFlagIndex(direction);
        var appearanceQuery = GetEntityQuery<AppearanceComponent>();
        var thrusterQuery = GetEntityQuery<ThrusterComponent>();

        foreach (var uid in component.LinearThrusters[index])
        {
            if (!thrusterQuery.TryGetComponent(uid, out var comp))
                continue;

            comp.Firing = true;
            appearanceQuery.TryGetComponent(uid, out var appearance);
            _appearance.SetData(uid, ThrusterVisualState.Thrusting, true, appearance);
        }
    }

    /// <summary>
    /// Disables a thrust direction.
    /// </summary>
    public void DisableLinearThrustDirection(ShuttleComponent component, DirectionFlag direction)
    {
        if ((component.ThrustDirections & direction) == 0x0)
            return;

        component.ThrustDirections &= ~direction;

        var index = GetFlagIndex(direction);
        var appearanceQuery = GetEntityQuery<AppearanceComponent>();
        var thrusterQuery = GetEntityQuery<ThrusterComponent>();

        foreach (var uid in component.LinearThrusters[index])
        {
            if (!thrusterQuery.TryGetComponent(uid, out var comp))
                continue;

            appearanceQuery.TryGetComponent(uid, out var appearance);
            comp.Firing = false;
            _appearance.SetData(uid, ThrusterVisualState.Thrusting, false, appearance);
        }
    }

    public void DisableLinearThrusters(ShuttleComponent component)
    {
        foreach (DirectionFlag dir in Enum.GetValues(typeof(DirectionFlag)))
        {
            DisableLinearThrustDirection(component, dir);
        }

        DebugTools.Assert(component.ThrustDirections == DirectionFlag.None);
    }

    public void SetAngularThrust(ShuttleComponent component, bool on)
    {
        var appearanceQuery = GetEntityQuery<AppearanceComponent>();
        var thrusterQuery = GetEntityQuery<ThrusterComponent>();

        if (on)
        {
            foreach (var uid in component.AngularThrusters)
            {
                if (!thrusterQuery.TryGetComponent(uid, out var comp))
                    continue;

                appearanceQuery.TryGetComponent(uid, out var appearance);
                comp.Firing = true;
                _appearance.SetData(uid, ThrusterVisualState.Thrusting, true, appearance);
            }
        }
        else
        {
            foreach (var uid in component.AngularThrusters)
            {
                if (!thrusterQuery.TryGetComponent(uid, out var comp))
                    continue;

                appearanceQuery.TryGetComponent(uid, out var appearance);
                comp.Firing = false;
                _appearance.SetData(uid, ThrusterVisualState.Thrusting, false, appearance);
            }
        }
    }

    // Frontier: upgradeable machine parts, separate EMP handler
    private void OnRefreshParts(EntityUid uid, ThrusterComponent component, RefreshPartsEvent args)
    {
        if (component.IsOn) // safely disable thruster to prevent negative thrust
            DisableThruster(uid, component);

        var thrustRating = args.PartRatings[component.MachinePartThrust];

        if (component.ThrustPerPartLevel.Length <= 0)
            component.Thrust = component.BaseThrust;
        else if (thrustRating <= 1)
            component.Thrust = component.ThrustPerPartLevel[0];
        else if (thrustRating > component.ThrustPerPartLevel.Length)
            component.Thrust = component.ThrustPerPartLevel[^1];
        else
        {
            var idx = (int)thrustRating - 1;
            component.Thrust = component.ThrustPerPartLevel[idx];
            // Linearly interpolate if fractional
            if (idx < component.ThrustPerPartLevel.Length - 1)
                component.Thrust += (thrustRating - 1 - idx) * (component.ThrustPerPartLevel[idx + 1] - component.ThrustPerPartLevel[idx]);
        }

        if (component.Enabled && CanEnable(uid, component))
            EnableThruster(uid, component);
    }

    private void OnUpgradeExamine(EntityUid uid, ThrusterComponent component, UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("thruster-comp-upgrade-thrust", component.Thrust / component.BaseThrust);
    }

    //private void OnEmpPulse(EntityUid uid, ThrusterComponent component, ref EmpPulseEvent args)
    //{
    //    if (component.Enabled && !component.ThrusterIgnoreEmp)
    //    {
    //        args.Affected = true;
    //        args.Disabled = true;
    //    }
    //}

    //[ByRefEvent]
    //public record struct ThrusterToggleAttemptEvent(bool Cancelled);
    // End Frontier: upgradeable machine parts, separate EMP handler

    #endregion

    private int GetFlagIndex(DirectionFlag flag)
    {
        return (int)Math.Log2((int)flag);
    }
}
