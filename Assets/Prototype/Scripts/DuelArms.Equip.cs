using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelArms
    {
        // 弾倉と次に撃てる時刻は、地面に置いても銃の個体に付いて回る。
        public class Memo
        {
            public int mag;
            public float ready, cooldown;
        }

        public DuelLoot Nearby { get; private set; }
        public DuelLoot Choosing { get; private set; }
        public DuelLoot Breaking { get; private set; }
        public bool ChoiceOpen => Choosing != null || Breaking != null;
        public bool ChoiceLocksInput => ChoiceOpen || choiceEndFrame == Time.frameCount;
        public bool LockDpadTaken => Breaking != null || breakLeftHold;
        bool breakLeftHold;
        int choiceEndFrame = -1;

        void UpdateChoice()
        {
            bool cancel = ProtoInput.ChoiceCancel;
            if (Breaking != null)
            {
                breakLeftHold = true;
                if (Breaking != Nearby || ProtoInput.BreakNow || cancel) EndChoice();
                else if (ProtoInput.BreakAmmoNow) Salvage(Breaking, false);
                else if (ProtoInput.BreakHealNow) Salvage(Breaking, true);
                return;
            }
            if (Choosing != null)
            {
                if (Choosing != Nearby || cancel) EndChoice();
                else if (ProtoInput.EquipNow)
                {
                    var loot = Choosing;
                    EndChoice();
                    Equip(loot);
                }
                return;
            }
            if (Nearby == null) return;
            if (ProtoInput.EquipNow) Choosing = Nearby;
            else if (ProtoInput.BreakNow) Breaking = Nearby;
            if (ChoiceOpen)
            {
                ClearBufferedInput();
                player.ClearBufferedInput();
            }
        }

        public int SalvageAmmo(DuelWeapon w) => Rules.BreakAmmo(w);
        public int SalvageHeal(DuelWeapon w) => Rules.BreakHeal(w);

        public float MagAmmo(DuelLoot loot) =>
            loot != null && loot.Weapon.HasMagazine ? loot.Memo.mag * loot.Weapon.ammoCost : 0f;

        void Salvage(DuelLoot loot, bool heal)
        {
            float back = MagAmmo(loot);
            float ammoGain = heal ? back : back + SalvageAmmo(loot.Weapon);
            bool canHeal = heal && player.HP < player.MaxHP && SalvageHeal(loot.Weapon) > 0;
            if (!canHeal && (Ammo >= Rules.ammoMax || ammoGain <= 0f))
            {
                game.Log(heal ? "回復も弾倉からの補給もできない" : "共通の弾を補給できない");
                return;
            }

            float hp = player.HP;
            if (canHeal) player.Heal(SalvageHeal(loot.Weapon));
            float gained = AddAmmo(ammoGain);
            string notice = $"{loot.Weapon.name}を解体";
            if (player.HP > hp) notice += $"　回復 +{player.HP - hp:0}";
            if (gained > 0f) notice += $"　弾 +{gained:0.#}";
            game.Log(notice);
            loot.Take();
            Nearby = null;
            EndChoice();
        }

        void EndChoice()
        {
            Choosing = Breaking = null;
            choiceEndFrame = Time.frameCount;
            blockR1 |= ProtoInput.RightHandHeld || ProtoInput.Attack;
            blockR2 |= ProtoInput.RightShoulderHeld || ProtoInput.RightShoulderPressed;
            blockL2 |= ProtoInput.LeftShoulderHeld;
            blockFly |= ProtoInput.FlyHeld;
            ClearBufferedInput();
        }

        public void CancelChoice()
        {
            if (ChoiceOpen) EndChoice();
        }

        void Equip(DuelLoot loot)
        {
            var w = loot.Weapon;
            var old = DuelLoot.Place(Get(w.Slot), loot.transform.position);
            old.LowerPriority();
            if (w.IsGun)
            {
                old.Memo = rangedMemo;
                rangedMemo = loot.Memo;
                reloadTime = bloom = 0f;
            }

            slots[(int)w.Slot] = w;
            Refresh(w.Slot);
            loot.Take();
            Nearby = null;
            game.Log($"{DuelWeapon.SlotName(w.Slot)}を「{w.name}」に入れ替えた");
        }

        bool ammoKept;
        float keptAmmo;
        readonly Dictionary<Memo, int> keptMags = new();

        // ボス戦前の共通弾と銃ごとの残弾を控える。戦闘中に詰めた弾が再開で増えないよう、床の銃も含める。
        public void KeepAmmo()
        {
            ammoKept = true;
            keptAmmo = Ammo;
            keptMags.Clear();
            if (RangedWeapon.HasMagazine) keptMags[rangedMemo] = Magazine;
            foreach (var loot in DuelLoot.All)
                if (loot != null && loot.Weapon.HasMagazine) keptMags[loot.Memo] = loot.Memo.mag;
        }

        public void RestoreKeptAmmo()
        {
            if (!ammoKept) return;
            Ammo = keptAmmo;
            if (RangedWeapon.HasMagazine) RestoreMagazine(rangedMemo);
            foreach (var loot in DuelLoot.All)
                if (loot != null && loot.Weapon.HasMagazine) RestoreMagazine(loot.Memo);
            reloadTime = 0f;
            ForgetKeptAmmo();
        }

        void RestoreMagazine(Memo memo) => memo.mag = keptMags.TryGetValue(memo, out int mag) ? mag : 0;

        public void ForgetKeptAmmo()
        {
            ammoKept = false;
            keptMags.Clear();
        }

        void ResetWeapons(bool loaded)
        {
            slots[(int)DuelSlot.Melee] = DuelWeapon.BaseSword;
            slots[(int)DuelSlot.Ranged] = DuelWeapon.BaseGun;
            slots[(int)DuelSlot.Back] = DuelWeapon.BaseThruster;
            rangedMemo = new Memo { mag = loaded ? DuelWeapon.BaseGun.magazine : 0 };
            reloadTime = bloom = 0f;
            lastFire = -10f;
            for (int i = 0; i < slots.Length; i++) Refresh((DuelSlot)i);
        }

        public int LoseAll()
        {
            int lost = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var w = slots[i];
                if (w == DuelWeapon.BaseSword || w == DuelWeapon.BaseGun || w == DuelWeapon.BaseThruster) continue;
                lost++;
                Transform v = visuals[i];
                if (w.IsBlade && v == null && rig != null && rig.sword != null)
                {
                    v = CreateVisual(w, null);
                    v.SetPositionAndRotation(rig.sword.position, rig.sword.rotation);
                }
                if (v == null) continue;
                visuals[i] = null;
                v.gameObject.SetActive(true);
                Fling(v);
            }
            ResetWeapons(false);
            CancelChoice();
            ClearBufferedInput();
            Ammo = 0f;
            return lost;
        }

        void Refresh(DuelSlot slot)
        {
            int i = (int)slot;
            var w = slots[i];
            if (visuals[i] != null)
            {
                visuals[i].gameObject.SetActive(false);
                Destroy(visuals[i].gameObject);
            }
            visuals[i] = null;
            if (slot == DuelSlot.Melee && rig != null && rig.sword != null)
            {
                var block = new MaterialPropertyBlock();
                if (w.rarity > 0) block.SetColor("_BaseColor", w.Color);
                foreach (var renderer in rig.sword.GetComponentsInChildren<Renderer>(true))
                    renderer.SetPropertyBlock(block);
            }
            else
            {
                Transform mount = rig == null ? null : slot == DuelSlot.Back ? rig.mountBack : rig.mountRightArm;
                var visual = CreateVisual(w, mount != null ? mount : transform);
                if (mount == null)
                    visual.localPosition = slot == DuelSlot.Back
                        ? new Vector3(0f, game.mechHeight * 0.85f, -1.8f)
                        : new Vector3(2.6f, game.mechHeight * 0.5f, 0.8f);
                else if (slot != DuelSlot.Back)
                    visual.localPosition = new Vector3(0f, -0.45f, 1.2f) * (game.mechHeight / 9.06f);
                visuals[i] = visual;
            }
            UpdateWeaponVisibility();
        }

        public void UpdateWeaponVisibility()
        {
            bool ranged = Aiming;
            if (rig != null && rig.sword != null) rig.sword.gameObject.SetActive(!ranged);
            var blade = visuals[(int)DuelSlot.Melee];
            if (blade != null) blade.gameObject.SetActive(!ranged);
            var gun = visuals[(int)DuelSlot.Ranged];
            if (gun != null) gun.gameObject.SetActive(ranged);
        }

        /// <summary>武装を落とし物や機体に表示する仮の形。</summary>
        public static Transform CreateVisual(DuelWeapon w, Transform parent)
        {
            var root = new GameObject(w.name).transform;
            root.SetParent(parent, false);
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root, false);
            part.transform.localScale = w.IsBlade ? new Vector3(0.4f, 1f, 4.5f) : w.scale;
            part.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(w.Color);
            return root;
        }

        /// <summary>
        /// 外れた武装を、回しながら外へ吹き飛ばして少しして消す。当たり判定は付けず自前で動かす
        /// (カメラの壁よけ・足もとのレイ・敵の見通しに当たらないよう、機体と同じ Ignore Raycast の層にも置く)。
        /// </summary>
        void Fling(Transform v)
        {
            v.SetParent(null, true);
            foreach (var c in v.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            foreach (var tr in v.GetComponentsInChildren<Transform>()) tr.gameObject.layer = DuelFighter.FighterLayer;
            ProtoFx.Blast(v.position, 3f, new Color(1f, 0.6f, 0.2f), 0.25f);
            DuelSpark.Burst(v.position, Vector3.up, new Color(1f, 0.75f, 0.4f), 12, 1f);
            Vector3 out_ = v.position - player.Chest;
            out_.y = 0f;
            Vector3 vel = (out_.sqrMagnitude > 0.01f ? out_.normalized : Random.onUnitSphere) * 14f + Vector3.up * 16f;
            StartCoroutine(Debris(v, vel, Random.insideUnitSphere * 360f, 2.5f));
        }

        /// <summary>破片の動き:重力で落ち、床で弾んで勢いを失い、life 秒で消える(床は真下へのレイで、移動量を含めて調べる)。</summary>
        static IEnumerator Debris(Transform v, Vector3 vel, Vector3 spin, float life)
        {
            float t = 0f;
            while (t < life && v != null)
            {
                float dt = Time.deltaTime;
                t += dt;
                vel.y -= DuelFighter.Gravity * dt;
                Vector3 p = v.position, next = p + vel * dt;
                if (vel.y < 0f && Physics.Raycast(new Vector3(next.x, p.y, next.z), Vector3.down, out var hit, p.y - next.y + 0.5f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    next.y = hit.point.y + 0.5f;
                    vel = new Vector3(vel.x * 0.5f, -vel.y * 0.3f, vel.z * 0.5f);
                    spin *= 0.5f;
                }
                v.position = next;
                v.Rotate(spin * dt, Space.World);
                yield return null;
            }
            if (v != null) Destroy(v.gameObject);
        }
    }
}
