using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // 一時停止中の装備確認と所持品の管理。
    public partial class DuelGame
    {
        const int VisibleCarriedRows = 6;
        int viewSel, carriedSel;
        readonly Dictionary<(DuelWeapon, DuelWeapon), string> weaponStatsText = new();

        void DrawEquipView(DuelArms arms)
        {
            BuildMenuText();
            const float width = 680f, height = 577f, pad = 18f, rowH = 49f;
            float x = (SW - width) * 0.5f, y = (SH - height) * 0.5f;
            float rowW = width - pad * 2f;
            MenuBackdrop();
            MenuPanel(new Rect(x, y, width, height));
            GUI.Label(new Rect(x, y + 8f, width, 38f), "装備", viewTitleStyle);

            for (int i = 0; i < ArmRows; i++)
            {
                var slot = (DuelSlot)i;
                var w = arms.Get(slot);
                var row = new Rect(x + pad, y + 52f + rowH * i, rowW, rowH);
                if (i == viewSel) SelectionBand(row);
                Chip(new Rect(row.x, row.yMax - 1f, row.width, 1f), new Color(1f, 1f, 1f, 0.12f));
                GUI.Label(new Rect(row.x + 14f, row.y + 10f, 90f, 29f), DuelWeapon.SlotName(slot), viewHeadStyle);
                GUI.color = w?.Color ?? Color.white;
                GUI.Label(new Rect(row.x + 115f, row.y + 10f, row.width - 126f, 29f), WeaponName(w), viewHeadStyle);
                GUI.color = Color.white;
            }

            var selected = arms.Get((DuelSlot)viewSel);
            float detailX = x + pad + 14f;
            Rect Detail(float offset) => new(detailX, y + offset, rowW - 28f, 23f);
            if (selected != null)
            {
                GUI.Label(Detail(215f), WeaponTechnique(selected), small);
                GUI.Label(Detail(240f), WeaponStats(selected), small);
                GUI.Label(Detail(265f), selected.feel, small);
                DrawWeaponChart(new Rect(x + pad, y + 292f, rowW, 176f), DuelRadar.Get(selected));
            }
            GUI.Label(Detail(478f),
                $"共通の弾 {arms.Ammo:0.#}/{arms.Rules.ammoMax:0}　推力 ×{arms.Mobility:0.00}　回避距離（重量込み）×{arms.DodgeDistanceMul:0.000}", small);
            GUI.Label(Detail(502f), CarryWeightText(arms), small);

            bool kb = ProtoInput.Current == ProtoInput.Device.Keyboard;
            string back = ProtoInput.L(ProtoInput.Btn.Back), menu = ProtoInput.L(ProtoInput.Btn.EquipView);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(x, y + 541f, width, 23f), $"一時停止中 ― {(kb ? "↑↓/W・S" : "↑↓")} 選ぶ  {back} 戻る  {menu} 閉じる", center);
            GUI.color = Color.white;
        }

        void UpdateCarriedView(DuelArms arms, int direction)
        {
            if (arms.CarriedChoiceIndex >= 0)
            {
                arms.UpdateCarriedChoice();
                return;
            }
            if (ProtoInput.MenuBack)
            {
                SetMenuPage(MenuPage.Top);
                return;
            }

            int count = arms.CarriedWeapons.Count;
            if (count == 0) return;
            carriedSel = Mathf.Clamp(carriedSel, 0, count - 1);
            if (direction != 0) carriedSel = (carriedSel + direction + count) % count;
            else if (ProtoInput.MenuConfirm) arms.OpenCarriedChoice(carriedSel);
        }

        void DrawCarriedView(DuelArms arms)
        {
            MenuBackdrop();
            float width = Mathf.Min(680f, SW - 24f);
            float x = (SW - width) * 0.5f;
            var items = arms.CarriedWeapons;
            if (arms.CarriedChoiceIndex >= 0 && arms.CarriedChoiceIndex < items.Count)
            {
                var item = items[arms.CarriedChoiceIndex];
                const float chartHeight = 194f;
                float top = (SH - chartHeight - ItemActionsHeight - 8f) * 0.5f;
                var chart = new Rect(x, top, width, chartHeight);
                DrawWeaponComparison(chart, item.Weapon, arms.Get(item.Weapon.Slot), "所持品");
                DrawItemActions(new Rect(x, chart.yMax + 8f, width, ItemActionsHeight), arms, item, true);
                return;
            }

            const float height = 410f, pad = 18f, rowHeight = 44f;
            float y = (SH - height) * 0.5f;
            MenuPanel(new Rect(x, y, width, height));
            GUI.Label(new Rect(x, y + 8f, width, 36f), "所持品", viewTitleStyle);
            GUI.Label(new Rect(x + pad, y + 48f, width - pad * 2f, 24f), CarryWeightText(arms), small);

            if (items.Count == 0)
            {
                GUI.Label(new Rect(x + pad, y + 112f, width - pad * 2f, 32f), "所持品はありません", center);
            }
            else
            {
                int selected = Mathf.Clamp(carriedSel, 0, items.Count - 1);
                int first = Mathf.Clamp(selected - VisibleCarriedRows / 2, 0, Mathf.Max(0, items.Count - VisibleCarriedRows));
                for (int i = first; i < Mathf.Min(first + VisibleCarriedRows, items.Count); i++)
                {
                    var item = items[i];
                    var weapon = item.Weapon;
                    var row = new Rect(x + pad, y + 78f + (i - first) * rowHeight, width - pad * 2f, rowHeight);
                    if (i == selected) SelectionBand(row);
                    GUI.color = weapon.Color;
                    GUI.Label(new Rect(row.x + 10f, row.y + 10f, row.width - 120f, 25f),
                        $"{DuelWeapon.SlotName(weapon.Slot)}　{WeaponName(weapon)}", small);
                    GUI.color = Color.white;
                    string mag = weapon.HasMagazine ? $"　弾 {item.State.mag}/{weapon.magazine}" : "";
                    GUI.Label(new Rect(row.x + row.width - 176f, row.y + 10f, 166f, 25f),
                        $"重量 {weapon.weight:0.#}{mag}", rightSmall);
                }
                GUI.Label(new Rect(x + pad, y + 347f, width - pad * 2f, 23f), $"{selected + 1} / {items.Count}", cellTinyRight);
            }

            string confirm = ProtoInput.L(ProtoInput.Btn.Confirm), back = ProtoInput.L(ProtoInput.Btn.Back);
            string actions = items.Count == 0 ? "" : arms.CanUseCarried ? $"↑↓ 選ぶ　{confirm} 詳細　　" : "行動可能な状態で武装を操作できます　　";
            GUI.Label(new Rect(x + pad, y + 375f, width - pad * 2f, 24f), $"{actions}{back} 戻る", center);
        }

        static string CarryWeightText(DuelArms arms) =>
            $"総重量 {arms.TotalWeight:0.#}/{arms.Rules.carryMax:0.#}（装備 {arms.EquippedWeight:0.#} ＋ 所持品 {arms.CarriedWeight:0.#}）";

        void DrawWeaponChart(Rect area, DuelRadar.Pair pair)
        {
            const float radarWidth = 162f, gap = 18f;
            DuelRadar.DrawRadar(new Rect(area.x, area.y, radarWidth, area.height), uiScale, pair);
            DuelRadar.DrawBars(new Rect(area.x + radarWidth + gap, area.y + 5f, area.width - radarWidth - gap, area.height - 10f),
                pair, 80f, 144f);
        }

        static void SelectionBand(Rect row)
        {
            Chip(row, new Color(0.28f, 0.59f, 0.77f, 0.2f));
            Chip(new Rect(row.x, row.y, 3f, row.height), ActiveArms);
        }

        static string WeaponName(DuelWeapon w) =>
            w != null ? $"{w.name}〈{w.RarityName}〉{(w.UsesTwoHands ? "　両手持ち" : "")}" : "なし";

        static string WeaponTechnique(DuelWeapon w) => w.Slot switch
        {
            DuelSlot.Melee => $"強攻撃　{w.HeavyName}（EN {w.HeavyMove.cost:0.#}）",
            DuelSlot.Ranged => $"通常　{w.GunTypeName}　　大技　{w.HeavyName}（共通の弾 {w.SpecialAmmo}）",
            _ => "回避・ブースト・飛行の性能を変える",
        };

        // 同じ枠の武装を比較するときは、項目ごとに「装備中 → 落とし物」の順に並べる。
        string WeaponStats(DuelWeapon w, DuelWeapon current = null)
        {
            var key = (w, current);
            if (!weaponStatsText.TryGetValue(key, out var text))
            {
                text = BuildWeaponStats(w, current);
                weaponStatsText.Add(key, text);
            }
            return text;
        }

        static string BuildWeaponStats(DuelWeapon w, DuelWeapon current)
        {
            static string Value(float next, float? before, string format) =>
                before.HasValue && !Mathf.Approximately(next, before.Value)
                    ? $"{before.Value.ToString(format)}→{next.ToString(format)}"
                    : next.ToString(format);

            if (w.Slot == DuelSlot.Melee)
                return $"近接威力倍率 {Value(w.MeleePower, current?.MeleePower, "0.00")}　強攻撃の崩し {Value(w.HeavyMove.stagger, current?.HeavyMove.stagger, "0.#")}";
            if (w.Slot == DuelSlot.Ranged)
            {
                string Fuel(DuelWeapon weapon) => weapon.HasMagazine ? $"弾倉 {weapon.magazine} 発" : $"EN {weapon.enCost:0.#}/発";
                string fuel = Fuel(w);
                string previous = current != null ? Fuel(current) : fuel;
                if (previous != fuel) fuel = $"{previous} → {fuel}";
                float? previousDamage = current != null ? DuelRadar.VolleyDamage(current) : null;
                return $"通常最大威力 {Value(DuelRadar.VolleyDamage(w), previousDamage, "0.#")}　射程 {Value(w.Primary.range, current?.Primary.range, "0.#")} m　{fuel}";
            }
            return $"ブースト ×{Value(w.dashMul, current?.dashMul, "0.00")}　" +
                $"回避距離補正 ×{Value(w.dodgeDistanceMul, current?.dodgeDistanceMul, "0.00")}　" +
                $"無敵時間 ×{Value(w.dodgeInvincibleMul, current?.dodgeInvincibleMul, "0.00")}　" +
                $"消費 EN ×{Value(w.enCostMul, current?.enCostMul, "0.00")}";
        }
    }
}
