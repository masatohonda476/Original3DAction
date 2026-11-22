using HuntProto;
using UnityEngine;

namespace Prototype
{
    /// <summary>近接・遠距離・背中を一つずつ装備する。L2 を押している間だけ遠距離を構える。</summary>
    public partial class DuelArms : MonoBehaviour
    {
        const float PressBuffer = 0.2f;
        const float KeepAimAfterShot = 0.6f;
        DuelPlayer player;
        DuelGame game;
        MechRig rig;
        readonly DuelWeapon[] slots = new DuelWeapon[3];
        readonly Transform[] visuals = new Transform[3];
        Memo rangedMemo = new();
        float reloadTime, bloom;
        float lastFire = -10f;
        Vector3 inheritVel;
        bool wasRanged, blockR1, blockR2, blockL2, blockFly;
        float shotPressedAt = float.NegativeInfinity, specialPressedAt = float.NegativeInfinity;

        public DuelArmsRules Rules => DuelArmsRules.Of(game);
        public DuelWeapon Get(DuelSlot slot) => slots[(int)slot];
        public DuelWeapon MeleeWeapon => Get(DuelSlot.Melee);
        public DuelWeapon RangedWeapon => Get(DuelSlot.Ranged);
        public DuelWeapon BackW => Get(DuelSlot.Back);
        public float EquippedWeight => MeleeWeapon.weight + RangedWeapon.weight + BackW.weight;
        public float Mobility => Rules.MobilityAt(EquippedWeight);
        public float RunMul => BackW.runMul;
        public float DashMul => BackW.dashMul * Mobility;
        public float DodgeMul => BackW.dodgeMul * Mobility;
        public float EnCostMul => BackW.enCostMul;

        // 共用の入力クラスでは肩の名称。ここでは L2/E が持ち替え、R2/R が強攻撃・大技。
        public bool RangedHeld => !ChoiceLocksInput && !blockL2 && ProtoInput.LeftShoulderHeld;
        public bool Aiming => RangedHeld && player != null && !player.Dead && !player.MeleeCommitted;
        public bool CursorActive => Aiming && Rules.cursorMovable;
        // 固定照準では射撃入力と撃った直後だけ自動選択を待つ。L2 だけなら従来どおり敵を探す。
        public bool BlocksAutoLock => Aiming && (CursorActive ||
            (!blockR1 && ProtoInput.RightHandHeld) || (!blockR2 && ProtoInput.RightShoulderHeld) ||
            Time.time - lastFire < KeepAimAfterShot);
        public bool FlyBlocked => ChoiceLocksInput || blockFly;
        public bool LightPressed => !ChoiceLocksInput && !blockR1 && ProtoInput.Attack;
        public bool HeavyPressed => !ChoiceLocksInput && !blockR2 && ProtoInput.RightShoulderPressed;

        public float Ammo { get; private set; }
        public int Magazine => rangedMemo.mag;
        public float ReloadProgress => Mathf.Clamp01(reloadTime / ReloadInterval);
        public float Cooling => Mathf.Clamp01((rangedMemo.ready - Time.time) / Mathf.Max(0.01f, rangedMemo.cooldown));
        public float NoAmmoAt { get; private set; } = -10f;
        public float NoEnergyAt { get; private set; } = -10f;
        public float HitMarkAt { get; private set; } = -10f;
        public int HitMarkKind { get; private set; }
        float ReloadInterval => Mathf.Max(0.01f, Rules.reloadRoundTime);

        public void Init(DuelPlayer p, DuelGame g, MechRig r)
        {
            player = p;
            game = g;
            rig = r;
            if (rig != null && rig.swordL != null) rig.swordL.gameObject.SetActive(false);
            ResetWeapons(true);
            Ammo = Mathf.Clamp(Rules.ammoStart, 0f, Rules.ammoMax);
        }

        public void Tick()
        {
            if (!ProtoInput.RightHandHeld) blockR1 = false;
            if (!ProtoInput.RightShoulderHeld) blockR2 = false;
            if (!ProtoInput.LeftShoulderHeld) blockL2 = false;
            if (!ProtoInput.FlyHeld) blockFly = false;
            if (!ProtoInput.BreakAmmoHeld) breakLeftHold = false;

            if (player.Dead || game.stage == null || game.stage.Cleared)
            {
                Nearby = null;
                CancelChoice();
                return;
            }

            DuelLoot.RestorePriorities(player);
            bool canPick = !player.Airborne && player.Current == DuelPlayer.State.Free;
            var selected = Choosing != null ? Choosing : Breaking;
            Nearby = canPick
                ? selected != null && selected.CanPick(player) ? selected : DuelLoot.Nearest(player)
                : null;
            UpdateChoice();

            bool ranged = RangedHeld;
            if (ranged != wasRanged)
            {
                ClearBufferedInput();
                wasRanged = ranged;
            }
            if (ranged && LightPressed) shotPressedAt = Time.time;
            if (ranged && HeavyPressed) specialPressedAt = Time.time;
            // 選択画面で入力を止めていても、実際に L2 を押している間は弾を詰めない。
            UpdateReload(!ProtoInput.LeftShoulderHeld);
        }

        void UpdateReload(bool melee)
        {
            var w = RangedWeapon;
            if (!melee || !w.HasMagazine || Magazine >= w.magazine || Ammo < w.ammoCost)
            {
                reloadTime = 0f;
                return;
            }

            // 一発が詰まった時点で共通の弾を引く。構え直すと次の一発は最初から。
            reloadTime += Time.deltaTime;
            while (reloadTime >= ReloadInterval && Magazine < w.magazine && Ammo >= w.ammoCost)
            {
                reloadTime -= ReloadInterval;
                Ammo -= w.ammoCost;
                rangedMemo.mag++;
            }
            if (Magazine >= w.magazine || Ammo < w.ammoCost) reloadTime = 0f;
        }

        public void UpdateCombat(bool canShoot)
        {
            inheritVel = Vector3.Lerp(inheritVel, player.Velocity,
                Rules.inheritSmoothTime > 0f ? 1f - Mathf.Exp(-3f * Time.deltaTime / Rules.inheritSmoothTime) : 1f);
            UpdateWeaponVisibility();
            if (Time.time - lastFire > Rules.recoilDelay)
                bloom = Mathf.MoveTowards(bloom, 0f, RangedWeapon.Primary.recoilRecover * Time.deltaTime);

            bool special = Time.time - specialPressedAt <= PressBuffer;
            UpdateCursor();
            if (!UpdateFrame(special, out Vector3 from, out Vector3 aim)) return;
            if (!canShoot || Time.deltaTime <= 0f || Time.time < rangedMemo.ready) return;
            if (!special && Time.time - shotPressedAt > PressBuffer) return;
            ClearBufferedInput();
            Fire(special, from, aim);
        }

        public void ClearBufferedInput() => shotPressedAt = specialPressedAt = float.NegativeInfinity;

        public void AddFatalAmmo()
        {
            float gained = AddAmmo(Rules.fatalAmmo);
            if (gained > 0f) game.Log($"致命攻撃で弾 +{gained:0.#}");
        }

        float AddAmmo(float amount)
        {
            float before = Ammo;
            Ammo = Mathf.Clamp(Ammo + amount, 0f, Rules.ammoMax);
            return Ammo - before;
        }
    }
}
