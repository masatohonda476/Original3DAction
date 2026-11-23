using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelGame
    {
        int registrationSlot, itemOption = -1;

        static string ItemStockText(DuelArms arms, DuelItem item) =>
            $"{DuelItems.Get(item).Name} ×{arms.ItemCount(item)}";

        void UpdateItemRegistration(DuelArms arms, int direction)
        {
            if (itemOption >= 0)
            {
                if (ProtoInput.MenuBack) itemOption = -1;
                else if (direction != 0)
                {
                    int count = DuelItems.Count + 1;
                    itemOption = (itemOption + direction + count) % count;
                }
                else if (ProtoInput.MenuConfirm)
                {
                    arms.RegisterItem(registrationSlot, itemOption == 0 ? null : (DuelItem)(itemOption - 1));
                    itemOption = -1;
                }
                return;
            }

            if (ProtoInput.MenuBack) SetMenuPage(MenuPage.Carried);
            else if (direction != 0)
                registrationSlot = (registrationSlot + direction + DuelArms.ItemSlotCount) % DuelArms.ItemSlotCount;
            else if (ProtoInput.MenuConfirm)
                itemOption = arms.ItemAt(registrationSlot) is DuelItem item ? (int)item + 1 : 0;
        }

        void DrawItemRegistration(DuelArms arms)
        {
            MenuBackdrop();
            const float width = 680f, height = 410f, pad = 18f, rowH = 44f;
            float x = (SW - width) * 0.5f, y = (SH - height) * 0.5f;
            MenuPanel(new Rect(x, y, width, height));
            GUI.Label(new Rect(x, y + 8f, width, 36f), "アイテム登録", viewTitleStyle);
            GUI.Label(new Rect(x + pad, y + 54f, 245f, 25f), "スロット", viewHeadStyle);
            GUI.Label(new Rect(x + 284f, y + 54f, 378f, 25f), "アイテム", viewHeadStyle);

            for (int i = 0; i < DuelArms.ItemSlotCount; i++)
            {
                var row = new Rect(x + pad, y + 86f + i * rowH, 232f, rowH - 4f);
                if (i == registrationSlot) SelectionBand(row);
                if (arms.ItemAt(i) is DuelItem item)
                    DrawItemIcon(new Rect(row.x + 37f, row.y + 10f, 19f, 19f), item, DuelItems.Get(item).Tint);
                string name = arms.ItemAt(i) is DuelItem kind ? ItemStockText(arms, kind) : "未登録";
                GUI.Label(new Rect(row.x + 12f, row.y + 7f, 28f, 24f), $"{i + 1}", label);
                GUI.Label(new Rect(row.x + 66f, row.y + 9f, row.width - 70f, 24f), name, small);
            }

            const int visible = 6;
            int first = Mathf.Clamp(itemOption - visible / 2, 0, Mathf.Max(0, DuelItems.Count + 1 - visible));
            for (int option = first; option < Mathf.Min(first + visible, DuelItems.Count + 1); option++)
            {
                var row = new Rect(x + 282f, y + 86f + (option - first) * rowH, width - 282f - pad, rowH - 4f);
                if (option == itemOption) SelectionBand(row);
                if (option == 0)
                {
                    GUI.Label(new Rect(row.x + 14f, row.y + 8f, row.width - 28f, 24f), "未登録", small);
                    continue;
                }

                var item = (DuelItem)(option - 1);
                var info = DuelItems.Get(item);
                DrawItemIcon(new Rect(row.x + 12f, row.y + 10f, 18f, 18f), item, info.Tint);
                GUI.Label(new Rect(row.x + 38f, row.y + 8f, 150f, 24f), ItemStockText(arms, item), small);
                GUI.Label(new Rect(row.x + 182f, row.y + 8f, row.width - 190f, 24f), info.Effect, rightSmall);
            }
            string confirm = ProtoInput.L(ProtoInput.Btn.Confirm), back = ProtoInput.L(ProtoInput.Btn.Back);
            string help = itemOption >= 0 ? $"↑↓ アイテムを選ぶ　　{confirm} 登録　　{back} 取り消す"
                : $"↑↓ スロットを選ぶ　　{confirm} 変更　　{back} 戻る";
            GUI.Label(new Rect(x + pad, y + height - 38f, width - pad * 2f, 24f), help, center);
        }

        void DrawItemsHud(DuelArms arms)
        {
            const float x = 24f, width = 286f, height = 74f, cell = 27f;
            float y = ArmsTop;
            MenuPanel(new Rect(x, y, width, height));
            for (int i = 0; i < DuelArms.ItemSlotCount; i++)
            {
                var r = new Rect(x + 8f + i * (cell + 5f), y + 7f, cell, cell);
                bool selected = i == arms.SelectedItemSlot;
                Chip(r, selected ? new Color(0.18f, 0.34f, 0.44f) : new Color(1f, 1f, 1f, 0.08f));
                if (arms.ItemAt(i) is DuelItem item)
                {
                    var tint = DuelItems.Get(item).Tint;
                    tint.a = arms.ItemCount(item) > 0 ? 1f : 0.35f;
                    DrawItemIcon(new Rect(r.x + 5f, r.y + 5f, cell - 10f, cell - 10f), item, tint);
                }
                if (selected) Chip(new Rect(r.x, r.yMax - 2f, r.width, 2f), ActiveArms);
            }
            var selectedItem = arms.ItemAt(arms.SelectedItemSlot);
            GUI.Label(new Rect(x + 139f, y + 8f, width - 147f, 24f),
                selectedItem is DuelItem kind ? ItemStockText(arms, kind) : "未登録", small);
            string notice = arms.UsingItem ? "使用中" : arms.ItemNotice ?? "↑ 切替　↑長押し 先頭　↓ 使用";
            GUI.color = arms.UsingItem ? ActiveArms : new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(x + 8f, y + 38f, width - 16f, 23f), notice, cellNameSmall);
            GUI.color = Color.white;
            if (arms.UsingItem)
                Bar(new Rect(x + 8f, y + height - 9f, width - 16f, 3f), arms.ItemUseProgress, ActiveArms);
        }

        static void DrawItemIcon(Rect r, DuelItem item, Color tint)
        {
            void Part(float x, float y, float w, float h) =>
                Chip(new Rect(r.x + r.width * x, r.y + r.height * y, r.width * w, r.height * h), tint);

            switch (item)
            {
                case DuelItem.RepairArmor:
                    Part(0.35f, 0f, 0.3f, 1f);
                    Part(0f, 0.35f, 1f, 0.3f);
                    break;
                case DuelItem.Ammo:
                    for (int i = 0; i < 3; i++) Part(0.32f * i, 0f, 0.22f, 1f);
                    break;
                case DuelItem.Grenade:
                    Part(0.37f, 0.04f, 0.36f, 0.22f);
                    Part(0.20f, 0.28f, 0.60f, 0.65f);
                    break;
                case DuelItem.MagneticAnchor:
                    Part(0.14f, 0.2f, 0.2f, 0.62f);
                    Part(0.66f, 0.2f, 0.2f, 0.62f);
                    Part(0.14f, 0.68f, 0.72f, 0.20f);
                    break;
                case DuelItem.DecoyBeacon:
                    Part(0.40f, 0.10f, 0.20f, 0.20f);
                    Part(0.10f, 0.16f, 0.13f, 0.27f);
                    Part(0.77f, 0.16f, 0.13f, 0.27f);
                    Part(0.43f, 0.40f, 0.14f, 0.50f);
                    Part(0.23f, 0.79f, 0.54f, 0.14f);
                    break;
            }
        }
    }
}
