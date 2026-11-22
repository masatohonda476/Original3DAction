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
        const float SupportGripSpacing = 0.85f;

        // クリップにキーがない関節にも、前フレームの補正が積み重ならないようにする。
        internal void ReleaseSupportGrip()
        {
            rightArm?.Restore();
            leftArm?.Restore();
        }

        void LateUpdate()
        {
            if (!MeleeWeapon.UsesTwoHands || Aiming || player.Dead || rig == null) return;
            rightArm ??= GripArm.Find(rig.mountRightArm, rig.sword);
            leftArm ??= GripArm.Find(rig.mountLeftArm, rig.swordL);
            if (rightArm == null || leftArm == null) return;

            var grip = rig.sword;
            Quaternion rightFacing = grip.rotation;
            Quaternion leftFacing = rightFacing * Quaternion.AngleAxis(180f, Vector3.up);
            Vector3 supportOffset = grip.TransformVector(Vector3.down * SupportGripSpacing);
            Vector3 rightCenter = rightArm.GripCenter(rightFacing);
            Vector3 leftCenter = leftArm.GripCenter(leftFacing) - supportOffset;
            float rightReach = rightArm.Reach, leftReach = leftArm.Reach;

            // 片手斬りのクリップで柄が左手の届かない位置に来たら、右手ごと両腕の届く範囲に寄せる。
            Vector3 rightTarget = Vector3.MoveTowards(leftCenter, grip.position, leftReach);
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
            rightArm.Apply(rightTarget, rightFacing, -transform.up);
            leftArm.Apply(grip.position + supportOffset, leftFacing, -transform.up);
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

            public void Apply(Vector3 target, Quaternion facing, Vector3 bendFallback)
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
                Vector3 bend = Vector3.ProjectOnPlane(upper, direction);
                if (bend.sqrMagnitude < 0.0001f) bend = Vector3.ProjectOnPlane(bendFallback, direction);
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
