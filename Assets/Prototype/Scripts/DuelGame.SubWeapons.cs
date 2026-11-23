using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelGame
    {
        const int SubSlotCount = 4;

        void DrawSubAim(DuelArms arms, Camera cam)
        {
            if (Event.current.type != EventType.Repaint || cam == null || !arms.SubAiming) return;
            bool showCorners = arms.SubSight(cam, out float halfW, out float halfH);
            DrawCross(SW * 0.5f, SH * 0.5f, CrossMinGap, showCorners ? 0.7f : 0.95f, outline: !showCorners);
            if (!showCorners) return;
            bool hasTarget = false;
            for (int i = 0; i < SubSlotCount; i++)
            {
                if (arms.SubTargetAt(i) != null)
                {
                    hasTarget = true;
                    break;
                }
            }

            float k = SH * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            DrawWeaponSightCorners(halfW * k, halfH * k, hasTarget, false);

            for (int i = 0; i < SubSlotCount; i++)
            {
                var target = arms.SubTargetAt(i);
                if (target == null) continue;
                bool alreadyMarked = false;
                for (int j = 0; j < i; j++)
                {
                    if (arms.SubTargetAt(j) != target) continue;
                    alreadyMarked = true;
                    break;
                }
                if (!alreadyMarked) DrawAimTargetMark(cam, target);
            }
        }

        static bool SubTracksEnemy(DuelArms arms, DuelEnemy enemy)
        {
            if (!arms.SubAiming || enemy == null) return false;
            for (int i = 0; i < SubSlotCount; i++)
                if (arms.SubTargetAt(i) == enemy) return true;
            return false;
        }

        void DrawSubWeaponGroup(DuelArms arms, Rect r, int first)
        {
            bool shoulder = first == 0;
            var input = shoulder ? ProtoInput.Btn.R2 : ProtoInput.Btn.R1;
            Chip(r, new Color(0.04f, 0.13f, 0.19f, 0.85f));
            Chip(new Rect(r.x, r.y, r.width, 2f), ActiveArms);
            GUI.color = ActiveArms;
            GUI.Label(new Rect(r.x + 8f, r.y + 3f, r.width - 16f, 22f),
                $"{ProtoInput.L(input)}　{(shoulder ? "両肩" : "両脚")}", cellNameSmall);
            GUI.color = Color.white;

            for (int side = 0; side < 2; side++)
            {
                int slot = first + side;
                float y = r.y + 27f + side * 24f;
                var weapon = arms.SubWeaponAt(slot);
                GUI.color = weapon != null ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                GUI.Label(new Rect(r.x + 8f, y, 24f, 22f), side == 0 ? "左" : "右", cellNameSmall);
                if (weapon != null)
                    Chip(new Rect(r.x + 32f, y + 7f, 8f, 8f), weapon.Color);
                GUI.Label(new Rect(r.x + 44f, y, r.width - 92f, 22f), weapon?.name ?? "空き", cellNameSmall);
                if (weapon != null)
                {
                    GUI.color = AmmoGauge;
                    GUI.Label(new Rect(r.xMax - 49f, y + 1f, 41f, 22f), $"残 {arms.SubUsesAt(slot)}", cellTinyRight);
                }
                GUI.color = Color.white;
            }
        }

        void DrawSubPickup(DuelArms arms, DuelLoot loot)
        {
            const float height = 204f;
            float width = Mathf.Min(590f, SW - 24f);
            var card = new Rect((SW - width) * 0.5f, ArmsTop - height - 12f, width, height);
            var weapon = loot.Weapon;
            bool shoulder = weapon.Slot == DuelSlot.Shoulder;
            MenuPanel(card);
            Chip(new Rect(card.x, card.y, 3f, card.height), weapon.Color);
            Rect Line(float y, float h = 24f) => new(card.x + 14f, card.y + y, card.width - 28f, h);
            GUI.color = weapon.Color;
            GUI.Label(Line(8f, 28f), $"サブ武装　{WeaponName(weapon)}", viewHeadStyle);
            GUI.color = Color.white;
            GUI.Label(Line(41f), $"{DuelWeapon.SlotName(weapon.Slot)}　残り {loot.SubUses}回　重量 {weapon.weight:0.#}", small);
            GUI.Label(Line(66f), weapon.feel, cellNameSmall);

            int first = DuelArms.SubSlotStart(weapon);
            for (int side = 0; side < 2; side++)
            {
                int slot = first + side;
                var fitted = arms.SubWeaponAt(slot);
                var row = Line(95f + side * 34f, 30f);
                if (arms.SubChoiceIndex == side) SelectionBand(row);
                string sideName = side == 0 ? "左" : "右";
                string kind = shoulder ? "肩" : "脚";
                string current = fitted != null
                    ? $"{fitted.name}（残 {arms.SubUsesAt(slot)}回）と交換"
                    : "空き枠に装着";
                float weight = arms.SubWeightAfterEquip(weapon, side);
                bool fits = weight <= arms.Rules.carryMax;
                GUI.color = fits ? Color.white : ResourceLow;
                GUI.Label(new Rect(row.x + 8f, row.y + 3f, row.width - 162f, 24f),
                    $"{sideName}{kind}　{current}", small);
                GUI.Label(new Rect(row.xMax - 151f, row.y + 5f, 143f, 23f),
                    fits ? $"総重量 {weight:0.#} / {arms.Rules.carryMax:0.#}" : "重量上限を超えます", cellTinyRight);
                GUI.color = Color.white;
            }

            string confirm = ProtoInput.L(ProtoInput.Btn.Confirm), back = ProtoInput.L(ProtoInput.Btn.Back);
            GUI.Label(Line(169f), $"↑↓ 取付先を選ぶ　　{confirm} 装着　　{back} 戻る", center);
        }
    }
}
