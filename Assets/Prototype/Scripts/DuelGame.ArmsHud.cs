using HuntProto;
using UnityEngine;

namespace Prototype
{
    // 武装の構え・弾と、落ちている武装の比較・入れ替え・解体の案内。
    public partial class DuelGame
    {
        const float CellW = 232f, CellH = 84f, CellGap = 8f, ArmsMargin = 20f, AmmoGaugeW = 24f;
        // ロックオンの枠が武装欄に重ならないよう、上端の位置は従来の武装欄とそろえる。
        float ArmsTop => SH - 166f;
        float ArmsLeft => SW - ArmsMargin - CellW * 2f - CellGap * 2f - AmmoGaugeW;

        static readonly Color ActiveArms = new(0.62f, 0.86f, 1f);
        static readonly Color AmmoGauge = new(1f, 0.75f, 0.35f);
        static readonly Color ResourceLow = new(1f, 0.55f, 0.45f);

        void DrawArms(DuelArms arms)
        {
            float y = ArmsTop, x = ArmsLeft + AmmoGaugeW + CellGap;
            float width = CellW * 2f + CellGap;
            DrawWeaponCell(arms, DuelSlot.Ranged, new Rect(x, y, CellW, CellH));
            DrawWeaponCell(arms, DuelSlot.Melee, new Rect(x + CellW + CellGap, y, CellW, CellH));

            var back = arms.Get(DuelSlot.Back);
            var backRect = new Rect(x, y + CellH + CellGap, width, 25f);
            Chip(backRect, new Color(0f, 0f, 0f, 0.5f));
            if (back != null) Chip(new Rect(x + 8f, backRect.y + 8f, 9f, 9f), back.Color);
            GUI.Label(new Rect(x + 22f, backRect.y + 1f, width - 28f, 24f), $"背中　{back?.name ?? "なし"}", cellNameSmall);

            var gauge = new Rect(ArmsLeft, y, AmmoGaugeW, backRect.yMax - y);
            float max = arms.Rules.ammoMax;
            bool low = max > 0f && arms.Ammo <= max * 0.2f;
            bool denied = Time.time - arms.NoAmmoAt < 1f;
            var ammoColor = denied ? ResourceLow : low ? AmmoGauge : Color.white;
            Chip(gauge, denied ? new Color(0.6f, 0.1f, 0.08f, 0.6f) : new Color(0f, 0f, 0f, 0.5f));
            float fill = max > 0f ? Mathf.Clamp01(arms.Ammo / max) : 0f;
            if (fill > 0f)
            {
                float height = Mathf.Max(2f, gauge.height * fill);
                Chip(new Rect(gauge.x, gauge.yMax - height, gauge.width, height), ammoColor == Color.white ? AmmoGauge : ammoColor);
            }
            for (int i = 1; i < 4; i++)
                Chip(new Rect(gauge.x, gauge.yMax - gauge.height * i * 0.25f, gauge.width, 1f), new Color(0f, 0f, 0f, 0.55f));

            GUI.color = ammoColor;
            GUI.Label(new Rect(x + width - 187f, backRect.y + 1f, 180f, 22f), $"共通の弾  {arms.Ammo:0.#}/{max:0}", cellTinyRight);
            GUI.color = Color.white;
        }

