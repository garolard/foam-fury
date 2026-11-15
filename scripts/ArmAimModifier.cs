using Godot;

/// Procedurally raises the arms so they point in a world-space direction (used for aiming a blaster
/// or for the classic zombie "arms forward" pose). Runs after the AnimationPlayer has posed the skeleton.
[GlobalClass]
public partial class ArmAimModifier : SkeletonModifier3D
{
	public Vector3 RightDir = Vector3.Forward;   // world space
	public Vector3 LeftDir = Vector3.Forward;    // world space
	public bool UseLeft = true, UseRight = true;

	int _rArm = -1, _rFore, _rHand, _lArm, _lFore, _lHand;

	public override void _Ready()
	{
		var sk = GetSkeleton();
		_rArm = sk.FindBone("RightArm"); _rFore = sk.FindBone("RightForeArm"); _rHand = sk.FindBone("RightHand");
		_lArm = sk.FindBone("LeftArm"); _lFore = sk.FindBone("LeftForeArm"); _lHand = sk.FindBone("LeftHand");
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		var sk = GetSkeleton();
		if (sk == null || _rArm < 0) return;
		var inv = sk.GlobalTransform.Basis.Inverse();
		if (UseRight) Point(sk, _rArm, _rFore, _rHand, (inv * RightDir).Normalized());
		if (UseLeft) Point(sk, _lArm, _lFore, _lHand, (inv * LeftDir).Normalized());
	}

	static void Point(Skeleton3D sk, int arm, int fore, int hand, Vector3 dir)
	{
		Aim(sk, arm, fore, dir);
		Aim(sk, fore, hand, dir);
	}

	static void Aim(Skeleton3D sk, int bone, int child, Vector3 dir)
	{
		var g = sk.GetBoneGlobalPose(bone);
		var cg = sk.GetBoneGlobalPose(child);
		var cur = cg.Origin - g.Origin;
		if (cur.LengthSquared() < 1e-10f) return;
		cur = cur.Normalized();
		if (cur.Dot(dir) > 0.9999f) return;
		var axis = cur.Cross(dir);
		if (axis.LengthSquared() < 1e-10f) return;
		float ang = cur.AngleTo(dir);
		var q = new Quaternion(axis.Normalized(), ang);
		var nb = new Basis(q) * g.Basis;
		sk.SetBoneGlobalPose(bone, new Transform3D(nb, g.Origin));
	}
}
