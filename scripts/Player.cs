using Godot;

public partial class Player : CharacterBody3D
{
    public CharacterDef Def;
    public float Hp, MaxHp;
    public bool Dead;
    public int WeaponIndex;
    /// Shared blasters plus the character's signature weapon (if any).
    public WeaponDef[] Weapons;
    public int[] Mag, Reserve;
    public int Grenades;
    public float ReloadProgress = -1f; // -1 not reloading, else 0..1
    public float Spread;               // current spread in degrees
    public bool AimingAtEnemy;
    public float DashReady = 1f;
    public bool UsingPad = true;
    public Vector3 AimPoint;

    public CharacterModel Model;
    CameraRig _rig;
    Node3D _gun;
    float _muzzleDist;
    float _fireCd, _reloadTimer, _lastShot = 10f, _armBlend, _yaw, _invuln, _sinceHurt = 10f;
    float _dashTime, _dashCd, _bloom, _snapT = -1f;
    float _kick, _hurtFlinch, _throwT = -1f, _dashBuffer;
    const float DashCooldown = 0.6f;
    Vector3 _dashDir, _knock;
    float _snapYaw0, _snapPitch0, _snapYaw1, _snapPitch1;
    bool _wasAim;
    float _flameSfx;

    public WeaponDef Weapon => Weapons[WeaponIndex];

    public void Setup(CharacterDef def, CameraRig rig)
    {
        Def = def;
        _rig = rig;
        MaxHp = Hp = def.MaxHp;
        Grenades = def.Grenades;
        Weapons = def.Signature != null ? [.. GameData.Weapons, def.Signature] : GameData.Weapons;
        Mag = new int[Weapons.Length];
        Reserve = new int[Weapons.Length];
        for (int i = 0; i < Weapons.Length; i++)
        {
            Mag[i] = Weapons[i].MagSize;
            Reserve[i] = Weapons[i].StartReserve;
        }
    }