        void DrawWeaponCell(DuelArms arms, DuelSlot slot, Rect r)
        {
            var w = arms.Get(slot);
            bool ranged = slot == DuelSlot.Ranged;
            bool active = !player.Dead && arms.Aiming == ranged;
            Chip(r, active ? new Color(0.04f, 0.13f, 0.19f, 0.85f) : new Color(0f, 0f, 0f, 0.55f));
            if (active) Chip(new Rect(r.x, r.y, r.width, 2f), ActiveArms);

            string mode = active ? "構え中" : ranged ? $"{ProtoInput.L(ProtoInput.Btn.L2)} 長押し" : "";
            GUI.color = active ? ActiveArms : Color.white;
            GUI.Label(new Rect(r.x + 8f, r.y + 3f, 110f, 20f), DuelWeapon.SlotName(slot) + (mode.Length > 0 ? $"　{mode}" : ""), cellTiny);
            GUI.color = Color.white;
            if (w == null)
            {
                GUI.Label(new Rect(r.x + 8f, r.y + 19f, r.width - 16f, 25f), "なし", cellNameStyle);
                return;
            }

            Chip(new Rect(r.x + 8f, r.y + 27f, 10f, 10f), w.Color);
            GUI.Label(new Rect(r.x + 22f, r.y + 19f, r.width - 30f, 25f), w.name, cellNameStyle);
            string attack = ProtoInput.L(ProtoInput.Btn.R1), heavy = ProtoInput.L(ProtoInput.Btn.R2);
            if (!ranged)
            {
                GUI.Label(new Rect(r.x + 8f, r.y + 42f, r.width - 16f, 20f), $"{attack} 通常攻撃   {heavy} 強攻撃", cellTiny);
                if (w.UsesTwoHands)
                    GUI.Label(new Rect(r.x + 8f, r.y + 59f, r.width - 16f, 20f), $"両手持ち　{w.HeavyName}", cellTiny);
                return;
            }

            string fireButton = w.Primary.autoFire ? $"{attack} 長押し" : attack;
            GUI.Label(new Rect(r.x + 8f, r.y + 42f, r.width - 16f, 20f), $"{fireButton}　{w.GunTypeName}", cellTiny);
            GUI.Label(new Rect(r.x + 8f, r.y + 59f, r.width - 16f, 20f), $"{heavy}　{w.HeavyName}（弾 {w.SpecialAmmo}）", cellTiny);
            float reload = arms.ReloadProgress;
            bool loading = reload > 0f;
            bool cooling = arms.Aiming && arms.Cooling > 0f;
            string status = w.HasMagazine ? $"弾倉 {arms.Magazine}/{w.magazine}" : "通常射撃は EN";
            if (loading) status += "　装填中";
            else if (cooling) status += "　次弾待ち";
            GUI.color = loading || cooling ? AmmoGauge : w.HasMagazine && arms.Magazine == 0 ? ResourceLow : Color.white;
            GUI.Label(new Rect(r.x + 108f, r.y + 3f, r.width - 116f, 20f), status, cellTinyRight);
            GUI.color = Color.white;
            if (loading || cooling)
                Bar(new Rect(r.x + 2f, r.yMax - 4f, r.width - 4f, 2f), loading ? reload : 1f - arms.Cooling, AmmoGauge);
        }

