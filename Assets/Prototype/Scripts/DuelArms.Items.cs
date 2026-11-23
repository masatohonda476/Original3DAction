using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Prototype
{
    public partial class DuelArms
    {
        public const int ItemSlotCount = 4;
        const float ItemSelectHold = 0.4f, ItemUseSeconds = 0.35f, FeedbackSeconds = 1.4f;
        readonly DuelItem?[] itemSlots = { DuelItem.RepairArmor, DuelItem.Ammo, null, null };
        readonly int[] itemStock = new int[DuelItems.Count];
        public int SelectedItemSlot { get; private set; }
        public DuelItem? ItemAt(int slot) => itemSlots[slot];
        public int ItemCount(DuelItem item) => itemStock[(int)item];

        ButtonControl itemSelectButton;
        float itemSelectAt;
        DuelItem? usingItem;
        float itemUseAt;
        int itemInputBlockedFrame = -1;
        string itemNotice;
        float itemNoticeAt = -10f;
        DuelItem gainedItem;
        float itemGain, itemGainAt = -10f;

        public bool UsingItem => usingItem.HasValue;
        public float ItemUseProgress => UsingItem ? Mathf.Clamp01((Time.time - itemUseAt) / ItemUseSeconds) : 0f;
        public string ItemNotice => Time.unscaledTime - itemNoticeAt < FeedbackSeconds ? itemNotice : null;
        public float RecentItemGain(DuelItem item) =>
            gainedItem == item && Time.unscaledTime - itemGainAt < FeedbackSeconds ? itemGain : 0f;

        public void RegisterItem(int slot, DuelItem? item)
        {
            int old = item.HasValue ? Array.IndexOf(itemSlots, item) : -1;
            if (old >= 0 && old != slot) itemSlots[old] = itemSlots[slot];
            itemSlots[slot] = item;
            if (!itemSlots[SelectedItemSlot].HasValue) SelectNextItem();
        }

        void SelectNextItem()
        {
            for (int offset = 1; offset <= ItemSlotCount; offset++)
            {
                int slot = (SelectedItemSlot + offset) % ItemSlotCount;
                if (!itemSlots[slot].HasValue) continue;
                SelectedItemSlot = slot;
                return;
            }
        }

        void UpdateItemSelection()
        {
            if (itemSelectButton == null)
            {
                var up = Gamepad.current?.dpad.up;
                if (up?.wasPressedThisFrame != true) up = Keyboard.current?.upArrowKey;
                if (up?.wasPressedThisFrame != true) return;
                itemSelectButton = up;
                itemSelectAt = Time.unscaledTime;
            }

            if (!itemSelectButton.device.added)
            {
                itemSelectButton = null;
                return;
            }
            if (Time.unscaledTime - itemSelectAt >= ItemSelectHold)
            {
                SelectedItemSlot = 0;
                itemSelectButton = null;
            }
            else if (itemSelectButton.wasReleasedThisFrame || !itemSelectButton.isPressed)
            {
                SelectNextItem();
                itemSelectButton = null;
            }
        }

        // 回避が成立したフレームは Enter(State.Dodge) で中断する。被弾や怯みの間も使用は進む。
        internal void TickItems()
        {
            if (DuelGame.Paused || player.Dead || game.stage == null || game.stage.Cleared) return;
            if (itemInputBlockedFrame == Time.frameCount) return;

            if (usingItem is DuelItem pending)
            {
                if (ItemUseProgress >= 1f)
                {
                    usingItem = null;
                    UseItem(pending);
                }
                return;
            }

            if (ChoiceLocksInput)
            {
                itemSelectButton = null;
                return;
            }

            UpdateItemSelection();
            bool down = Gamepad.current?.dpad.down.wasPressedThisFrame == true ||
                        Keyboard.current?.downArrowKey.wasPressedThisFrame == true;
            if (!down || player.Current != DuelPlayer.State.Free || roundsPending > 0 || SubFiring) return;
            if (itemSlots[SelectedItemSlot] is not DuelItem item)
            {
                ShowItemNotice("未登録のスロット");
                return;
            }
            if (!CanUseItem(item)) return;
            itemSelectButton = null;
            itemNotice = null;
            itemUseAt = Time.time;
            usingItem = item;
        }

        bool CanUseItem(DuelItem item)
        {
            if (ItemCount(item) == 0)
            {
                ShowItemNotice("所持数が0");
                return false;
            }
            bool repair = item == DuelItem.RepairArmor;
            if (repair ? player.HP < player.MaxHP : Ammo < Rules.ammoMax) return true;
            ShowItemNotice(repair ? "HP満タン" : "共通の弾が満タン");
            return false;
        }

        void UseItem(DuelItem item)
        {
            if (!CanUseItem(item)) return;
            float gain;
            if (item == DuelItem.RepairArmor)
            {
                float before = player.HP;
                player.Heal(player.MaxHP * DuelItems.RepairFraction);
                gain = player.HP - before;
            }
            else gain = AddAmmo(DuelItems.AmmoAmount);

            itemStock[(int)item]--;
            gainedItem = item;
            itemGain = gain;
            itemGainAt = Time.unscaledTime;
        }

        void ShowItemNotice(string notice)
        {
            itemNotice = notice;
            itemNoticeAt = Time.unscaledTime;
        }

        internal void InterruptItemUse()
        {
            itemInputBlockedFrame = Time.frameCount;
            if (!UsingItem) return;
            usingItem = null;
            ShowItemNotice("アイテムの使用を中断");
        }

        // メニューは使用の進捗を時間停止で保持し、再開・ステージ終了では使用状態も破棄する。
        internal void ResetItemInput(bool cancelUse = true)
        {
            itemInputBlockedFrame = Time.frameCount;
            itemSelectButton = null;
            if (cancelUse) usingItem = null;
            itemNotice = null;
            itemGainAt = -10f;
        }

        int[] keptItemStock;
        DuelItem?[] keptItemSlots;
        int keptSelectedItemSlot;
        readonly List<(DuelItem Item, Vector3 Position)> bossPickups = new();

        void PickUpItem(DuelItem item, DuelLoot loot)
        {
            itemStock[(int)item]++;
            if (keptItemStock != null) bossPickups.Add((item, loot.transform.position));
            game.Log($"「{DuelItems.Get(item).Name}」を1個入手");
            loot.Take();
            Nearby = null;
        }

        void KeepItems()
        {
            keptItemStock = (int[])itemStock.Clone();
            keptItemSlots = (DuelItem?[])itemSlots.Clone();
            keptSelectedItemSlot = SelectedItemSlot;
            bossPickups.Clear();
        }

        void RestoreItems()
        {
            Array.Copy(keptItemStock, itemStock, itemStock.Length);
            Array.Copy(keptItemSlots, itemSlots, itemSlots.Length);
            SelectedItemSlot = keptSelectedItemSlot;
            foreach (var pickup in bossPickups) DuelLoot.PlaceItem(pickup.Item, pickup.Position);
            ResetItemInput();
        }

        void ForgetKeptItems()
        {
            keptItemStock = null;
            keptItemSlots = null;
            bossPickups.Clear();
        }

        int LoseItems()
        {
            int lost = 0;
            foreach (int count in itemStock) lost += count;
            Array.Clear(itemStock, 0, itemStock.Length);
            ResetItemInput();
            return lost;
        }
    }
}