    public override void _Ready()
    {
        CollisionLayer = 2;
        CollisionMask = 1 | 4 | 16;
        FloorSnapLength = 0.3f;
        FloorMaxAngle = Mathf.DegToRad(50);
        var cap = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.38f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) };
        AddChild(cap);
        Model = new CharacterModel(Def.Skin, 0.62f);
        AddChild(Model);
        Model.Play("idle");
        _gun = new Node3D { TopLevel = true };
        AddChild(_gun);
        // signature weapons are the character's identity, so start with it in hand
        EquipWeapon(Weapons.Length - 1, true);
        _rig.Target = this;
        _rig.Arm.AddExcludedObject(GetRid());
        _yaw = _rig.Yaw + Mathf.Pi;
    }

    void EquipWeapon(int i, bool force = false)
    {
        if (i == WeaponIndex && !force) return;
        if (!force && Reserve[i] == 0 && Mag[i] == 0) { Sfx.Play("empty"); return; }
        WeaponIndex = i;
        ReloadProgress = -1f;
        _fireCd = Mathf.Max(_fireCd, 0.2f);
        foreach (var c in _gun.GetChildren()) c.QueueFree();
        var m = Assets.Model(Weapon.Pack, Weapon.Model);
        float s = 0.7f * Weapon.Scale;
        if (Weapon.FlipModel) m.RotationDegrees = new Vector3(0, 180, 0);
        _gun.AddChild(m);
        float minZ = 0;
        foreach (var mi in Assets.FindMeshes(m))
        {
            var ab = m.Transform * mi.Transform * mi.GetAabb();
            minZ = Mathf.Min(minZ, ab.Position.Z);
        }
        _muzzleDist = -minZ * s;
        if (!force) Sfx.Play("reload", -6f);
        Main.Instance.Hud?.WeaponChanged();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventJoypadButton || (e is InputEventJoypadMotion jm && Mathf.Abs(jm.AxisValue) > 0.3f)) UsingPad = true;
        else if (e is InputEventMouseMotion || e is InputEventKey || e is InputEventMouseButton) UsingPad = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Dead) return;
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sens = 0.0025f * (Input.IsActionPressed("aim") ? 0.6f : 1f);
            _rig.Yaw -= mm.Relative.X * sens;
            _rig.Pitch -= mm.Relative.Y * sens;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead)
        {
            var v = Velocity; v.X = Mathf.MoveToward(v.X, 0, dt * 20); v.Z = Mathf.MoveToward(v.Z, 0, dt * 20);
            if (!IsOnFloor()) v.Y -= 22f * dt;
            Velocity = v; MoveAndSlide();
            return;
        }
        _lastShot += dt; _fireCd -= dt; _invuln -= dt; _sinceHurt += dt; _dashCd -= dt;
        bool aiming = Input.IsActionPressed("aim");
        _rig.Aiming = aiming;

        // ----- camera look (stick) -----
        var look = Input.GetVector("look_left", "look_right", "look_up", "look_down");
        look = new Vector2(look.X * Mathf.Abs(look.X), look.Y * Mathf.Abs(look.Y));
        UpdateAimTarget();
        float friction = (UsingPad && AimingAtEnemy) ? 0.45f : 1f;
        float aimMul = aiming ? 0.5f : 1f;
        _rig.Yaw -= look.X * 3.6f * aimMul * friction * dt;
        _rig.Pitch -= look.Y * 2.4f * aimMul * friction * dt;
        if (aiming && !_wasAim && UsingPad) TryAimSnap();
        _wasAim = aiming;
        if (_snapT >= 0)
        {
            _snapT += dt / 0.12f;
            float t = Mathf.SmoothStep(0, 1, Mathf.Min(1, _snapT));
            _rig.Yaw = Mathf.LerpAngle(_snapYaw0, _snapYaw1, t);
            _rig.Pitch = Mathf.Lerp(_snapPitch0, _snapPitch1, t);
            if (_snapT >= 1) _snapT = -1;
        }

        // ----- movement -----
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var camBasis = new Basis(Vector3.Up, _rig.Yaw);
        var move = camBasis * new Vector3(input.X, 0, input.Y);
        if (move.Length() > 1) move = move.Normalized();
        float speed = Def.Speed * (aiming ? 0.6f : 1f) * (ReloadProgress >= 0 ? 0.85f : 1f);

        var vel = Velocity;
        // buffer the press so tapping B slightly early (still on cooldown) isn't swallowed
        if (Input.IsActionJustPressed("dash")) _dashBuffer = 0.35f;
        _dashBuffer -= dt;
        if (_dashBuffer > 0 && _dashCd <= 0)
        {
            _dashBuffer = 0;
            _rig.FovKick = 1f;
            InputSetup.Rumble(0.35f, 0.1f, 0.12f);
            _dashDir = move.LengthSquared() > 0.01f ? move.Normalized() : -camBasis.Z;
            _dashTime = 0.22f * Def.DashMult;
            _dashCd = DashCooldown;
            _invuln = Mathf.Max(_invuln, 0.3f);
            Sfx.Play("dash", -2f);
            Fx.SpawnDust(GlobalPosition);
        }
        DashReady = Mathf.Clamp(1f - _dashCd / DashCooldown, 0, 1);
        Vector3 hv;
        if (_dashTime > 0)
        {
            _dashTime -= dt;
            hv = _dashDir * 19f;
            if (GD.Randf() < 0.5f) Fx.Burst(GlobalPosition + Vector3.Up * 0.1f, new Color(0.55f, 0.45f, 0.35f), 3, 2f, 0.12f, 0.4f, Vector3.Up, 40f, 6f);
        }
        else
        {
            var cur = new Vector3(vel.X, 0, vel.Z);
            float accel = IsOnFloor() ? 50f : 18f;
            hv = cur.MoveToward(move * speed, accel * dt);
        }
        hv += _knock;
        _knock = _knock.MoveToward(Vector3.Zero, dt * 25f);
        vel.X = hv.X; vel.Z = hv.Z;
        if (IsOnFloor())
        {
            if (Input.IsActionJustPressed("jump")) { vel.Y = 8.2f; Model.Play("jump", 1.2f, 0.1f); }
        }
        else vel.Y -= 22f * dt;
        Velocity = vel;
        MoveAndSlide();

        // ----- facing -----
        bool combat = aiming || _lastShot < 0.8f || ReloadProgress >= 0;
        float targetYaw = _yaw;
        if (combat) targetYaw = _rig.Yaw + Mathf.Pi;
        else if (move.LengthSquared() > 0.01f) targetYaw = Mathf.Atan2(move.X, move.Z);
        _yaw = Mathf.LerpAngle(_yaw, targetYaw, Mathf.Min(1, dt * (combat ? 20f : 12f)));
        Model.Rotation = new Vector3(0, _yaw, 0);

        // ----- animation -----
        var flat = new Vector3(vel.X, 0, vel.Z).Length();
        if (!IsOnFloor()) Model.Play("jump", 1f, 0.15f);
        else if (flat > 0.5f) Model.Play("run", Mathf.Clamp(flat / 6.2f, 0.6f, 1.4f) * 1.1f);
        else Model.Play("idle");

        // ----- weapons -----
        if (Input.IsActionJustPressed("weapon_next")) CycleWeapon(1);
        if (Input.IsActionJustPressed("weapon_prev")) CycleWeapon(-1);
        for (int i = 0; i < Weapons.Length; i++) if (Input.IsActionJustPressed($"weapon_{i + 1}")) EquipWeapon(i);

        if (Input.IsActionJustPressed("reload")) StartReload();
        if (ReloadProgress >= 0)
        {
            _reloadTimer -= dt;
            ReloadProgress = 1f - _reloadTimer / (Weapon.ReloadTime * Def.ReloadMult);
            if (_reloadTimer <= 0) FinishReload();
        }

        bool trigger = Weapon.Auto ? Input.IsActionPressed("fire") : Input.IsActionJustPressed("fire");
        if (trigger && _fireCd <= 0 && ReloadProgress < 0)
        {
            if (Mag[WeaponIndex] > 0) Shoot();
            else if (Input.IsActionJustPressed("fire"))
            {
                if (!StartReload()) { Sfx.Play("empty"); _fireCd = 0.25f; }
            }
        }

        if (Input.IsActionJustPressed("grenade") && Grenades > 0) ThrowGrenade();

        _bloom = Mathf.MoveToward(_bloom, 0, dt * 12f);
        float moveSpread = flat > 0.5f ? 1.5f : 1f;
        Spread = Weapon.Spread * (aiming ? 0.5f : 1f) * moveSpread + _bloom;

        // regen up to 50%
        if (_sinceHurt > 5f && Hp < MaxHp * 0.5f) Hp = Mathf.Min(MaxHp * 0.5f, Hp + 4f * dt);

        if (GlobalPosition.Y < -10) { GlobalPosition = new Vector3(0, 1, 6); Velocity = Vector3.Zero; }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Dead) return; // the death animation owns the pose now
        bool armsUp = Input.IsActionPressed("aim") || _lastShot < 0.8f || ReloadProgress >= 0;
        _armBlend = Mathf.MoveToward(_armBlend, armsUp ? 1f : 0f, dt * 8f);
        float blend = Mathf.SmoothStep(0, 1, _armBlend);
        _kick *= Mathf.Exp(-14f * dt);
        _hurtFlinch = Mathf.Max(0, _hurtFlinch - dt * 4f);
        if (_throwT >= 0) { _throwT += dt / 0.42f; if (_throwT >= 1) _throwT = -1; }

        var muzzleBase = Model.RightHand.GlobalPosition;
        var aimDir = (AimPoint - muzzleBase).Normalized();
        var fwd = Model.GlobalBasis.Z.Normalized();
        var right = -Model.GlobalBasis.X.Normalized();   // model's right side is -X
        // never point arms backwards relative to body
        if (aimDir.Dot(fwd) < 0.2f) aimDir = (aimDir + fwd * 0.8f).Normalized();
        // recoil: muzzle climbs, then settles
        var kicked = (aimDir + Vector3.Up * _kick * 0.16f).Normalized();

        var pose = Model.Pose;
        var aimModel = Model.ToModel(kicked);
        pose.ArmsWeight = blend * 0.95f;
        pose.RightArm = aimModel;
        pose.RightFore = null;
        pose.LeftArm = Model.ToModel(kicked + right * 0.55f);   // reach across to support the blaster
        pose.LeftFore = null;

        // reload: tip the blaster down and slap the magazine with the off hand
        float reload = ReloadProgress >= 0 ? Mathf.Sin(Mathf.Clamp(ReloadProgress, 0, 1) * Mathf.Pi) : 0f;
        if (reload > 0)
        {
            var low = (fwd - Vector3.Up * 0.75f + right * 0.15f).Normalized();
            pose.RightArm = Model.ToModel(aimDir.Slerp(low, reload * 0.8f));
            float slap = Mathf.Sin(ReloadProgress * Mathf.Pi * 3f);
            pose.LeftArm = Model.ToModel((low + right * 0.7f + Vector3.Up * (0.15f * slap)).Normalized());
        }

        // grenade throw: wind up overhead, then whip forward
        if (_throwT >= 0)
        {
            float t = _throwT;
            var back = new Vector3(-0.35f, 0.9f, -0.45f).Normalized();
            var front = new Vector3(-0.15f, 0.35f, 1f).Normalized();
            pose.ArmsWeight = 1f;
            pose.RightArm = t < 0.45f ? Vector3.Down.Slerp(back, Mathf.SmoothStep(0, 1, t / 0.45f)) : back.Slerp(front, Mathf.SmoothStep(0, 1, (t - 0.45f) / 0.55f));
            pose.RightFore = null;
            pose.LeftArm = new Vector3(0.35f, 0.1f, 0.95f).Normalized();
        }

        // torso follows the aim pitch, rocks back with recoil, leans into a run, flinches when hit
        float aimPitch = Mathf.Asin(Mathf.Clamp(Model.ToModel(aimDir).Y, -1, 1));
        float run = Mathf.Clamp(new Vector3(Velocity.X, 0, Velocity.Z).Length() / Def.Speed, 0, 1);
        float throwLean = _throwT >= 0 ? (_throwT < 0.45f ? -0.25f : 0.35f) : 0f;
        pose.SpinePitch = -aimPitch * 0.45f * blend - _kick * 0.18f + run * 0.12f * (1 - blend) - _hurtFlinch * 0.5f + throwLean;
        pose.SpineYaw = _throwT >= 0 ? (_throwT < 0.45f ? 0.4f : -0.3f) : -0.12f * blend; // blade the body slightly behind the blaster
        pose.HeadPitch = aimPitch * 0.25f * blend - _hurtFlinch * 0.6f;
        pose.HeadYaw = 0.12f * blend;
        pose.HeadRoll = 0;

        var idleDir = (fwd - Vector3.Up * 0.25f).Normalized();
        var gunDir = idleDir.Slerp(kicked, blend);
        float s = 0.7f * Weapon.Scale;
        if (gunDir.LengthSquared() > 0.01f)
        {
            var basis = Basis.LookingAt(gunDir, Vector3.Up);
            // recoil tilts the muzzle up; reload rolls the blaster over
            basis = new Basis(basis.X.Normalized(), _kick * 0.35f) * basis;
            if (reload > 0) basis = new Basis(gunDir, reload * 1.1f) * new Basis(basis.X.Normalized(), -reload * 0.5f) * basis;
            _gun.GlobalTransform = new Transform3D(basis.Scaled(Vector3.One * s), muzzleBase + gunDir * (0.05f - _kick * 0.07f));
        }
    }

    Vector3 MuzzlePos => _gun.GlobalPosition - _gun.GlobalBasis.Z.Normalized() * _muzzleDist + _gun.GlobalBasis.Y.Normalized() * 0.04f;

    void UpdateAimTarget()
    {
        var cam = _rig.Camera;
        var from = cam.GlobalPosition;
        var dir = -cam.GlobalBasis.Z;
        float skip = (GlobalPosition + Vector3.Up * 1.4f - from).Dot(dir);
        from += dir * Mathf.Max(0, skip);
        var q = PhysicsRayQueryParameters3D.Create(from, from + dir * 200f, 1 | 4, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        AimPoint = hit.Count > 0 ? hit["position"].AsVector3() : from + dir * 200f;
        AimingAtEnemy = hit.Count > 0 && hit["collider"].AsGodotObject() is Zombie;
        // pad aim assist: magnetic slowdown near enemies
        if (!AimingAtEnemy && UsingPad)
        {
            var vp = GetViewport().GetVisibleRect().Size;
            var center = vp / 2f;
            foreach (var z in Main.Instance.Zombies)
            {
                if (z.Dead || cam.IsPositionBehind(z.AimPoint)) continue;
                if (cam.UnprojectPosition(z.AimPoint).DistanceTo(center) < vp.Y * 0.06f) { AimingAtEnemy = true; break; }
            }
        }
    }

    void TryAimSnap()
    {
        var cam = _rig.Camera;
        var vp = GetViewport().GetVisibleRect().Size;
        var center = vp / 2f;
        Zombie best = null; float bestD = vp.Y * 0.18f;
        foreach (var z in Main.Instance.Zombies)
        {
            if (z.Dead || cam.IsPositionBehind(z.AimPoint)) continue;
            if (z.GlobalPosition.DistanceTo(GlobalPosition) > 35f) continue;
            float d = cam.UnprojectPosition(z.AimPoint).DistanceTo(center);
            if (d < bestD) { bestD = d; best = z; }
        }
        if (best == null) return;
        var to = best.AimPoint - cam.GlobalPosition;
        _snapYaw0 = _rig.Yaw; _snapPitch0 = _rig.Pitch;
        // approximate yaw/pitch that centers the target (camera is offset; good enough as assist)
        var pivotTo = best.AimPoint - _rig.GlobalPosition;
        _snapYaw1 = Mathf.Atan2(-pivotTo.X, -pivotTo.Z);
        float horiz = new Vector2(to.X, to.Z).Length();
        _snapPitch1 = Mathf.Atan2(to.Y, horiz);
        // blend partway (assist, not lock)
        _snapYaw1 = Mathf.LerpAngle(_snapYaw0, _snapYaw1, 0.85f);
        _snapPitch1 = Mathf.Lerp(_snapPitch0, _snapPitch1, 0.85f);
        _snapT = 0;
    }

    void CycleWeapon(int d)
    {
        int n = Weapons.Length;
        for (int k = 1; k <= n; k++)
        {
            int i = ((WeaponIndex + d * k) % n + n) % n;
            if (Mag[i] > 0 || Reserve[i] != 0) { EquipWeapon(i); return; }
        }
    }

    bool StartReload()
    {
        var w = Weapon;
        if (ReloadProgress >= 0 || Mag[WeaponIndex] >= w.MagSize || Reserve[WeaponIndex] == 0) return false;
        _reloadTimer = w.ReloadTime * Def.ReloadMult;
        ReloadProgress = 0;
        Sfx.Play("reload", -4f);
        return true;
    }

    void FinishReload()
    {
        var w = Weapon;
        int need = w.MagSize - Mag[WeaponIndex];
        int take = Reserve[WeaponIndex] < 0 ? need : Mathf.Min(need, Reserve[WeaponIndex]);
        Mag[WeaponIndex] += take;
        if (Reserve[WeaponIndex] > 0) Reserve[WeaponIndex] -= take;
        ReloadProgress = -1f;
    }

    void Shoot()
    {
        var w = Weapon;
        Mag[WeaponIndex]--;
        _fireCd = w.FireRate;
        _lastShot = 0;
        // make sure arms are up so the muzzle points where we aim
        _armBlend = Mathf.Max(_armBlend, 0.7f);
        _yaw = _rig.Yaw + Mathf.Pi;
        Model.Rotation = new Vector3(0, _yaw, 0);
        var muzzle = MuzzlePos;
        var baseDir = (AimPoint - muzzle).Normalized();
        // if aim point is behind the muzzle (very close geometry) use camera dir
        if (baseDir.Dot(-_rig.Camera.GlobalBasis.Z) < 0.3f) baseDir = -_rig.Camera.GlobalBasis.Z;
        for (int i = 0; i < w.Pellets; i++)
        {
            var dir = ApplySpread(baseDir, Spread);
            switch (w.Kind)
            {
                case FireKind.Mud: MudGlob.Fire(muzzle, dir, w, Def.DamageMult, GetRid()); break;
                case FireKind.Flame: FlameJet.Fire(muzzle, dir, Velocity, w, Def.DamageMult); break;
                default: Projectile.Fire(muzzle, dir, w, Def.DamageMult, GetRid()); break;
            }
        }
        if (w.Kind == FireKind.Flame)
        {
            Fx.Flash(muzzle, w.Tracer, 2.5f, 6f, 0.1f);
            // continuous hiss: don't restart the sound every 50 ms
            _flameSfx -= w.FireRate;
            if (_flameSfx <= 0) { Sfx.Play(w.Sound, -6f, 0.08f); _flameSfx = 0.18f; }
        }
        else
        {
            Fx.MuzzleFlash(muzzle, baseDir, w.Tracer);
            Sfx.Play(w.Sound, w.Pellets > 1 ? 0f : -3f, 0.06f);
        }
        _rig.Recoil += Mathf.DegToRad(w.Recoil);
        _rig.AddTrauma(w.Pellets > 1 ? 0.25f : w.Recoil * 0.04f);
        _bloom = Mathf.Min(_bloom + w.Recoil * 0.6f, 6f);
        _kick = Mathf.Min(1.6f, _kick + 0.35f + w.Recoil * 0.18f);
        InputSetup.Rumble(0.25f + w.Recoil * 0.06f, w.Pellets > 1 ? 0.6f : 0.1f, 0.08f);
        if (Mag[WeaponIndex] == 0) StartReload();
    }

    static Vector3 ApplySpread(Vector3 dir, float deg)
    {
        if (deg <= 0.01f) return dir;
        var up = Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        var r = dir.Cross(up).Normalized();
        var u = r.Cross(dir).Normalized();
        float a = (float)GD.RandRange(0, Mathf.Tau);
        float m = Mathf.Tan(Mathf.DegToRad(deg)) * Mathf.Sqrt(GD.Randf());
        return (dir + (r * Mathf.Cos(a) + u * Mathf.Sin(a)) * m).Normalized();
    }

    void ThrowGrenade()
    {
        Grenades--;
        var dir = -_rig.Camera.GlobalBasis.Z;
        var pos = GlobalPosition + Vector3.Up * 1.6f + new Vector3(dir.X, 0, dir.Z).Normalized() * 0.6f;
        Grenade.Throw(pos, dir * 15f + Vector3.Up * 4.5f + new Vector3(Velocity.X, 0, Velocity.Z) * 0.5f, Def.BlastMult);
        Sfx.Play("throw");
        _throwT = 0f;
        _lastShot = 0.3f;
    }

    public void TakeDamage(float dmg, Vector3 dir, float knock)
    {
        if (Dead || _invuln > 0) return;
        Hp -= dmg;
        _invuln = 0.45f;
        _sinceHurt = 0;
        _knock += dir * knock;
        Model.Flash(0.8f);
        _hurtFlinch = 1f;
        _rig.AddTrauma(0.45f);
        InputSetup.Rumble(0.6f, 0.8f, 0.25f);
        Sfx.Play("hurt", 0f, 0.1f);
        Main.Instance.Hud?.Hurt(dmg / MaxHp);
        if (Hp <= 0) { Hp = 0; Die(dir); }
    }

    void Die(Vector3 dir)
    {
        Dead = true;
        _gun.Visible = false;
        // hit from behind -> pitch forward onto the face, otherwise get knocked flat on the back
        var flat = new Vector3(dir.X, 0, dir.Z).Normalized();
        bool fromBehind = flat.Dot(Model.GlobalBasis.Z.Normalized()) > 0.3f;
        Model.PlayDeath(fromBehind ? DeathKind.Crumple : DeathKind.FallBack, dir);
        InputSetup.Rumble(1f, 1f, 0.8f);
        Main.Instance.OnPlayerDied();
    }

    public bool TryCollect(PickupKind kind)
    {
        switch (kind)
        {
            case PickupKind.Health:
                if (Hp >= MaxHp) return false;
                Hp = Mathf.Min(MaxHp, Hp + MaxHp * 0.35f);
                Main.Instance.Hud?.Toast("+HEALTH", new Color(1f, 0.35f, 0.4f));
                break;
            case PickupKind.Ammo:
                for (int i = 0; i < Weapons.Length; i++)
                    if (Reserve[i] >= 0) Reserve[i] = Mathf.Min(Weapons[i].MaxReserve, Reserve[i] + Weapons[i].AmmoPickup);
                Main.Instance.Hud?.Toast("+AMMO", new Color(1f, 0.8f, 0.2f));
                break;
            case PickupKind.Grenade:
                if (Grenades >= 9) return false;
                Grenades++;
                Main.Instance.Hud?.Toast("+GRENADE", new Color(0.4f, 1f, 0.4f));
                break;
            case PickupKind.Coin:
                Main.Instance.AddScore(250, GlobalPosition + Vector3.Up * 2f);
                break;
        }
        Sfx.Play("pickup", -2f);
        InputSetup.Rumble(0.2f, 0f, 0.06f);
        return true;
    }
}
