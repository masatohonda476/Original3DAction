using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype
{
    public partial class DuelArms
    {
        public const int AmmoPickupAmount = 30;
        const float RepairFraction = 0.4f, RepairSeconds = 0.35f, FeedbackSeconds = 1.4f;

        public int RepairCount { get; private set; }
        public bool UsingRepair { get; private set; }
        public float RepairProgress => UsingRepair ? Mathf.Clamp01((Time.time - repairStartedAt) / RepairSeconds) : 0f;
        public string RepairNotice => Time.unscaledTime - repairNoticeAt < FeedbackSeconds ? repairNotice : null;
        public float RecentRepairGain => Time.unscaledTime - repairGainAt < FeedbackSeconds ? repairGain : 0f;
        public float RecentAmmoPickup => Time.unscaledTime - ammoPickupAt < FeedbackSeconds ? ammoPickupGain : 0f;

        float repairStartedAt;
        string repairNotice;
        float repairNoticeAt = float.NegativeInfinity;
        float repairGain, repairGainAt = float.NegativeInfinity;
        float ammoPickupGain, ammoPickupAt = float.NegativeInfinity;
        int repairInputBlockedFrame = -1;

        int keptRepairs;
        readonly List<(DuelSupply Supply, Vector3 Position)> bossPickups = new();

        void PickUpSupply(DuelSupply supply, DuelLoot loot)
        {
            if (supply == DuelSupply.Ammo)
            {
                float gained = AddAmmo(AmmoPickupAmount);
                if (gained <= 0f) return;
                ammoPickupGain = gained;
                ammoPickupAt = Time.unscaledTime;
                game.Log($"弾薬を補給：共通の弾 +{gained:0.#}");
            }
            else
            {
                RepairCount++;
                game.Log($"補修用装甲を1個入手（残り {RepairCount}個）");
            }

            if (ammoKept) bossPickups.Add((supply, loot.transform.position));
            loot.Take();
            Nearby = null;
        }

        // 回避が成立したフレームは Enter(State.Dodge) で中断する。被弾や怯みの間も補修は進む。
        internal void TickRepair()
        {
            if (DuelGame.Paused || player.Dead || game.stage == null || game.stage.Cleared ||
                repairInputBlockedFrame == Time.frameCount) return;

            if (UsingRepair)
            {
                if (RepairProgress < 1f) return;
                UsingRepair = false;
                if (!CanRepair()) return;
                float before = player.HP;
                player.Heal(player.MaxHP * RepairFraction);
                RepairCount--;
                repairGain = player.HP - before;
                repairGainAt = Time.unscaledTime;
                return;
            }

            if (ChoiceLocksInput || player.Current != DuelPlayer.State.Free || roundsPending > 0 || SubFiring) return;
            bool up = Gamepad.current?.dpad.up.wasPressedThisFrame == true ||
                      Keyboard.current?.upArrowKey.wasPressedThisFrame == true;
            if (!up || !CanRepair()) return;
            repairNotice = null;
            repairStartedAt = Time.time;
            UsingRepair = true;
        }

        bool CanRepair()
        {
            if (RepairCount > 0 && player.HP < player.MaxHP) return true;
            ShowRepairNotice(RepairCount == 0 ? "補修用装甲がありません" : "HP満タン");
            return false;
        }

        void ShowRepairNotice(string notice)
        {
            repairNotice = notice;
            repairNoticeAt = Time.unscaledTime;
        }

        internal void InterruptRepair()
        {
            repairInputBlockedFrame = Time.frameCount;
            if (!UsingRepair) return;
            UsingRepair = false;
            ShowRepairNotice("補修を中断");
        }

        // メニューは経過時間を止め、再開・ステージ終了では補修中の状態も破棄する。
        internal void ResetSupplyInput(bool cancelRepair = true)
        {
            repairInputBlockedFrame = Time.frameCount;
            if (cancelRepair) UsingRepair = false;
            repairNotice = null;
            repairGainAt = ammoPickupAt = float.NegativeInfinity;
        }

        int LoseRepairs()
        {
            int lost = RepairCount;
            RepairCount = 0;
            ResetSupplyInput();
            return lost;
        }
    }
}
