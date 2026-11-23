using HuntProto;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype
{
    /// <summary>近接・遠距離・背中を一つずつ装備する。L2 を押している間だけ遠距離を構える。</summary>
    public partial class DuelArms : MonoBehaviour
    {
        const float PressBuffer = 0.2f;
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
        public float EquippedWeight => MeleeWeapon.weight + RangedWeapon.weight + BackW.weight + SubWeight;
        public float Mobility => Rules.MobilityAt(TotalWeight);
        public float DashMul => BackW.dashMul * Mobility;
        /// <summary>スラスターと装備全体の重さを反映した回避距離の倍率。</summary>
        public float DodgeDistanceMul => BackW.dodgeDistanceMul * Mobility;
        public float DodgeInvincibleMul => BackW.dodgeInvincibleMul;
        public float EnCostMul => BackW.enCostMul;

        // 共用の入力クラスでは肩の名称。ここでは L2/E が持ち替え、R2/R が強攻撃・大技。
        bool RangedInputHeld => !ChoiceLocksInput && !BlocksMainAttack && !blockL2 && ProtoInput.LeftShoulderHeld;
        static bool RawRangedHeld => Gamepad.current?.leftTrigger.isPressed == true || Keyboard.current?.eKey.isPressed == true;
        // メニュー明けの入力制限中も、押し続けている L2 で開始済みの連射だけ再開できる。
        public bool RangedHeld => RangedInputHeld ||
            (roundsPending > 0 && !DuelGame.Paused && !ChoiceLocksInput && RawRangedHeld);
        public bool Aiming => RangedHeld && player != null && !player.Dead && !player.MeleeCommitted;
        public bool BlocksAutoLock => FreeAim;
        public bool FlyBlocked => ChoiceLocksInput || blockFly;
        public bool LightPressed => !ChoiceLocksInput && !BlocksMainAttack && !blockR1 && ProtoInput.Attack;
        public bool HeavyPressed => !ChoiceLocksInput && !BlocksMainAttack && !blockR2 && ProtoInput.RightShoulderPressed;

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
            InitSubAnchors();
            Ammo = Mathf.Clamp(Rules.ammoStart, 0f, Rules.ammoMax);
        }

        public void Tick()
        {
            if (!ProtoInput.RightHandHeld) blockR1 = false;
            if (!ProtoInput.RightShoulderHeld) blockR2 = false;
            bool rawRangedHeld = RawRangedHeld;
            if (!rawRangedHeld)
            {
                blockL2 = false;
                StopBurst();
            }
            if (!ProtoInput.FlyHeld) blockFly = false;
            TickSubInput();

            if (player.Dead || game.stage == null || game.stage.Cleared)
            {
                Nearby = null;
                CancelChoice();
                ClearBufferedInput();
                StopBurst();
                return;
            }

            DuelLoot.RestorePriorities(player);
            bool canPick = !UsingItem && !SubFiring && !player.Airborne && player.Current == DuelPlayer.State.Free;
            var selected = Choosing;
            Nearby = canPick
                ? selected != null && selected.CanPick(player) ? selected : DuelLoot.Nearest(player)
                : null;
            UpdateChoice();

            bool ranged = RangedInputHeld;
            if (ranged != wasRanged)
            {
                ClearBufferedInput();
                wasRanged = ranged;
            }
            if (ranged && LightPressed) shotPressedAt = Time.time;
            if (ranged && HeavyPressed) specialPressedAt = Time.time;
            // 選択画面で入力を止めていても、実際に L2 を押している間は弾を詰めない。
            UpdateReload(!rawRangedHeld);
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

            float now = Time.time;
            bool special = roundsPending > 0 ? burstSpecial : now - specialPressedAt <= PressBuffer;
            bool aimReady = UpdateFrame(special, out Vector3 from, out Vector3 aim);
            if (!canShoot || (!aimReady && !ChoiceLocksInput))
            {
                StopBurst();
                return;
            }
            if (ChoiceLocksInput)
            {
                // 落とし物の選択中は戦闘の時間が進むので、連射の間隔と待ち時間だけ止める。
                if (roundsPending > 0)
                {
                    nextRoundAt += Time.deltaTime;
                    rangedMemo.ready += Time.deltaTime;
                }
                return;
            }
            if (Time.deltaTime <= 0f || game.stage == null || game.stage.Cleared) return;
            if (roundsPending > 0)
            {
                FirePendingRounds(now, from, aim);
                return;
            }
            if (now < rangedMemo.ready) return;
            var primary = RangedWeapon.Primary;
            bool auto = primary.autoFire && !blockR1 && ProtoInput.RightHandHeld && CanPayPrimary(RangedWeapon);
            if (!special && !auto && now - shotPressedAt > PressBuffer) return;

            ClearBufferedInput();
            StartFiring(special, from, aim);
        }

        public void ClearBufferedInput() => shotPressedAt = specialPressedAt = float.NegativeInfinity;

        internal void CheckPausedBurstRelease()
        {
            if (roundsPending > 0 && !RawRangedHeld) StopBurst();
        }

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
