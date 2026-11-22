using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelArms
    {
        static readonly Color HaftColor = new(0.26f, 0.27f, 0.31f);

        static Transform CreateTwoHandVisual(DuelWeapon w, Transform parent, bool held)
        {
            var root = new GameObject(w.name).transform;
            root.SetParent(parent, false);
            root.gameObject.layer = DuelFighter.FighterLayer;
            var grip = new GameObject("Grip").transform;
            grip.SetParent(root, false);

            Vector3 head = w.scale;
            float back = w.MeleeShape == DuelMeleeShape.TwoHandSword ? w.HaftLength : 1.5f;
            float headStart = w.MeleeShape == DuelMeleeShape.TwoHandSword ? 0.4f : w.HaftLength - back;
            if (!held)
            {
                // 落とし物は武器を横にして、光の柱を中心に回す。
                float center = (headStart + head.y - back) * 0.5f;
                grip.SetLocalPositionAndRotation(Vector3.back * center, Quaternion.Euler(90f, 0f, 0f));
            }

            var haft = ProtoFx.Lit(HaftColor);
            var metal = ProtoFx.Lit(w.Color);
            float shaftLength = headStart + back;
            AddWeaponPart(grip, "柄", PrimitiveType.Cylinder, new Vector3(0.32f, shaftLength * 0.5f, 0.32f),
                (headStart - back) * 0.5f, haft);
            if (w.MeleeShape == DuelMeleeShape.TwoHandSword)
                AddWeaponPart(grip, "鍔", PrimitiveType.Cube, new Vector3(head.x * 1.6f, 0.18f, 0.5f), headStart, haft);

            PrimitiveType headType = w.MeleeShape == DuelMeleeShape.Spear ? PrimitiveType.Capsule : PrimitiveType.Cube;
            Vector3 headScale = headType == PrimitiveType.Capsule ? new Vector3(head.x, head.y * 0.5f, head.z) : head;
            AddWeaponPart(grip, "武器の先", headType, headScale, headStart + head.y * 0.5f, metal);
            return root;
        }

        static void AddWeaponPart(Transform parent, string partName, PrimitiveType type, Vector3 size, float height, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.layer = DuelFighter.FighterLayer;
            DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = Vector3.up * height;
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        GripArm rightArm, leftArm;
        GripPose? lastGripPose, swingStartGrip;

        internal void RememberGripForSwing() => swingStartGrip = lastGripPose;

        readonly struct GripPose
        {
            public readonly Vector3 hand, axis;
            public GripPose(Vector3 hand, Vector3 axis)
            {
                this.hand = hand;
                this.axis = axis.normalized;
            }

            public static GripPose Blend(GripPose from, GripPose to, float t)
            {
                t = Mathf.SmoothStep(0f, 1f, t);
                return new GripPose(Vector3.Lerp(from.hand, to.hand, t), Vector3.Slerp(from.axis, to.axis, t));
            }
        }

        static float SupportGripOffset(DuelMeleeShape shape) => shape switch
        {
            DuelMeleeShape.Spear => 1.25f,
            DuelMeleeShape.Glaive => 1.05f,
            DuelMeleeShape.Hammer => -1.15f,
            _ => -0.88f,
        };

        // 機体の胴を基準に、右手の位置と柄から先端への向きを指定する。
        GripPose TwoHandPose(DuelMeleeShape shape)
        {
            GripPose ready = shape switch
            {
                DuelMeleeShape.Spear => new(new(1.20f, 5.45f, 1.48f), new(-0.48f, 0.14f, 0.91f)),
                DuelMeleeShape.Glaive => new(new(0.76f, 5.12f, 1.74f), new(-0.53f, 0.62f, 0.64f)),
                DuelMeleeShape.Hammer => new(new(0.55f, 5.75f, 1.90f), new(0.58f, 0.75f, 0.26f)),
                _ => new(new(0.44f, 5.63f, 2.02f), new(0.43f, 0.85f, 0.36f)),
            };
            if (!player.TryGetGripMotion(out var swing, out int phase, out float progress)) return ready;

            var motion = swing.gripMotion;
            if (motion == DuelGripMotion.Swing && shape == DuelMeleeShape.Glaive) motion = DuelGripMotion.Sweep;
            bool backhand = swing.twist2 < -10f;
            float side = backhand ? -1f : 1f;
            (GripPose raised, GripPose struck) attack = motion switch
            {
                DuelGripMotion.Thrust => (
                    new(new(1.16f, 5.35f, 1.25f), new(-0.40f, 0.11f, 0.91f)),
                    new(new(0.68f, 5.64f, 2.55f), new(-0.28f, 0.02f, 0.96f))),
                DuelGripMotion.Sweep => (
                    new(new(0.65f, 5.62f, 1.86f), new(0.56f * side, 0.38f, 0.72f)),
                    new(new(0.45f, 5.56f, 2.14f), new(-0.85f * side, 0.08f, 0.52f))),
                DuelGripMotion.Slam => (
                    new(new(0.72f, 7.30f, 1.70f), new(0.35f, 0.82f, -0.45f)),
                    new(new(0.51f, 4.95f, 2.40f), new(0.12f, -0.89f, 0.45f))),
                _ => (
                    new(new(0.84f * side, 7.15f, 1.63f), new(0.47f * side, 0.64f, -0.61f)),
                    new(new(0.24f, 5.56f, 2.25f), new(-0.57f * side, -0.34f, 0.75f))),
            };
            return phase == 0 ? GripPose.Blend(swingStartGrip ?? ready, attack.raised, progress)
                : phase == 1 ? GripPose.Blend(attack.raised, attack.struck, progress)
                : GripPose.Blend(attack.struck, ready, progress);
        }

        // クリップにキーがない関節にも、前フレームの補正が積み重ならないようにする。
        internal void ReleaseSupportGrip()
        {
            rightArm?.Restore();
            leftArm?.Restore();
        }

        void LateUpdate()
        {
            if (!MeleeWeapon.UsesTwoHands || Aiming || player.Dead || rig == null)
            {
                lastGripPose = null;
                return;
            }
            rightArm ??= GripArm.Find(rig.mountRightArm, rig.sword);
            leftArm ??= GripArm.Find(rig.mountLeftArm, rig.swordL);
            if (rightArm == null || leftArm == null) return;

            var shape = MeleeWeapon.MeleeShape;
            var pose = TwoHandPose(shape);
            lastGripPose = pose;
            var body = rig.transform;
            Vector3 axis = body.TransformDirection(pose.axis);
            Quaternion rightFacing = Quaternion.LookRotation(Vector3.Cross(body.right, axis), axis);
            Quaternion leftFacing = rightFacing * Quaternion.AngleAxis(180f, Vector3.up);
            Vector3 supportOffset = axis * (SupportGripOffset(shape) * rig.sword.TransformVector(Vector3.up).magnitude);
            Vector3 rightCenter = rightArm.GripCenter(rightFacing);
            Vector3 leftCenter = leftArm.GripCenter(leftFacing) - supportOffset;
            float rightReach = rightArm.Reach, leftReach = leftArm.Reach;

            // 体のひねりや足の動きに合わせて、柄を両腕が届く範囲に収める。
            Vector3 rightTarget = Vector3.MoveTowards(leftCenter, body.TransformPoint(pose.hand), leftReach);
            rightTarget = Vector3.MoveTowards(rightCenter, rightTarget, rightReach);
            if ((rightTarget - leftCenter).sqrMagnitude > leftReach * leftReach)
            {
                Vector3 inside = Vector3.Lerp(rightCenter, leftCenter, rightReach / (rightReach + leftReach));
                Vector3 outside = rightTarget;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 middle = (inside + outside) * 0.5f;
                    if ((middle - leftCenter).sqrMagnitude <= leftReach * leftReach) inside = middle;
                    else outside = middle;
                }
                rightTarget = inside;
            }
            rightArm.Apply(rightTarget, rightFacing, body.TransformDirection(new Vector3(0.7f, -1f, 0f)));
            leftArm.Apply(rig.sword.position + supportOffset, leftFacing, body.TransformDirection(new Vector3(-0.7f, -1f, 0f)));
        }

        /// <summary>機体の肩・肘の長さを保ち、手首を回して握り点を柄に合わせる。</summary>
        sealed class GripArm
        {
            readonly Transform shoulder, elbow, wrist, grip;
            bool applied;
            Quaternion shoulderPose, elbowPose, wristPose;

            GripArm(Transform shoulder, Transform elbow, Transform wrist, Transform grip)
            {
                this.shoulder = shoulder;
                this.elbow = elbow;
                this.wrist = wrist;
                this.grip = grip;
            }

            public static GripArm Find(Transform mount, Transform grip)
            {
                if (mount == null || grip == null || mount.parent == null || mount.parent.parent == null) return null;
                var elbow = mount.parent;
                for (var joint = grip.parent; joint != null && joint != elbow; joint = joint.parent)
                    if (joint.parent == elbow) return new GripArm(elbow.parent, elbow, joint, grip);
                return null;
            }

            public float Reach => Mathf.Max(0.01f, Vector3.Distance(shoulder.position, elbow.position) +
                Vector3.Distance(elbow.position, wrist.position) - 0.05f);

            public Vector3 GripCenter(Quaternion facing) =>
                shoulder.position + facing * (Quaternion.Inverse(grip.rotation) * (grip.position - wrist.position));

            public void Apply(Vector3 target, Quaternion facing, Vector3 elbowPole)
            {
                Vector3 root = shoulder.position;
                Vector3 upper = elbow.position - root;
                float a = upper.magnitude, b = Vector3.Distance(elbow.position, wrist.position);
                if (a < 0.01f || b < 0.01f) return;
                Quaternion wristFacing = facing * Quaternion.Inverse(grip.rotation) * wrist.rotation;
                Vector3 toWrist = target - GripCenter(facing);
                Vector3 direction = toWrist.normalized;
                if (direction == Vector3.zero) return;
                float length = Mathf.Clamp(toWrist.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
                Vector3 bend = Vector3.ProjectOnPlane(elbowPole, direction);
                if (bend.sqrMagnitude < 0.0001f) bend = Vector3.ProjectOnPlane(upper, direction);
                if (bend.sqrMagnitude < 0.0001f) bend = Vector3.Cross(direction, Vector3.forward);
                float along = (a * a + length * length - b * b) / (2f * length);
                Vector3 nextUpper = direction * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));

                shoulderPose = shoulder.localRotation;
                elbowPose = elbow.localRotation;
                wristPose = wrist.localRotation;
                applied = true;
                shoulder.rotation = Quaternion.FromToRotation(upper, nextUpper) * shoulder.rotation;
                Vector3 nextLower = root + direction * length - elbow.position;
                elbow.rotation = Quaternion.FromToRotation(wrist.position - elbow.position, nextLower) * elbow.rotation;
                wrist.rotation = wristFacing;
            }

            public void Restore()
            {
                if (!applied) return;
                applied = false;
                shoulder.localRotation = shoulderPose;
                elbow.localRotation = elbowPose;
                wrist.localRotation = wristPose;
            }
        }
    }
}
