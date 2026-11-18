using Godot;

public enum ZombieType { Walker, Runner, Brute }

public partial class Zombie : CharacterBody3D
{
    public ZombieType Type;
    public bool Dead;
    public float Height;
    public int Points;

    CharacterModel _model;
    NavigationAgent3D _agent;
    float _hp, _maxHp, _speed, _damage, _scale, _animSpeed;
    float _repath, _groan, _attackCd, _windup = -1f, _stun, _rise, _hitArmKick;
    Vector3 _knock;
    float _yaw;
    float _flinch, _headFlinch, _lean, _headTilt, _phase;
    bool _lastExplosive;
    // status effects: mud slows, fire burns over time
    float _slowT, _slowMul = 1f, _burnT, _burnDps, _burnTick;
    CpuParticles3D _burnFx;
    Player _burnSource, _lastSource;
    // co-op: the player this zombie is chasing (nearest one on their feet, re-picked periodically)
    Player _target;
    float _retarget;
    OmniLight3D _burnLight;

    // ----- online -----
    /// Client copy of a server zombie: no AI, follows snapshots, shows hits and deaths it's told about.
    public bool Replica;
    public int NetId, Wave, Skin = -1;
    /// Counts landed swings (replicas replay the strike lunge when it changes).
    public int Strikes;
    readonly Interp _interp = new();
    Vector3 _lastPos;
    bool _netBurning;
    public bool Rising => _rise > 0;
    public float Yaw => _yaw;
    public int NetFlags => (Burning ? 1 : 0) | (Rising ? 2 : 0);
    /// Wind-up progress 1..255 (0 = not winding up).
    public int WindupByte => _windup >= 0 ? Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp(_windup / 0.45f, 0, 1)) * 254f) + 1, 1, 255) : 0;

    public static Zombie SpawnReplica(int id, ZombieType type, int wave, bool rise, int skin, Vector3 pos, float yaw)
    {
        var z = new Zombie { Replica = true, NetId = id };
        z.Setup(type, wave, rise, skin);
        z._yaw = yaw;
        Main.Instance.World.AddChild(z);
        z.GlobalPosition = pos;
        z._lastPos = pos;
        z._interp.Reset(pos, yaw);
        Main.Instance.Zombies.Add(z);
        return z;
    }

    public void PushNetState(double t, Vector3 pos, float yaw, int flags, int windup, int strikes)
    {
        if (Dead) return;
        _interp.Push(t, pos, yaw);
        _netBurning = (flags & 1) != 0;
        _windup = windup > 0 ? (1f - (windup - 1) / 254f) * 0.45f : -1f;
        if (strikes != (Strikes & 255)) { Strikes = strikes; _hitArmKick = 1f; }
    }

    public override void _EnterTree() => NetIds.Add(NetId, this);
    public override void _ExitTree() => NetIds.Remove(NetId, this);

    public bool Burning => Replica ? _netBurning : _burnT > 0;
    public bool Slowed => _slowT > 0;
    /// Player who set this zombie on fire (credit for burn kills / mud it lights).
    public Player BurnSource => _burnSource;

    public void Setup(ZombieType type, int wave, bool rise, int skin = -1)
    {
        Type = type;
        Wave = wave;
        // 0 zombieA, 1 zombieC (picked here so every client shows the same one)
        Skin = skin >= 0 ? skin : type == ZombieType.Runner ? 0 : type == ZombieType.Walker && GD.Randf() < 0.5f ? 0 : 1;
        float hpScale = 1f + 0.12f * (wave - 1);
        float spdScale = Mathf.Min(1f + 0.035f * (wave - 1), 1.4f);
        switch (type)
        {
            case ZombieType.Runner:
                _scale = 0.56f; _hp = 32; _speed = 5.4f; _damage = 8; _animSpeed = 1.25f; Points = 150; break;
            case ZombieType.Brute:
                _scale = 0.92f; _hp = 320; _speed = 2.0f; _damage = 28; _animSpeed = 0.55f; Points = 600; break;
            default:
                _scale = 0.62f; _hp = 60; _speed = 2.4f + (float)GD.RandRange(-0.3, 0.5); _damage = 12; _animSpeed = 0.6f; Points = 100; break;
        }
        _hp *= hpScale; _maxHp = _hp; _speed *= spdScale;
        _rise = rise ? 1.3f : 0f;
    }

    public override void _Ready()
    {
        // Model measurements (scale 1): neck ~2.42, head base 2.70, top of head 3.77.
        Height = 3.77f * _scale;
        CollisionLayer = 4;
        CollisionMask = 1 | 2 | 4;
        FloorSnapLength = 0.4f;
        float bodyH = 2.62f * _scale;
        var cap = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.62f * _scale, Height = bodyH } };
        cap.Position = new Vector3(0, bodyH / 2f, 0);
        AddChild(cap); // shape index 0 = body
        var head = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.6f * _scale } };
        head.Position = new Vector3(0, 3.2f * _scale, 0);
        AddChild(head); // shape index 1 = head

        string skin = Skin == 0 ? "zombieA" : "zombieC";
        Color? tint = Type == ZombieType.Brute ? new Color(1f, 0.55f, 0.5f) : Type == ZombieType.Runner ? new Color(0.8f, 1f, 0.75f) : null;
        _model = new CharacterModel(GameData.SurvivorDir + $"Skins/{skin}.png", _scale, tint);
        AddChild(_model);
        _model.Play("run", _animSpeed * (float)GD.RandRange(0.9, 1.1), 0f);
        _model.Anim.Seek(GD.Randf() * 0.6f, true);
        _model.Pose.ArmsWeight = 0.85f;
        _lean = Type switch { ZombieType.Runner => 0.55f, ZombieType.Brute => 0.4f, _ => 0.25f } + (float)GD.RandRange(-0.08, 0.08);
        _headTilt = (float)GD.RandRange(-0.35, 0.35);
        _phase = (float)GD.RandRange(0, 10);

        if (!Replica)
        {
            _agent = new NavigationAgent3D
            {
                PathDesiredDistance = 1.0f, TargetDesiredDistance = 1.0f, Radius = 0.5f, AvoidanceEnabled = false,
                PathMaxDistance = 4f,
            };
            AddChild(_agent);
        }
        _groan = (float)GD.RandRange(1.0, 6.0);
        _repath = (float)GD.RandRange(0, 0.3);

        if (_rise > 0)
        {
            CollisionLayer = 0;
            _model.Position = new Vector3(0, -Height, 0);
            Fx.SpawnDust(GlobalPosition);
            Sfx.Play3D("groan" + GD.RandRange(0, 3), GlobalPosition, -2f);
        }
        if (Type == ZombieType.Brute) Sfx.Play3D("brute_roar", GlobalPosition, 2f);
    }

    public const int HeadShapeIndex = 1;
    public static int Hits, Headshots, BruteHeadshots; // debug stats
    public bool IsHeadshot(int shapeIndex) => shapeIndex == HeadShapeIndex;

    /// Point used by aim assist (upper chest, so a small flick reaches the head).
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * 2.3f * _scale;

    public override void _PhysicsProcess(double delta)
    {
        if (Dead) return;
        float dt = (float)delta;
        var main = Main.Instance;

        if (_rise > 0)
        {
            _rise -= dt;
            _model.Position = new Vector3(0, -Height * Mathf.Clamp(_rise / 1.3f, 0, 1), 0);
            if (_rise <= 0) { _model.Position = Vector3.Zero; CollisionLayer = 4; }
            if (GD.Randf() < 0.15f) Fx.SpawnDust(GlobalPosition);
            return;
        }
        if (Replica) { ReplicaProcess(dt); return; }

        _attackCd -= dt; _stun -= dt;
        UpdateStatus(dt);
        if (Dead) return;
        _groan -= dt;
        if (_groan <= 0)
        {
            _groan = (float)GD.RandRange(3.5, 9.0);
            Sfx.Play3D("groan" + GD.RandRange(0, 3), GlobalPosition, -4f, 0.1f, Type == ZombieType.Runner ? 1.4f : Type == ZombieType.Brute ? 0.7f : 1f);
        }

        // keep the current victim through a wind-up; otherwise re-pick the nearest standing player now and then
        _retarget -= dt;
        bool targetLost = _target == null || !_target.Targetable;
        if (targetLost || (_retarget <= 0 && _windup < 0))
        {
            _retarget = 0.5f;
            var t = main.ClosestTarget(GlobalPosition);
            if (t != _target) { _target = t; _repath = 0; }
        }
        var player = _target;

        Vector3 desired = Vector3.Zero;
        bool playerAlive = player != null && player.Targetable;
        var flatToPlayer = Vector3.Zero;
        float distToPlayer = 999f;
        if (playerAlive)
        {
            flatToPlayer = player.GlobalPosition - GlobalPosition; flatToPlayer.Y = 0;
            distToPlayer = flatToPlayer.Length();
        }

        if (playerAlive && _windup < 0)
        {
            _repath -= dt;
            if (_repath <= 0)
            {
                _repath = 0.3f;
                _agent.TargetPosition = player.GlobalPosition;
            }
            Vector3 target = player.GlobalPosition;
            if (distToPlayer > 3.5f && !_agent.IsNavigationFinished())
            {
                var next = _agent.GetNextPathPosition();
                if ((next - GlobalPosition).Length() > 0.05f) target = next;
            }
            desired = target - GlobalPosition; desired.Y = 0;
            desired = desired.LengthSquared() > 0.001f ? desired.Normalized() : Vector3.Zero;

            // separation
            Vector3 sep = Vector3.Zero;
            foreach (var o in main.Zombies)
            {
                if (o == this || o.Dead) continue;
                var d = GlobalPosition - o.GlobalPosition; d.Y = 0;
                float l = d.Length();
                float r = 0.9f * (_scale + o._scale);
                if (l < r && l > 0.001f) sep += d / l * (1f - l / r);
            }
            desired = (desired + sep * 1.2f).Normalized();

            float reach = 0.7f + 0.62f * _scale + 0.45f;
            if (distToPlayer < reach && _attackCd <= 0)
            {
                _windup = Type == ZombieType.Brute ? 0.55f : 0.38f;
            }
        }

        if (_windup >= 0)
        {
            _windup -= dt;
            desired = Vector3.Zero;
            if (playerAlive && flatToPlayer.LengthSquared() > 0.01f) Face(flatToPlayer.Normalized(), dt * 12f);
            if (_windup < 0)
            {
                _attackCd = Type == ZombieType.Brute ? 1.6f : 1.1f;
                _hitArmKick = 1f;
                Strikes++;
                float reach = 0.7f + 0.62f * _scale + 1.0f;
                if (playerAlive && distToPlayer < reach)
                    player.TakeDamage(_damage, flatToPlayer.Normalized(), Type == ZombieType.Brute ? 12f : 5f);
            }
        }

        float speed = _stun > 0 ? 0f : _speed * (_slowT > 0 ? _slowMul : 1f);
        var vel = Velocity;
        var hv = desired * speed + _knock;
        _knock = _knock.MoveToward(Vector3.Zero, dt * 30f);
        vel.X = Mathf.Lerp(vel.X, hv.X, Mathf.Min(1, dt * 8f));
        vel.Z = Mathf.Lerp(vel.Z, hv.Z, Mathf.Min(1, dt * 8f));
        if (!IsOnFloor()) vel.Y -= 22f * dt; else vel.Y = Mathf.Max(vel.Y, -1f);
        Velocity = vel;
        MoveAndSlide();

        if (desired.LengthSquared() > 0.01f) Face(desired, dt * 6f);

        Animate(dt, new Vector3(vel.X, 0, vel.Z).Length());

        if (GlobalPosition.Y < -20) Die(Vector3.Forward, false);
    }

    /// Replica: glide along the server's path, keep groaning and burning for show.
    void ReplicaProcess(float dt)
    {
        if (_interp.Get(out var p, out var y))
        {
            GlobalPosition = p;
            _yaw = y;
            _model.Rotation = new Vector3(0, _yaw, 0);
        }
        var moved = GlobalPosition - _lastPos;
        float flat = dt > 0 ? new Vector2(moved.X, moved.Z).Length() / dt : 0f;
        _lastPos = GlobalPosition;
        _groan -= dt;
        if (_groan <= 0)
        {
            _groan = (float)GD.RandRange(3.5, 9.0);
            Sfx.Play3D("groan" + GD.RandRange(0, 3), GlobalPosition, -4f, 0.1f, Type == ZombieType.Runner ? 1.4f : Type == ZombieType.Brute ? 0.7f : 1f);
        }
        BurnVisuals();
        Animate(dt, flat);
    }

    /// Run/idle cycle plus the procedural body language (lean, reach, wind-up, flinches).
    void Animate(float dt, float flatVel)
    {
        if (flatVel > 0.4f) _model.Play("run", _animSpeed * Mathf.Clamp(flatVel / Mathf.Max(_speed, 0.1f), 0.5f, 1.3f));
        else _model.Play("idle", 0.8f);

        // ---- procedural body language ----
        _hitArmKick = Mathf.Max(0, _hitArmKick - dt * 4f);
        _flinch = Mathf.Max(0, _flinch - dt * 5f);
        _headFlinch = Mathf.Max(0, _headFlinch - dt * 4f);
        float time = Time.GetTicksMsec() * 0.001f + _phase;
        float gait = time * (Type == ZombieType.Runner ? 9f : 4.5f);
        float windupK = _windup >= 0 ? 1f - Mathf.Clamp(_windup / 0.45f, 0, 1) : 0f;
        var pose = _model.Pose;
        // arms: zombie reach, raised during wind-up, slammed down on strike
        float lift = _windup >= 0 ? 0.2f + windupK * 1.1f : -0.08f - _hitArmKick * 0.7f;
        float sway = Mathf.Sin(gait) * 0.12f;
        pose.ArmsWeight = 0.85f;
        pose.RightArm = new Vector3(-0.08f, lift + sway, 1f).Normalized();
        pose.LeftArm = new Vector3(0.08f, lift - sway, 1f).Normalized();
        pose.RightFore = pose.LeftFore = null;
        // hunched lean, rear back during wind-up, lunge on strike, snap back when shot
        pose.SpinePitch = _lean - windupK * 0.45f + _hitArmKick * 0.6f - _flinch * 0.7f;
        pose.SpineRoll = Mathf.Sin(gait * 0.5f) * 0.1f;
        pose.SpineYaw = Mathf.Sin(gait * 0.5f + 1f) * 0.08f;
        pose.HeadPitch = 0.1f - _headFlinch * 1.1f + Mathf.Sin(time * 1.3f) * 0.08f;
        pose.HeadRoll = _headTilt + Mathf.Sin(time * 0.9f) * 0.12f;
        if (_rise <= 0)
            _model.Position = new Vector3(_model.GlobalBasis.Z.X, 0, _model.GlobalBasis.Z.Z).Normalized() * _hitArmKick * 0.35f * _scale;
    }

    void Face(Vector3 dir, float t)
    {
        float target = Mathf.Atan2(dir.X, dir.Z);
        _yaw = Mathf.LerpAngle(_yaw, target, Mathf.Min(1f, t));
        _model.Rotation = new Vector3(0, _yaw, 0);
    }

    /// Slow movement to `mult` of normal speed for `time` seconds (strongest active slow wins).
    public void Slow(float mult, float time)
    {
        if (Dead) return;
        if (Replica) { Net.Game?.SendSlow(this, mult, time); return; } // only our own mud reaches here
        _slowMul = _slowT > 0 ? Mathf.Min(_slowMul, mult) : mult;
        _slowT = Mathf.Max(_slowT, time);
    }

    public void AddGravityPull(Vector3 impulse) => _knock += impulse;

    /// Set on fire: `dps` damage per second for `time` seconds (refreshes, keeps the hotter flame).
    public void Ignite(float dps, float time, Player source = null)
    {
        if (Dead || _rise > 0 || Replica) return;
        if (source != null) _burnSource = source;
        if (_burnT <= 0) _burnTick = 0.1f;
        _burnDps = _burnT > 0 ? Mathf.Max(_burnDps, dps) : dps;
        _burnT = Mathf.Max(_burnT, time);
    }

    void UpdateStatus(float dt)
    {
        if (_slowT > 0) { _slowT -= dt; if (_slowT <= 0) _slowMul = 1f; }
        bool burning = _burnT > 0;
        if (burning)
        {
            _burnT -= dt;
            _burnTick -= dt;
            if (_burnTick <= 0)
            {
                const float tick = 0.35f;
                _burnTick += tick;
                // fall away from where we're facing if the fire finishes us off
                var back = -new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
                TakeDamage(_burnDps * tick, GlobalPosition + Vector3.Up * Height * 0.6f, back, false, 0f, false, true, _burnSource);
            }
        }
        BurnVisuals();
    }

    /// Flames and glow while burning (server: none).
    void BurnVisuals()
    {
        if (Net.IsServer) return;
        if (Burning && _burnFx == null)
        {
            _burnFx = new CpuParticles3D
            {
                Amount = 24, Lifetime = 0.5f, Mesh = Fx.FireMesh("flame-wisp"), MaterialOverride = FlameJet.ParticleMaterial,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.35f, Height * 0.4f, 0.35f) * (_scale / 0.62f),
                Position = Vector3.Up * Height * 0.45f, Direction = Vector3.Up, Spread = 20f, Gravity = new Vector3(0, 3f, 0),
                InitialVelocityMin = 0.5f, InitialVelocityMax = 1.6f, ScaleAmountMin = 0.21f * (_scale / 0.62f), ScaleAmountMax = 0.45f * (_scale / 0.62f),
                ColorRamp = FlameJet.FireRamp, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_burnFx);
            _burnLight = new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.15f), LightEnergy = 1.8f, OmniRange = 3.5f, ShadowEnabled = false,
                Position = Vector3.Up * Height * 0.6f };
            AddChild(_burnLight);
        }
        if (_burnFx != null)
        {
            bool on = Burning && !Dead;
            _burnFx.Emitting = on;
            _burnLight.Visible = on;
            if (on) _burnLight.LightEnergy = 1.4f + 0.6f * Mathf.Sin(Time.GetTicksMsec() * 0.03f + _phase);
        }
    }

    public void TakeDamage(float dmg, Vector3 pos, Vector3 dir, bool headshot, float knock = 2.5f, bool explosive = false, bool minor = false, Player source = null)
    {
        if (Dead || _rise > 0) return;
        var main = Main.Instance;
        if (Replica)
        {
            // our own shot on our screen: show it now, let the server apply it
            if (source == null || source.NetRole != Player.Role.Local) return;
            HitFeedback(pos, dir, headshot ? dmg * 2f : dmg, headshot, minor, explosive);
            if (!minor) source.Hud?.HitMarker(headshot, false);
            Net.Game?.SendHit(this, dmg, pos, dir, headshot, knock, explosive, minor);
            return;
        }
        if (source != null) _lastSource = source;
        if (!minor) Hits++;
        if (headshot) { dmg *= 2f; Headshots++; if (Type == ZombieType.Brute) BruteHeadshots++; }
        _hp -= dmg;
        float kres = Type == ZombieType.Brute ? 0.25f : 1f;
        var kd = dir; kd.Y = 0;
        if (knock > 0 && kd.LengthSquared() > 0.0001f) _knock += kd.Normalized() * knock * kres;
        // minor hits (fire ticks, flame licks) shouldn't stun-lock or spam feedback
        if (Type != ZombieType.Brute && !minor) _stun = Mathf.Max(_stun, 0.12f);
        _lastExplosive = explosive;
        HitFeedback(pos, dir, dmg, headshot, minor, explosive);
        if (Net.IsServer) Net.Game?.SendZombieHit(this, pos, dir, dmg, headshot, minor, explosive);
        // hit markers go to whoever fired (solo: the only player, even for sourceless burn ticks)
        if (!minor || _hp <= 0) (source ?? main.SoloPlayer)?.Hud?.HitMarker(headshot, _hp <= 0);
        if (_hp <= 0) Die(dir, headshot);
    }

    /// Flash, flinch, blood, damage number and sound for a hit (`dmg` as shown, headshot bonus included).
    public void HitFeedback(Vector3 pos, Vector3 dir, float dmg, bool headshot, bool minor, bool explosive)
    {
        if (Dead) return;
        _model.Flash(minor ? 0.4f : 1f);
        float fl = Type == ZombieType.Brute ? 0.4f : 1f;
        _flinch = Mathf.Min(1.2f, _flinch + (explosive ? 1f : minor ? 0.12f : 0.5f) * fl);
        if (headshot) _headFlinch = 1f * fl;
        if (Net.IsServer) return;
        if (!minor) Fx.Burst(pos, new Color(0.45f, 0.08f, 0.06f), headshot ? 14 : 7, 5f, 0.09f, 0.5f, -dir + Vector3.Up * 0.5f, 45f);
        Fx.Number(pos + Vector3.Up * 0.3f, Mathf.Max(1, Mathf.RoundToInt(dmg)).ToString(),
            headshot ? new Color(1f, 0.85f, 0.2f) : minor ? new Color(1f, 0.6f, 0.25f) : Colors.White, headshot ? 1.4f : minor ? 0.7f : 1f);
        if (!minor || GD.Randf() < 0.2f) Sfx.Play3D(headshot ? "headshot" : "hit", pos, headshot ? 0f : minor ? -9f : -3f, 0.15f);
    }

    void Die(Vector3 dir, bool headshot)
    {
        if (Dead) return;
        // procedural death: explosions launch, headshots crumple, everything else gets knocked flat
        var kind = _lastExplosive ? DeathKind.Launch : headshot ? DeathKind.Crumple : GD.Randf() < 0.25f ? DeathKind.Crumple : DeathKind.FallBack;
        if (Net.IsServer) Net.Game?.SendZombieDied(this, dir, headshot, kind, _lastSource);
        Dead = true;
        CollisionLayer = 0;
        CollisionMask = 0;
        DeathVisuals(kind, dir);
        Main.Instance.OnZombieKilled(this, headshot, _lastSource);
    }

    /// Replica: the server says this zombie died (killer: who gets the kill marker).
    public void DieReplica(Vector3 dir, bool headshot, DeathKind kind, Player killer)
    {
        if (Dead) return;
        Dead = true;
        CollisionLayer = 0;
        CollisionMask = 0;
        Main.Instance.Zombies.Remove(this);
        if (killer is { NetRole: Player.Role.Local })
        {
            killer.Hud?.HitMarker(headshot, true);
            killer.In?.Rumble(0.2f, Type == ZombieType.Brute ? 0.6f : 0.15f, 0.1f);
            if (headshot) Fx.Number(GlobalPosition + Vector3.Up * (Height + 1.1f), "HEADSHOT", new Color(1f, 0.85f, 0.25f), 1.2f);
        }
        DeathVisuals(kind, dir);
    }

    void DeathVisuals(DeathKind kind, Vector3 dir)
    {
        Sfx.Play3D("zombie_die", GlobalPosition, -1f, 0.15f, Type == ZombieType.Brute ? 0.6f : Type == ZombieType.Runner ? 1.4f : 1f);
        Fx.Burst(GlobalPosition + Vector3.Up * Height * 0.5f, new Color(0.4f, 0.07f, 0.05f), 18, 7f, 0.14f, 0.8f);
        float power = Type == ZombieType.Brute ? 0.5f : Type == ZombieType.Runner ? 1.4f : 1f;
        float settle = _model.PlayDeath(kind, dir, power);
        var tw = CreateTween();
        tw.TweenInterval(settle);
        tw.TweenCallback(Callable.From(() => { if (Type == ZombieType.Brute) Main.Instance.Shake(GlobalPosition, 0.35f); }));
        tw.TweenInterval(2.4f);
        tw.TweenProperty(_model, "position:y", -1.5f, 1.4f).AsRelative();
        tw.TweenCallback(Callable.From(QueueFree));
    }
}
