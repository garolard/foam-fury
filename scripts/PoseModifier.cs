using Godot;

/// Procedural full-body posing layered on top of the AnimationPlayer:
/// spine/head bends, arms and legs pointed along model-space directions.
/// Drives aiming, recoil, flinches, attacks and the death animations (the Kenney packs only ship idle/run/jump).
/// Model space: +Z forward, +Y up, -X is the character's right side.
[GlobalClass]
public partial class PoseModifier : SkeletonModifier3D
{
	public Node3D Space;

	public float ArmsWeight;
	public Vector3 RightArm = Vector3.Back, LeftArm = Vector3.Back;    // Vector3.Back == +Z == forward
	public Vector3? RightFore, LeftFore;                                // forearm directions (null = same as upper arm)

	public float LegsWeight;
	public Vector3 RightUpLeg = Vector3.Down, RightLeg = Vector3.Down, LeftUpLeg = Vector3.Down, LeftLeg = Vector3.Down;

	/// Radians. Pitch > 0 leans forward, yaw turns, roll tilts sideways.
	public float SpinePitch, SpineYaw, SpineRoll, HeadPitch, HeadYaw, HeadRoll;

	int _spine = -1, _chest, _upperChest, _neck, _head, _headEnd;
	int _rArm, _rFore, _rHand, _lArm, _lFore, _lHand;
	int _rUp, _rLeg, _rFoot, _lUp, _lLeg, _lFoot;

	public override void _Ready()
	{
		var sk = GetSkeleton();
		_spine = sk.FindBone("Spine"); _chest = sk.FindBone("Chest"); _upperChest = sk.FindBone("UpperChest");
		_neck = sk.FindBone("Neck"); _head = sk.FindBone("Head"); _headEnd = sk.FindBone("Head_end");
		_rArm = sk.FindBone("RightArm"); _rFore = sk.FindBone("RightForeArm"); _rHand = sk.FindBone("RightHand");
		_lArm = sk.FindBone("LeftArm"); _lFore = sk.FindBone("LeftForeArm"); _lHand = sk.FindBone("LeftHand");
		_rUp = sk.FindBone("RightUpLeg"); _rLeg = sk.FindBone("RightLeg"); _rFoot = sk.FindBone("RightFoot");
		_lUp = sk.FindBone("LeftUpLeg"); _lLeg = sk.FindBone("LeftLeg"); _lFoot = sk.FindBone("LeftFoot");
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		var sk = GetSkeleton();
		if (sk == null || _spine < 0 || Space == null) return;
		// model space -> skeleton space
		var m = sk.GlobalBasis.Inverse() * Space.GlobalBasis;
		Vector3 Ax(Vector3 v) => (m * v).Normalized();

		if (SpinePitch != 0 || SpineYaw != 0 || SpineRoll != 0)
			foreach (var b in new[] { _spine, _chest, _upperChest })
				Bend(sk, b, Ax(Vector3.Right), SpinePitch / 3f, Ax(Vector3.Up), SpineYaw / 3f, Ax(Vector3.Back), SpineRoll / 3f);
		if (HeadPitch != 0 || HeadYaw != 0 || HeadRoll != 0)
		{
			Bend(sk, _neck, Ax(Vector3.Right), HeadPitch * 0.4f, Ax(Vector3.Up), HeadYaw * 0.4f, Ax(Vector3.Back), HeadRoll * 0.4f);
			Bend(sk, _head, Ax(Vector3.Right), HeadPitch * 0.6f, Ax(Vector3.Up), HeadYaw * 0.6f, Ax(Vector3.Back), HeadRoll * 0.6f);
		}
		if (ArmsWeight > 0.001f)
		{
			Aim(sk, _rArm, _rFore, Ax(RightArm), ArmsWeight);
			Aim(sk, _rFore, _rHand, Ax(RightFore ?? RightArm), ArmsWeight);
			Aim(sk, _lArm, _lFore, Ax(LeftArm), ArmsWeight);
			Aim(sk, _lFore, _lHand, Ax(LeftFore ?? LeftArm), ArmsWeight);
		}
		if (LegsWeight > 0.001f)
		{
			Aim(sk, _rUp, _rLeg, Ax(RightUpLeg), LegsWeight);
			Aim(sk, _rLeg, _rFoot, Ax(RightLeg), LegsWeight);
			Aim(sk, _lUp, _lLeg, Ax(LeftUpLeg), LegsWeight);
			Aim(sk, _lLeg, _lFoot, Ax(LeftLeg), LegsWeight);
		}
	}

	static void Bend(Skeleton3D sk, int bone, Vector3 ax1, float a1, Vector3 ax2, float a2, Vector3 ax3, float a3)
	{
		if (bone < 0) return;
		var g = sk.GetBoneGlobalPose(bone);
		var b = g.Basis;
		if (a1 != 0) b = new Basis(ax1, a1) * b;
		if (a2 != 0) b = new Basis(ax2, a2) * b;
		if (a3 != 0) b = new Basis(ax3, a3) * b;
		sk.SetBoneGlobalPose(bone, new Transform3D(b, g.Origin));
	}

	/// Rotate `bone` so the direction toward `child` points along `dir` (blended by weight).
	static void Aim(Skeleton3D sk, int bone, int child, Vector3 dir, float weight)
	{
		if (bone < 0 || child < 0) return;
		var g = sk.GetBoneGlobalPose(bone);
		var cur = sk.GetBoneGlobalPose(child).Origin - g.Origin;
		if (cur.LengthSquared() < 1e-12f) return;
		cur = cur.Normalized();
		var axis = cur.Cross(dir);
		if (axis.LengthSquared() < 1e-10f) return;
		float ang = cur.AngleTo(dir) * Mathf.Clamp(weight, 0, 1);
		sk.SetBoneGlobalPose(bone, new Transform3D(new Basis(axis.Normalized(), ang) * g.Basis, g.Origin));
	}
}
