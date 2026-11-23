using System;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelArms
    {
        const int SubSlotCount = 4, Sides = 2;
        const float SubSightAngle = 16f, SubSightAspect = 0.65f;
        const float MissileTurn = 240f;

        struct SubMount
        {
            public DuelWeapon Weapon;
            public int Uses;
        }

        struct SubLaunch
        {
            public DuelWeapon Weapon;
            public int Rounds;
            public float NextAt;
            public Vector3 Aim;
            public DuelEnemy Target;
        }

        // 左肩・右肩・左脚・右脚。肩と脚はそれぞれ専用の武装を受け付ける。
        readonly SubMount[] subMounts = new SubMount[SubSlotCount];
        readonly Transform[] subAnchors = new Transform[SubSlotCount];
        readonly Transform[] subVisuals = new Transform[SubSlotCount];
        readonly SubLaunch[] subLaunches = new SubLaunch[SubSlotCount];
        readonly DuelEnemy[] subTargets = new DuelEnemy[SubSlotCount];
        int pendingSubRounds, subFlightVersion;
        bool wasSubMode;

        public bool SubFiring => pendingSubRounds > 0;
        bool SubModeHeld => !ChoiceLocksInput && ProtoInput.LeftHandHeld;
        public bool SubAiming => SubModeHeld && !UsingItem && roundsPending == 0 &&
            player != null && player.CanShoot && game.stage != null && !game.stage.Cleared;
        public bool FreeAim => Aiming || SubAiming;
        bool BlocksMainAttack => UsingItem || SubModeHeld || SubFiring;

        public static int SubSlotStart(DuelWeapon w) => w.Slot == DuelSlot.Shoulder ? 0 : Sides;
        public DuelWeapon SubWeaponAt(int slot) => subMounts[slot].Weapon;
        public int SubUsesAt(int slot) => subMounts[slot].Uses;
        public DuelEnemy SubTargetAt(int slot) => SubAiming && player.LockOn ? subTargets[slot] : null;
        public int SubChoiceIndex { get; private set; }

        public bool IsEquipped(DuelWeapon weapon)
        {
            if (!weapon.IsSub) return Get(weapon.Slot) == weapon;
            int start = SubSlotStart(weapon);
            return subMounts[start].Weapon == weapon || subMounts[start + 1].Weapon == weapon;
        }

        float SubWeight
        {
            get
            {
                float weight = 0f;
                foreach (var mount in subMounts) weight += mount.Weapon?.weight ?? 0f;
                return weight;
            }
        }

        public float SubWeightAfterEquip(DuelWeapon weapon, int side) =>
            TotalWeight - (subMounts[SubSlotStart(weapon) + side].Weapon?.weight ?? 0f) + weapon.weight;

        void BeginSubChoice(DuelWeapon weapon)
        {
            int start = SubSlotStart(weapon);
            SubChoiceIndex = subMounts[start].Weapon != null && subMounts[start + 1].Weapon == null ? 1 : 0;
        }

        void UpdateSubChoice(DuelLoot loot)
        {
            if (ProtoInput.MenuUp || ProtoInput.MenuDown)
            {
                SubChoiceIndex = 1 - SubChoiceIndex;
                return;
            }
            var w = loot.Weapon;
            if (PressedConfirm() == null || !Fits(SubWeightAfterEquip(w, SubChoiceIndex))) return;

            int index = SubSlotStart(w) + SubChoiceIndex;
            var old = subMounts[index];
            var at = loot.transform.position;
            if (keptSubMounts != null && !bossSubPlaced.Contains(loot))
                bossSubPicked.Add((w, loot.SubUses, at));
            if (old.Weapon != null && old.Uses > 0)
            {
                var placed = PlaceSub(old.Weapon, old.Uses, at);
                placed.LowerPriority();
                if (keptSubMounts != null) bossSubPlaced.Add(placed);
            }
            subMounts[index] = new SubMount { Weapon = w, Uses = loot.SubUses };
            RefreshSubVisual(index, false);
            game.Log($"「{w.name}」を装着　残り{loot.SubUses}回");
            loot.Take();
            Nearby = null;
            EndChoice();
        }

        static DuelLoot PlaceSub(DuelWeapon weapon, int uses, Vector3 at)
        {
            var loot = DuelLoot.Place(weapon, at);
            loot.SubUses = uses;
            return loot;
        }

        void InitSubAnchors()
        {
            subAnchors[0] = rig != null && rig.mountShoulderL != null ? rig.mountShoulderL
                : MakeSubAnchor("SubShoulderL", transform, transform.TransformPoint(new Vector3(-1.6f, 7f, 0f)));
            subAnchors[1] = rig != null && rig.mountShoulderR != null ? rig.mountShoulderR
                : MakeSubAnchor("SubShoulderR", transform, transform.TransformPoint(new Vector3(1.6f, 7f, 0f)));
            var bones = rig != null ? rig.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
            for (int side = 0; side < Sides; side++)
            {
                string prefix = side == 0 ? "L" : "R";
                float sign = side == 0 ? -1f : 1f;
                var knee = Array.Find(bones, t => t.name == prefix + "_膝_Y");
                var ankle = Array.Find(bones, t => t.name == prefix + "_足首前後_Y");
                Vector3 at = knee != null && ankle != null
                    ? Vector3.Lerp(knee.position, ankle.position, 0.48f) + transform.right * (sign * 0.95f)
                    : transform.TransformPoint(new Vector3(sign * 2f, 1.8f, 0f));
                subAnchors[2 + side] = MakeSubAnchor("SubLeg" + prefix, knee != null ? knee : transform, at);
            }
        }

        Transform MakeSubAnchor(string name, Transform parent, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.SetPositionAndRotation(position, transform.rotation);
            return anchor;
        }

        void RefreshSubVisual(int slot, bool fling)
        {
            var old = subVisuals[slot];
            if (old != null)
            {
                if (fling) Fling(old);
                else
                {
                    old.gameObject.SetActive(false);
                    Destroy(old.gameObject);
                }
            }
            var w = subMounts[slot].Weapon;
            subVisuals[slot] = w != null ? CreateVisual(w, subAnchors[slot]) : null;
        }

        void TickSubInput()
        {
            bool mode = SubModeHeld;
            if (mode != wasSubMode)
            {
                player.ClearBufferedInput();
                ClearBufferedInput();
                wasSubMode = mode;
            }
            if (!BlocksMainAttack) return;
            blockR1 |= ProtoInput.RightHandHeld;
            blockR2 |= ProtoInput.RightShoulderHeld;
            blockL2 |= RawRangedHeld;
        }

        public bool SubSight(Camera camera, out float halfW, out float halfH)
        {
            halfW = halfH = 0f;
            if (camera == null || !SubAiming || !player.LockOn) return false;
            float vertical = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = Mathf.Tan(SubSightAngle * Mathf.Deg2Rad);
            halfW = Mathf.Min(width, 0.96f * vertical * camera.aspect);
            halfH = Mathf.Min(width * SubSightAspect, 0.9f * vertical);
            return true;
        }

        internal void TickSubCombat(Camera camera)
        {
            if (game.stage == null || game.stage.Cleared)
            {
                StopSubVolley();
                return;
            }
            Array.Clear(subTargets, 0, subTargets.Length);
            if (camera != null && SubAiming && !SubFiring)
            {
                if (SubSight(camera, out float halfW, out float halfH))
                {
                    viewFrame = -1;
                    for (int i = 0; i < SubSlotCount; i++)
                    {
                        var sub = subMounts[i];
                        if (sub.Uses > 0 && sub.Weapon.GuidedSub)
                            subTargets[i] = AimTarget(camera, halfW, halfH, SubMuzzle(i), sub.Weapon.Primary.range, out _);
                    }
                }
                int start = ProtoInput.RightShoulderPressed && PairHasUses(0) ? 0
                    : ProtoInput.Attack && PairHasUses(Sides) ? Sides : -1;
                if (start >= 0 && Time.deltaTime > 0f)
                    BeginSubVolley(start, AimPointAlong(camera.transform.position, camera.transform.forward, player.LockOn));
            }
            if (SubFiring) FireSubRounds();
        }

        bool PairHasUses(int start) => subMounts[start].Uses > 0 || subMounts[start + 1].Uses > 0;

        Vector3 SubMuzzle(int slot) =>
            subVisuals[slot].TransformPoint(new Vector3(0f, 0f, subMounts[slot].Weapon.scale.z * 0.5f + 0.15f));

        void BeginSubVolley(int start, Vector3 aim)
        {
            for (int i = start; i < start + Sides; i++)
            {
                if (subMounts[i].Uses <= 0) continue;
                var w = subMounts[i].Weapon;
                subMounts[i].Uses--;
                subLaunches[i] = new SubLaunch
                {
                    Weapon = w, Rounds = w.Primary.burstCount, NextAt = Time.time, Aim = aim,
                    Target = subTargets[i],
                };
                pendingSubRounds += w.Primary.burstCount;
            }
            ClearBufferedInput();
            player.ClearBufferedInput();
        }

        void FireSubRounds()
        {
            for (int i = 0; i < SubSlotCount; i++)
            {
                ref var launch = ref subLaunches[i];
                if (launch.Rounds <= 0) continue;
                while (launch.Rounds > 0 && Time.time >= launch.NextAt)
                {
                    var w = launch.Weapon;
                    var shot = w.Primary;
                    if (w.DeploysFlare)
                        DeployFlare(i);
                    else
                    {
                        var from = SubMuzzle(i);
                        var target = player.LockOn && launch.Target != null && !launch.Target.Dead ? launch.Target : null;
                        Vector3 direction = (launch.Aim - from).normalized;
                        if (direction == Vector3.zero) direction = transform.forward;
                        if (target != null) direction = (direction + Vector3.up * 0.22f).normalized;
                        var color = w.GuidedSub || shot.blastRadius <= 0f ? BulletColor : ShellColor;
                        ProtoFx.Blast(from, 1.2f, color, 0.09f);
                        StartCoroutine(Fly(from, direction, shot, color, false, SpeedOf(shot), false,
                            target, MissileTurn, subFlightVersion, inheritMovement: player.LockOn));
                    }
                    launch.Rounds--;
                    pendingSubRounds--;
                    launch.NextAt += shot.burstInterval;
                }
                if (launch.Rounds == 0) RetireSpentSub(i);
            }
        }

        void RetireSpentSub(int slot)
        {
            if (subMounts[slot].Weapon == null || subMounts[slot].Uses > 0) return;
            subMounts[slot] = default;
            RefreshSubVisual(slot, true);
        }

        internal void StopSubVolley()
        {
            if (!SubFiring) return;
            pendingSubRounds = 0;
            Array.Clear(subLaunches, 0, subLaunches.Length);
            for (int i = 0; i < SubSlotCount; i++) RetireSpentSub(i);
        }

        SubMount[] keptSubMounts;
        readonly HashSet<DuelLoot> bossSubPlaced = new();
        readonly List<(DuelWeapon Weapon, int Uses, Vector3 At)> bossSubPicked = new();

        void KeepSubWeapons() => keptSubMounts = (SubMount[])subMounts.Clone();

        void RestoreSubWeapons()
        {
            foreach (var loot in bossSubPlaced)
                if (loot != null) Destroy(loot.gameObject);
            foreach (var saved in bossSubPicked) PlaceSub(saved.Weapon, saved.Uses, saved.At);
            Array.Copy(keptSubMounts, subMounts, SubSlotCount);
            for (int i = 0; i < SubSlotCount; i++)
            {
                if (subMounts[i].Uses <= 0) subMounts[i] = default;
                RefreshSubVisual(i, false);
            }
        }

        void ForgetKeptSubWeapons()
        {
            keptSubMounts = null;
            bossSubPlaced.Clear();
            bossSubPicked.Clear();
        }

        int LoseSubWeapons()
        {
            ResetSubCombat();
            int lost = 0;
            for (int i = 0; i < SubSlotCount; i++)
            {
                if (subMounts[i].Weapon == null) continue;
                lost++;
                subMounts[i] = default;
                RefreshSubVisual(i, true);
            }
            return lost;
        }

        internal void ResetSubCombat()
        {
            StopSubVolley();
            subFlightVersion++;
            ClearFlares();
            Array.Clear(subTargets, 0, subTargets.Length);
        }
    }
}