        static void Chip(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        const float ItemActionsHeight = 260f;

        void DrawPickup(DuelArms arms)
        {
            var loot = arms.Choosing ?? arms.Nearby;
            if (loot == null || player.Dead) return;
            var weapon = loot.Weapon;
            if (!arms.ChoiceOpen)
            {
                string hint = $"{WeaponName(weapon)}　　{ProtoInput.L(ProtoInput.Btn.PickUp)} 調べる";
                float hintWidth = Mathf.Min(SW - 24f, center.CalcSize(new GUIContent(hint)).x + 40f);
                var hintRect = new Rect((SW - hintWidth) * 0.5f, ArmsTop - 50f, hintWidth, 34f);
                MenuPanel(hintRect);
                Chip(new Rect(hintRect.x, hintRect.y, 3f, hintRect.height), weapon.Color);
                GUI.Label(hintRect, hint, center);
                return;
            }

            float width = Mathf.Min(650f, SW - 24f);
            var card = new Rect((SW - width) * 0.5f, ArmsTop - ItemActionsHeight - 12f, width, ItemActionsHeight);
            DrawPickupChart(weapon, arms.Get(weapon.Slot), card.y);
            DrawItemActions(card, arms, new DuelArms.Carried(weapon, loot.Memo), false);
        }

        void DrawItemActions(Rect card, DuelArms arms, DuelArms.Carried item, bool fromCarried)
        {
            var weapon = item.Weapon;
            var current = arms.Get(weapon.Slot);
            MenuPanel(card);
            Chip(new Rect(card.x, card.y, 3f, card.height), weapon.Color);
            Rect Row(float offset, float height = 23f) => new(card.x + 14f, card.y + offset, card.width - 28f, height);
            GUI.color = weapon.Color;
            GUI.Label(Row(6f, 31f), $"{DuelWeapon.SlotName(weapon.Slot)}　{WeaponName(weapon)}", viewHeadStyle);
            GUI.color = Color.white;
            GUI.Label(Row(36f), $"装備中　{WeaponName(current)}", small);
            GUI.Label(Row(56f), WeaponStats(weapon, current), cellNameSmall);
            GUI.Label(Row(76f), WeaponTechnique(weapon), cellNameSmall);

            string confirm = ProtoInput.L(ProtoInput.Btn.Confirm);
            string back = ProtoInput.L(ProtoInput.Btn.Back);
            float equipWeight = fromCarried ? arms.TotalWeight : arms.WeightAfterEquip(weapon);
            float keepWeight = fromCarried ? arms.TotalWeight - weapon.weight : arms.TotalWeight + weapon.weight;
            string WeightText(float weight) => Mathf.Approximately(arms.TotalWeight, weight)
                ? $"総重量 {weight:0.#} / {arms.Rules.carryMax:0.#}"
                : $"総重量 {arms.TotalWeight:0.#} → {weight:0.#} / {arms.Rules.carryMax:0.#}";

            float ammo = arms.AmmoFrom(item, false);
            float hp = arms.HpFrom(item);
            float refund = arms.AmmoFrom(item, true);
            string heal = refund > 0f ? $"HP +{hp:0.#}　共通弾 +{refund:0.#}" : $"HP +{hp:0.#}";
            DrawAction(DuelArms.WeaponAction.Equip, 103f, "入れ替える", WeightText(equipWeight));
            DrawAction(DuelArms.WeaponAction.KeepOrDrop, 135f,
                fromCarried ? "床に置く" : "所持する",
                fromCarried && !arms.CanChooseAction(item, true, DuelArms.WeaponAction.KeepOrDrop)
                    ? "地上で置けます" : WeightText(keepWeight));
            DrawAction(DuelArms.WeaponAction.Ammo, 167f, "解体して弾にする", $"共通弾 +{ammo:0.#}");
            DrawAction(DuelArms.WeaponAction.Heal, 199f, "解体して回復する", heal);
            GUI.Label(Row(232f), $"↑↓ 選ぶ　　{confirm} 決定　　解体は {confirm} 長押し　　{back} 戻る", center);

            void DrawAction(DuelArms.WeaponAction action, float y, string label, string detail)
            {
                Rect line = Row(y, 28f);
                bool selected = arms.SelectedAction == action;
                bool available = arms.CanChooseAction(item, fromCarried, action);
                if (selected) SelectionBand(line);
                if (selected && arms.SalvageProgress > 0f)
                    Chip(new Rect(line.x, line.yMax - 3f, line.width * arms.SalvageProgress, 3f), ActiveArms);
                GUI.color = available ? Color.white : new Color(1f, 1f, 1f, 0.50f);
                GUI.Label(new Rect(line.x + 9f, line.y + 2f, line.width * 0.43f, 23f), label, small);
                GUI.Label(new Rect(line.x + line.width * 0.44f, line.y + 2f, line.width * 0.56f - 9f, 23f), detail, cellTinyRight);
                GUI.color = Color.white;
            }
        }

        void DrawPickupChart(DuelWeapon weapon, DuelWeapon current, float cardY)
        {
            const float width = 508f, height = 194f, margin = 16f, gap = 8f;
            // 比較は機体を隠さないよう右寄せ。背中の比較は従来どおり画面上部の中央。
            bool back = weapon.Slot == DuelSlot.Back;
            float x = back ? (SW - width) * 0.5f : SW - margin - width;
            float y = back ? 82f : Mathf.Max(6f, cardY - gap - height);
            DrawWeaponComparison(new Rect(x, y, width, height), weapon, current, "落とし物");
        }

        void DrawWeaponComparison(Rect panel, DuelWeapon weapon, DuelWeapon current, string itemLabel)
        {
            MenuPanel(panel);
            var pair = DuelRadar.Get(weapon, current);
            Chip(new Rect(panel.x + 15f, panel.y + 11f, 12f, 3f), DuelRadar.MainColor);
            GUI.Label(new Rect(panel.x + 32f, panel.y + 3f, 74f, 23f), itemLabel, cellNameSmall);
            if (pair.Compared)
            {
                Chip(new Rect(panel.x + 110f, panel.y + 11f, 12f, 3f), DuelRadar.OtherColor);
                GUI.Label(new Rect(panel.x + 127f, panel.y + 3f, 72f, 23f), "装備中", cellNameSmall);
                GUI.Label(new Rect(panel.x + 206f, panel.y + 3f, panel.width - 220f, 23f),
                    $"数値は装備中 → {itemLabel}", cellTinyRight);
            }
            DrawWeaponChart(new Rect(panel.x + 10f, panel.y + 27f, panel.width - 20f, panel.height - 31f), pair);
        }
    }
}
