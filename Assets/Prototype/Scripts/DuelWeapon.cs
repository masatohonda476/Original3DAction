using System;
using UnityEngine;

namespace Prototype
{
    public enum DuelSlot { Melee, Ranged, Back }
    public enum DuelHeavyEffect { None, Knockback, GuardPoint, Wave }

    /// <summary>銃の一つの撃ち方。照準の枠は通常射撃と大技で共通。</summary>
    public class DuelShot
    {
        public float damage, range, interval, stagger, knock, shotSpeed, spread, beamWidth;
        public float recoilPerShot, recoilMax, recoilRecover, aimKick, aimKickSide;
        public int pellets = 1;
        public bool pierce;
    }

    [Serializable]
    public partial class DuelWeapon
    {
        public string name, feel;
        public DuelSlot Slot { get; private set; }
        [Range(0, 2)] public int rarity;
        public float dropWeight = 1f;
        public float weight;

        [Header("近接")]
        public float MeleePower { get; private set; } = 1f;
        public DuelSwing HeavyMove { get; private set; }
        public DuelHeavyEffect HeavyEffect { get; private set; }

        [Header("遠距離")]
        public int magazine;
        public float ammoCost, enCost, aimFrame;
        public DuelShot Primary { get; private set; }
        public DuelShot Special { get; private set; }
        public int SpecialAmmo { get; private set; }

        [Header("背中")]
        public float runMul = 1f, dashMul = 1f, dodgeMul = 1f, enCostMul = 1f;

        [Header("見た目")]
        public Vector3 scale;
        public string HeavyName { get; private set; }

        static readonly string[] RarityNames = { "コモン", "レア", "エピック" };
        static readonly Color[] RarityColors = { new(0.75f, 0.77f, 0.8f), new(0.3f, 0.6f, 1f), new(1f, 0.75f, 0.15f) };
        public string RarityName => RarityNames[Mathf.Clamp(rarity, 0, 2)];
        public Color Color => RarityColors[Mathf.Clamp(rarity, 0, 2)];
        public bool IsBlade => Slot == DuelSlot.Melee;
        public bool IsGun => Slot == DuelSlot.Ranged;
        public bool HasMagazine => IsGun && magazine > 0;
        public float damage => Primary.damage;
        public float range => Primary.range;

        public static string SlotName(DuelSlot slot) => slot switch
        {
            DuelSlot.Melee => "近接",
            DuelSlot.Ranged => "遠距離",
            _ => "背中",
        };
    }
}
