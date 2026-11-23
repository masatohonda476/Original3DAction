using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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

        public readonly struct Carried
        {
            public readonly DuelWeapon Weapon;
            public readonly Memo State;

            public Carried(DuelWeapon weapon, Memo state)
            {
                Weapon = weapon;
                State = state;
            }
        }

        public enum WeaponAction { Equip, KeepOrDrop, Ammo, Heal }

        const float SalvageHoldSeconds = 0.6f;
        readonly List<Carried> carried = new();
        public IReadOnlyList<Carried> CarriedWeapons => carried;
        public float CarriedWeight { get; private set; }
        public float TotalWeight => EquippedWeight + CarriedWeight;
        public DuelLoot Nearby { get; private set; }
        public DuelLoot Choosing { get; private set; }
        public bool ChoiceOpen => Choosing != null;
        public bool ChoiceLocksInput => ChoiceOpen || choiceEndFrame == Time.frameCount;
        public WeaponAction SelectedAction { get; private set; }
        public int CarriedChoiceIndex { get; private set; } = -1;
        public float SalvageProgress { get; private set; }
        int choiceEndFrame = -1;
        float salvageStartedAt;
        ButtonControl salvageButton;

        static ButtonControl PressedConfirm()
        {
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame) return pad.buttonSouth;
            var keyboard = Keyboard.current;
            if (keyboard == null) return null;
            if (keyboard.spaceKey.wasPressedThisFrame) return keyboard.spaceKey;
            return keyboard.enterKey.wasPressedThisFrame ? keyboard.enterKey : null;
        }

        void UpdateChoice()
        {
            if (!ChoiceOpen)
            {
                if (Nearby == null || !ProtoInput.PickUp) return;
                if (Nearby.Item is DuelItem itemKind)
                {
                    PickUpItem(itemKind, Nearby);
                    return;
                }
                Choosing = Nearby;
                ResetAction();
                ClearBufferedInput();
                player.ClearBufferedInput();
                return;
            }

            if (Choosing != Nearby || ProtoInput.ChoiceCancel)
            {
                EndChoice();
                return;
            }

            var loot = Choosing;
            var item = new Carried(loot.Weapon, loot.Memo);
            if (!UpdateWeaponAction(item, false)) return;
            if (SelectedAction == WeaponAction.Equip)
            {
                var old = Mount(item);
                var placed = DuelLoot.Place(old.Weapon, loot.transform.position);
                if (old.State != null) placed.Memo = old.State;
                placed.LowerPriority();
                LogEquip(item.Weapon);
            }
            else if (SelectedAction == WeaponAction.KeepOrDrop)
            {
                carried.Add(item);
                CarriedWeight += item.Weapon.weight;
                game.Log($"「{item.Weapon.name}」を所持した");
            }
            else
            {
                Salvage(item, SelectedAction == WeaponAction.Heal);
            }

            loot.Take();
            Nearby = null;
            EndChoice();
        }

        public bool OpenCarriedChoice(int index)
        {
            if (!CanUseCarried || index < 0 || index >= carried.Count) return false;
            CarriedChoiceIndex = index;
            ResetAction();
            return true;
        }

        public void CloseCarriedChoice()
        {
            CarriedChoiceIndex = -1;
            ResetAction();
        }

        public void UpdateCarriedChoice()
        {
            int index = CarriedChoiceIndex;
            if (!CanUseCarried || index < 0 || index >= carried.Count || ProtoInput.MenuBack)
            {
                CloseCarriedChoice();
                return;
            }

            var item = carried[index];
            if (!UpdateWeaponAction(item, true)) return;
            if (SelectedAction == WeaponAction.Equip)
            {
                var old = Mount(item);
                carried[index] = old;
                CarriedWeight += old.Weapon.weight - item.Weapon.weight;
                LogEquip(item.Weapon);
            }
            else
            {
                if (SelectedAction == WeaponAction.KeepOrDrop)
                {
                    if (!DuelLoot.TryPlaceAtFeet(item.Weapon, player, out var placed)) return;
                    if (item.State != null) placed.Memo = item.State;
                    game.Log($"「{item.Weapon.name}」を床に置いた");
                }
                else
                {
                    Salvage(item, SelectedAction == WeaponAction.Heal);
                }
                carried.RemoveAt(index);
                CarriedWeight -= item.Weapon.weight;
            }
            CloseCarriedChoice();
        }

        public bool CanUseCarried => player.Current == DuelPlayer.State.Free && game.stage != null && !game.stage.Cleared;

        bool UpdateWeaponAction(Carried item, bool fromCarried)
        {
            bool up = Keyboard.current?.upArrowKey.wasPressedThisFrame == true || Gamepad.current?.dpad.up.wasPressedThisFrame == true;
            bool down = Keyboard.current?.downArrowKey.wasPressedThisFrame == true || Gamepad.current?.dpad.down.wasPressedThisFrame == true;
            if (up != down)
            {
                SelectedAction = (WeaponAction)(((int)SelectedAction + (up ? 3 : 1)) % 4);
                ResetSalvage();
                return false;
            }

            if (!CanChooseAction(item, fromCarried, SelectedAction))
            {
                ResetSalvage();
                return false;
            }
            var pressed = PressedConfirm();
            if (SelectedAction < WeaponAction.Ammo) return pressed != null;
            if (pressed != null)
            {
                salvageButton = pressed;
                salvageStartedAt = Time.unscaledTime;
            }
            if (salvageButton == null) return false;
            if (!salvageButton.device.added || !salvageButton.isPressed || salvageButton.wasReleasedThisFrame)
            {
                ResetSalvage();
                return false;
            }
            SalvageProgress = Mathf.Clamp01((Time.unscaledTime - salvageStartedAt) / SalvageHoldSeconds);
            return SalvageProgress >= 1f;
        }

        public bool CanChooseAction(Carried item, bool fromCarried, WeaponAction action) => action switch
        {
            WeaponAction.Equip => fromCarried || Fits(WeightAfterEquip(item.Weapon)),
            WeaponAction.KeepOrDrop => fromCarried ? DuelLoot.HasFloorAtFeet(player) : Fits(TotalWeight + item.Weapon.weight),
            WeaponAction.Ammo => AmmoFrom(item, false) > 0f,
            WeaponAction.Heal => HpFrom(item) > 0f || AmmoFrom(item, true) > 0f,
            _ => false,
        };

        public float WeightAfterEquip(DuelWeapon item) => TotalWeight - Get(item.Slot).weight + item.weight;
        bool Fits(float weight) => weight <= Rules.carryMax;
        float MagazineAmmo(Carried item) => item.Weapon.HasMagazine ? (item.State?.mag ?? 0) * item.Weapon.ammoCost : 0f;
        public float AmmoFrom(Carried item, bool heal) => Mathf.Clamp(
            MagazineAmmo(item) + (heal ? 0f : Rules.BreakAmmo(item.Weapon)), 0f, Mathf.Max(0f, Rules.ammoMax - Ammo));
        public float HpFrom(Carried item) => Mathf.Clamp(Rules.BreakHeal(item.Weapon), 0f, Mathf.Max(0f, player.MaxHP - player.HP));

        void Salvage(Carried item, bool heal)
        {
            float hp = heal ? HpFrom(item) : 0f;
            float gained = AddAmmo(AmmoFrom(item, heal));
            if (hp > 0f) player.Heal(hp);
            string notice = $"「{item.Weapon.name}」を解体";
            if (hp > 0f) notice += $"　回復 +{hp:0}";
            if (gained > 0f) notice += $"　弾 +{gained:0.#}";
            game.Log(notice);
        }

        void ResetSalvage()
        {
            salvageButton = null;
            SalvageProgress = 0f;
        }

        void ResetAction()
        {
            SelectedAction = WeaponAction.Equip;
            ResetSalvage();
        }

        void EndChoice()
        {
            Choosing = null;
            ResetAction();
            choiceEndFrame = Time.frameCount;
            blockR1 |= ProtoInput.RightHandHeld || ProtoInput.Attack;
            blockR2 |= ProtoInput.RightShoulderHeld || ProtoInput.RightShoulderPressed;
            blockL2 |= RawRangedHeld;
            blockFly |= ProtoInput.FlyHeld;
            ClearBufferedInput();
        }

        public void CancelChoice()
        {
            if (ChoiceOpen) EndChoice();
        }

        Carried Mount(Carried item)
        {
            var w = item.Weapon;
            var old = new Carried(Get(w.Slot), w.IsGun ? rangedMemo : null);
            if (w.IsGun)
            {
                StopBurst();
                rangedMemo = item.State ?? new Memo();
                reloadTime = bloom = 0f;
            }

            slots[(int)w.Slot] = w;
            if (w.IsBlade) player.ResetMeleeCombo();
            Refresh(w.Slot);
            return old;
        }

        void LogEquip(DuelWeapon weapon) =>
            game.Log($"{DuelWeapon.SlotName(weapon.Slot)}を「{weapon.name}」に入れ替えた");

        bool ammoKept;
        float keptAmmo;
        readonly Dictionary<Memo, int> keptMags = new();

        // ボス戦前のアイテムと、共通弾・銃ごとの残弾を控える。銃は床と所持品も含める。
        public void KeepBossSupplies()
        {
            KeepItems();
            ammoKept = true;
            // 再開時に連射は引き継がないので、入口時点の未発射分は弾として控える。
            keptAmmo = Mathf.Min(Rules.ammoMax, Ammo + RefundableBurstAmmo());
            keptMags.Clear();
            if (RangedWeapon.HasMagazine) keptMags[rangedMemo] = Magazine;
            foreach (var item in carried)
                if (item.Weapon.HasMagazine) keptMags[item.State] = item.State.mag;
            foreach (var loot in DuelLoot.All)
                if (loot != null && loot.Weapon?.HasMagazine == true) keptMags[loot.Memo] = loot.Memo.mag;
        }

        public void RestoreBossSupplies()
        {
            if (!ammoKept) return;
            RestoreItems();
            Ammo = keptAmmo;
            if (RangedWeapon.HasMagazine) RestoreMagazine(rangedMemo);
            foreach (var item in carried)
                if (item.Weapon.HasMagazine) RestoreMagazine(item.State);
            foreach (var loot in DuelLoot.All)
                if (loot != null && loot.Weapon?.HasMagazine == true) RestoreMagazine(loot.Memo);
            reloadTime = 0f;
            ForgetBossSupplies();
        }

        void RestoreMagazine(Memo memo) => memo.mag = keptMags.TryGetValue(memo, out int mag) ? mag : 0;

        public void ForgetBossSupplies()
        {
            ammoKept = false;
            keptMags.Clear();
            ForgetKeptItems();
        }

        void ResetWeapons(bool loaded)
        {
            slots[(int)DuelSlot.Melee] = DuelWeapon.BaseSword;
            player.ResetMeleeCombo();
            slots[(int)DuelSlot.Ranged] = DuelWeapon.BaseGun;
            slots[(int)DuelSlot.Back] = DuelWeapon.BaseThruster;
            rangedMemo = new Memo { mag = loaded ? DuelWeapon.BaseGun.magazine : 0 };
            reloadTime = bloom = 0f;
            lastFire = -10f;
            for (int i = 0; i < slots.Length; i++) Refresh((DuelSlot)i);
        }

        public int LoseAll()
        {
            int lost = carried.Count + LoseItems();
            carried.Clear();
            CarriedWeight = 0f;
            CloseCarriedChoice();
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
            StopBurst();
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
            if (w.UsesTwoHands && rig != null && rig.sword != null)
            {
                var grip = rig.sword;
                var visual = CreateTwoHandVisual(w, grip.parent, true);
                visual.SetLocalPositionAndRotation(grip.localPosition, grip.localRotation);
                visual.localScale = grip.localScale;
                visuals[i] = visual;
            }
            else if (slot == DuelSlot.Melee && rig != null && rig.sword != null)
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
            if (rig != null && rig.sword != null) rig.sword.gameObject.SetActive(!ranged && !MeleeWeapon.UsesTwoHands);
            var blade = visuals[(int)DuelSlot.Melee];
            if (blade != null) blade.gameObject.SetActive(!ranged);
            var gun = visuals[(int)DuelSlot.Ranged];
            if (gun != null) gun.gameObject.SetActive(ranged);
        }

        /// <summary>武装を落とし物や機体に表示する仮の形。</summary>
        public static Transform CreateVisual(DuelWeapon w, Transform parent)
        {
            if (w.UsesTwoHands) return CreateTwoHandVisual(w, parent, false);
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
