using Godot;

public enum PickupKind { Ammo, Health, Grenade, Coin }

public partial class Pickup : Node3D
{
    public PickupKind Kind;
    Node3D _model;
    float _t, _life = 22f;
    Vector3 _vel;
    bool _grounded;
    /// Online: server id. Taken: already claimed (server) / asked for (client).
    public int NetId;
    public bool Taken;
    bool _replica;
    float _claimT;

    public static void Spawn(PickupKind kind, Vector3 pos)
    {
        var p = new Pickup { Kind = kind };
        if (Net.IsServer) p.NetId = NetIds.Next();
        Main.Instance.World.AddChild(p);
        p.GlobalPosition = pos + Vector3.Up * 0.8f;
        p._vel = new Vector3((float)GD.RandRange(-2, 2), 5f, (float)GD.RandRange(-2, 2));
        if (Net.IsServer) Net.Game?.SendPickupSpawned(p, p._vel);
    }

    public static void SpawnReplica(int id, PickupKind kind, Vector3 pos, Vector3 vel)
    {
        if (NetIds.Get<Pickup>(id) != null) return;
        var p = new Pickup { Kind = kind, NetId = id, _replica = true, _vel = vel };
        Main.Instance.World.AddChild(p);
        p.GlobalPosition = pos;
        if (vel == Vector3.Zero) p._grounded = true; // already lying there (joined mid-run)
    }

    public override void _EnterTree() => NetIds.Add(NetId, this);
    public override void _ExitTree() => NetIds.Remove(NetId, this);

    public override void _Ready()
    {
        (_model, float s, Color glow) = Kind switch
        {
            PickupKind.Ammo => (Assets.Model("kit", "clip-large"), 3.2f, new Color(1f, 0.8f, 0.2f)),
            PickupKind.Health => (Assets.Model("dungeon", "potion"), 2.0f, new Color(1f, 0.25f, 0.3f)),
            PickupKind.Grenade => (Assets.Model("kit", "grenade-a"), 2.4f, new Color(0.4f, 1f, 0.4f)),
            _ => (Assets.Model("dungeon", "coin"), 2.0f, new Color(1f, 0.85f, 0.1f)),
        };
        _model.Scale = Vector3.One * s;
        AddChild(_model);
        AddChild(new OmniLight3D { LightColor = glow, LightEnergy = 1.4f, OmniRange = 2.5f, Position = Vector3.Up * 0.5f });
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.45f, OuterRadius = 0.55f, Rings = 24, RingSegments = 6 },
            MaterialOverride = Fx.Glow(glow), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        ring.Position = new Vector3(0, 0.03f, 0);
        AddChild(ring);
        ring.TopLevel = false;
        ring.Name = "Ring";
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        _life -= dt;
        var main = Main.Instance;
        if (!_grounded)
        {
            _vel.Y -= 18f * dt;
            var p = GlobalPosition + _vel * dt;
            if (p.Y <= 0.05f) { p.Y = 0.05f; _grounded = true; }
            GlobalPosition = p;
        }
        _model.Position = new Vector3(0, 0.35f + Mathf.Sin(_t * 3f) * 0.12f, 0);
        _model.RotateY(dt * 2.5f);
        if (_life < 4f) Visible = Mathf.PosMod(_life, 0.25f) > 0.1f;
        if (_life <= 0)
        {
            if (Net.IsServer) Net.Game?.SendPickupGone(this);
            QueueFree();
            return;
        }
        if (Net.IsServer) return; // clients claim pickups; the server just referees

        // nearest player on their feet gets the magnet (and the loot)
        Player pl = null;
        float best = float.MaxValue;
        foreach (var p in main.Players)
        {
            if (!p.Targetable) continue;
            float d2 = p.GlobalPosition.DistanceSquaredTo(GlobalPosition);
            if (d2 < best) { best = d2; pl = p; }
        }
        if (pl == null) return;
        var to = pl.GlobalPosition + Vector3.Up * 0.6f - GlobalPosition;
        float d = to.Length();
        if (d < 3.5f && _grounded)
        {
            GlobalPosition += to.Normalized() * Mathf.Min(d, (14f - d * 2f) * dt);
        }
        if (d >= 1.0f) return;
        if (_replica)
        {
            // ask once; the server answers with PickupTaken (first come, first served)
            // (re-ask after a moment if the server didn't hand it over, e.g. we were a step too far)
            _claimT -= dt;
            if ((!Taken || _claimT <= 0) && pl.NetRole == Player.Role.Local && pl.CanCollect(Kind))
            {
                Taken = true;
                _claimT = 1f;
                Net.Game?.SendTakePickup(this);
            }
        }
        else if (pl.TryCollect(Kind)) QueueFree();
    }
}
