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

        void DrawPickup(DuelArms arms)
        {
            var loot = arms.Breaking ?? arms.Choosing ?? arms.Nearby;
            var w = loot != null ? loot.Weapon : null;
            if (w == null || player.Dead) return;

            float width = 700f;
            float x = (SW - width) * 0.5f, y = ArmsTop - 142f;
            var card = new Rect(x, y, width, 130f);
            MenuPanel(card);
            Chip(new Rect(x, y, 3f, card.height), w.Color);
            Rect Row(float offset) => new(x + 14f, y + offset, width - 28f, 23f);
            GUI.color = w.Color;
            GUI.Label(Row(6f), $"{DuelWeapon.SlotName(w.Slot)}　{WeaponName(w)}", viewHeadStyle);
            GUI.color = Color.white;

            string cancel = ProtoInput.L(ProtoInput.Btn.Back);
            string equip = ProtoInput.L(ProtoInput.Btn.EquipNow);
            string salvage = ProtoInput.L(ProtoInput.Btn.Break);
            if (arms.Breaking != null)
            {
                float room = Mathf.Max(0f, arms.Rules.ammoMax - arms.Ammo);
                float magAmmo = arms.MagAmmo(loot);
                float refund = Mathf.Min(room, magAmmo);
                float ammo = Mathf.Min(room, arms.SalvageAmmo(w) + magAmmo);
                float hp = Mathf.Max(0f, Mathf.Min(player.MaxHP - player.HP, arms.SalvageHeal(w)));
                string left = ProtoInput.L(ProtoInput.Btn.BreakAmmo);
                string right = ProtoInput.L(ProtoInput.Btn.BreakHeal);
                string ammoText = ammo > 0.001f ? $"共通の弾 +{ammo:0.#}" : "弾の補給なし";
                string repairText = hp > 0.001f ? $"回復 +{hp:0.#}" : "回復なし";
                if (refund > 0.001f) repairText += $"・弾倉から弾 +{refund:0.#}";
                GUI.Label(Row(37f), "解体する", small);
                GUI.Label(Row(65f), $"{left} {ammoText}　　{right} {repairText}", small);
                GUI.Label(Row(101f), $"{salvage} / {cancel} 取り消す", small);
                return;
            }

            var current = arms.Get(w.Slot);
            string currentTechnique = current is { IsGun: true } ? $"　{current.GunTypeName} / {current.HeavyName}" : "";
            GUI.Label(Row(36f), $"装備中　{WeaponName(current)}{currentTechnique}", small);
            GUI.Label(Row(59f), WeaponStats(w, current), cellNameSmall);
            GUI.Label(Row(79f), WeaponTechnique(w), cellNameSmall);
            GUI.Label(Row(101f), arms.Choosing != null
                ? $"{equip} {DuelWeapon.SlotName(w.Slot)}を入れ替える　　{cancel} 取り消す"
                : $"{equip} 入れ替えの確認　　{salvage} 解体する", small);
            if (arms.Choosing != null) DrawPickupChart(w, current, y);
        }

        void DrawPickupChart(DuelWeapon weapon, DuelWeapon current, float cardY)
        {
            const float width = 508f, height = 194f, margin = 16f, gap = 8f;
            // 比較は機体を隠さないよう右寄せ。背中の比較は従来どおり画面上部の中央。
            bool back = weapon.Slot == DuelSlot.Back;
            float x = back ? (SW - width) * 0.5f : SW - margin - width;
            float y = back ? 82f : cardY - gap - height;
            MenuPanel(new Rect(x, y, width, height));

            var pair = DuelRadar.Get(weapon, current);
            Chip(new Rect(x + 15f, y + 11f, 12f, 3f), DuelRadar.MainColor);
            GUI.Label(new Rect(x + 32f, y + 3f, 74f, 23f), "落とし物", cellNameSmall);
            if (pair.Compared)
            {
                Chip(new Rect(x + 110f, y + 11f, 12f, 3f), DuelRadar.OtherColor);
                GUI.Label(new Rect(x + 127f, y + 3f, 72f, 23f), "装備中", cellNameSmall);
                GUI.Label(new Rect(x + 206f, y + 3f, width - 220f, 23f), "数値は装備中 → 落とし物", cellTinyRight);
            }
            DrawWeaponChart(new Rect(x + 10f, y + 27f, width - 20f, height - 31f), pair);
        }
    }
}
