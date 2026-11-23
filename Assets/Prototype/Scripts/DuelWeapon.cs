using System;
using UnityEngine;

namespace Prototype
{
    public enum DuelSlot { Melee, Ranged, Back, Shoulder, Leg }
    public enum DuelHeavyEffect { None, Knockback, GuardPoint, Wave }
    public enum DuelMeleeShape { OneHandSword, TwoHandSword, Spear, Glaive, Hammer }

    /// <summary>銃の一つの撃ち方。照準の枠は通常射撃と大技で共通。</summary>
    public class DuelShot
    {
        public float damage, range, interval, stagger, knock, shotSpeed, spread, beamWidth;
        public float recoilPerShot, recoilMax, recoilRecover;
        /// <summary>一度の発射で同時に飛ぶ弾の数。散弾や拡散爆発に使う。</summary>
        public int pellets = 1;
        /// <summary>一度の入力で時間差を付けて撃つ回数と間隔(秒)。interval は最初の発射から次の入力で撃てるまで。</summary>
        public int burstCount = 1;
        public float burstInterval;
        /// <summary>通常射撃を押し続けていると次の発射を受け付ける。</summary>
        public bool autoFire;
        public bool pierce;
        /// <summary>爆発の半径と、至近弾で起爆する距離(m)。blastAtEnd は射程の端で起爆する弾。</summary>
        public float blastRadius, proximityRadius;
        public bool blastAtEnd;
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
        public DuelMoveSet MeleeMoves { get; private set; }
        public DuelSwing HeavyMove { get; private set; }
        public DuelHeavyEffect HeavyEffect { get; private set; }
        public DuelMeleeShape MeleeShape { get; private set; }
        public float HaftLength { get; private set; }

        [Header("遠距離")]
        public int magazine;
        public float ammoCost, enCost, aimFrame;
        public DuelShot Primary { get; private set; }
        public DuelShot Special { get; private set; }
        public int SpecialAmmo { get; private set; }
        public string GunTypeName { get; private set; }

        public int SubUses { get; private set; }
        public bool IsSub => Slot is DuelSlot.Shoulder or DuelSlot.Leg;
        public bool GuidedSub { get; private set; }

        [Header("背中")]
        public float dashMul = 1f, dodgeDistanceMul = 1f, dodgeInvincibleMul = 1f, enCostMul = 1f;

        [Header("見た目")]
        public Vector3 scale;
        public string HeavyName { get; private set; }

        static readonly string[] RarityNames = { "コモン", "レア", "エピック" };
        static readonly Color[] RarityColors = { new(0.75f, 0.77f, 0.8f), new(0.3f, 0.6f, 1f), new(1f, 0.75f, 0.15f) };
        public string RarityName => RarityNames[Mathf.Clamp(rarity, 0, 2)];
        public Color Color => RarityColors[Mathf.Clamp(rarity, 0, 2)];
        public bool IsBlade => Slot == DuelSlot.Melee;
        public bool UsesTwoHands => MeleeShape != DuelMeleeShape.OneHandSword;
        public bool IsGun => Slot == DuelSlot.Ranged;
        public bool HasMagazine => IsGun && magazine > 0;

        public static string SlotName(DuelSlot slot) => slot switch
        {
            DuelSlot.Melee => "近接",
            DuelSlot.Ranged => "遠距離",
            DuelSlot.Shoulder => "肩専用",
            DuelSlot.Leg => "脚専用",
            _ => "背中",
        };
    }
}
