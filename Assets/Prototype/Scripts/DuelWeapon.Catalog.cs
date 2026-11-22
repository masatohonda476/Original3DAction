using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    public partial class DuelWeapon
    {
        static DuelWeapon Blade(string name, int rarity, string heavyName, string feel, float power, float weight,
            DuelSwing heavy, DuelHeavyEffect effect = DuelHeavyEffect.None) => new()
        {
            name = name, Slot = DuelSlot.Melee, rarity = rarity, HeavyName = heavyName, feel = feel,
            MeleePower = power, weight = weight, HeavyMove = heavy, HeavyEffect = effect,
        };

        static DuelWeapon Gun(string name, string typeName, int rarity, string heavyName, float weight, Vector3 scale,
            int mag, float ammoCost, float enCost, float frame, DuelShot normal, DuelShot special, int specialAmmo, string feel) => new()
        {
            name = name, GunTypeName = typeName, Slot = DuelSlot.Ranged, rarity = rarity, HeavyName = heavyName, feel = feel,
            weight = weight, scale = scale, magazine = mag, ammoCost = ammoCost, enCost = enCost, aimFrame = frame,
            Primary = normal, Special = special, SpecialAmmo = specialAmmo,
        };

        static DuelWeapon Back(string name, int rarity, string feel, float run, float dash, float dodge,
            float en, Vector3 scale, float weight) => new()
        {
            name = name, Slot = DuelSlot.Back, rarity = rarity, feel = feel, runMul = run, dashMul = dash,
            dodgeMul = dodge, enCostMul = en, scale = scale, weight = weight,
        };

        // 仮。技の時間・EN・崩しは DuelMoveSet の強攻撃の設定。通常の振り方と間合いは全武器共通。
        public static readonly DuelWeapon BaseSword = Blade("素の剣", 0, "振り下ろし", "標準の近接武器", 1f, 4f, DuelMoveSet.CommonHeavy);
        public static readonly DuelWeapon BaseThruster = Back("標準スラスター", 0, "標準の機動力", 1f, 1f, 1f, 1f, new Vector3(1.8f, 2.3f, 0.9f), 3f);

        // 仮。初期銃の通常射撃は中距離向けの単射。
        public static readonly DuelWeapon BaseGun = Gun("標準ライフル", "ライフル", 0, "貫通弾", 4.5f, new Vector3(0.45f, 0.8f, 3.8f),
            10, 1f, 0f, 14f,
            new DuelShot
            {
                damage = 8f, range = 90f, interval = 0.32f, stagger = 1.8f, knock = 3f, shotSpeed = 360f, spread = 0.35f,
                recoilPerShot = 0.8f, recoilMax = 3f, recoilRecover = 10f, aimKick = 1.2f, aimKickSide = 0.4f,
            },
            new DuelShot { damage = 24f, range = 100f, interval = 0.9f, stagger = 8f, knock = 8f, shotSpeed = 450f, pierce = true, beamWidth = 0.35f },
            5, "中距離で扱いやすい単射。大技は射線上の敵を貫く");

        static readonly DuelWeapon[] DropCatalog =
        {
            // 仮。近接の通常威力倍率。違いは主に強攻撃へ。
            Blade("ブロードソード", 0, "振り下ろし", "上から強く振り下ろす", 1.05f, 4.5f, DuelMoveSet.CommonHeavy),
            Blade("電磁ブレード", 1, "吹き飛ばし斬り", "強攻撃で敵を押し飛ばす", 1.08f, 4f, DuelMoveSet.KnockbackHeavy, DuelHeavyEffect.Knockback),
            Blade("重斬刀", 1, "踏ん張り斬り", "強攻撃の出始めは傷を抑えて怯まない", 1.10f, 6f, DuelMoveSet.GuardPointHeavy, DuelHeavyEffect.GuardPoint),
            Blade("光波刀", 2, "光波斬り", "強攻撃で光波を飛ばす", 1.14f, 5f, DuelMoveSet.WaveHeavy, DuelHeavyEffect.Wave),

            // 仮。通常射撃の性能・大技の性能・大技の共通弾の消費。
            BaseGun,
            Gun("三連ライフル", "バーストライフル", 0, "高速連射", 5.5f, new Vector3(0.5f, 0.8f, 4.2f),
                12, 1f, 0f, 12f,
                new DuelShot
                {
                    damage = 4.5f, range = 78f, interval = 0.5f, stagger = 1f, knock = 2f, shotSpeed = 335f,
                    burstCount = 3, burstInterval = 0.075f, spread = 0.65f,
                    recoilPerShot = 0.35f, recoilMax = 3f, recoilRecover = 9f, aimKick = 0.6f, aimKickSide = 0.25f,
                },
                new DuelShot
                {
                    damage = 6f, range = 86f, interval = 1.05f, stagger = 1.5f, knock = 2f, shotSpeed = 385f,
                    burstCount = 6, burstInterval = 0.055f, spread = 0.8f,
                },
                7, "一押しで 3 点射。大技はさらに短い間隔で 6 連射"),
            Gun("機関銃", "マシンガン", 0, "貫通弾", 5f, new Vector3(0.6f, 0.9f, 3.4f),
                22, 1f, 0f, 16f,
                new DuelShot
                {
                    damage = 2.5f, range = 55f, interval = 0.095f, stagger = 0.65f, knock = 1.5f, shotSpeed = 240f,
                    autoFire = true, spread = 1.3f, recoilPerShot = 0.22f, recoilMax = 4.8f, recoilRecover = 11f, aimKick = 0.25f, aimKickSide = 0.2f,
                },
                new DuelShot { damage = 20f, range = 75f, interval = 0.82f, stagger = 6f, knock = 6f, shotSpeed = 325f, pierce = true, beamWidth = 0.5f },
                5, "押し続けて近距離に連射。大技は敵を貫く一発"),
            Gun("長距離狙撃銃", "スナイパーライフル", 1, "重榴弾", 7f, new Vector3(0.55f, 0.85f, 5.8f),
                5, 1f, 0f, 9f,
                new DuelShot
                {
                    damage = 21f, range = 145f, interval = 0.8f, stagger = 4f, knock = 6f, shotSpeed = 620f, spread = 0.12f,
                    recoilPerShot = 2.8f, recoilMax = 5f, recoilRecover = 10f, aimKick = 4f, aimKickSide = 0.45f,
                },
                new DuelShot { damage = 32f, range = 110f, interval = 1.45f, stagger = 11f, knock = 13f, shotSpeed = 180f, blastRadius = 8f },
                10, "速く正確な遠距離単射。大技の榴弾は着弾時に爆発する"),
            Gun("重バズーカ", "バズーカ", 1, "空中炸裂グレネード", 9f, new Vector3(0.9f, 1.15f, 4.5f),
                4, 2f, 0f, 12f,
                new DuelShot
                {
                    damage = 16f, range = 68f, interval = 0.86f, stagger = 4f, knock = 8f, shotSpeed = 105f, blastRadius = 5f,
                    recoilPerShot = 2f, recoilMax = 6f, recoilRecover = 8f, aimKick = 4f, aimKickSide = 0.4f,
                },
                new DuelShot
                {
                    damage = 26f, range = 45f, interval = 1.4f, stagger = 9f, knock = 12f, shotSpeed = 65f,
                    blastRadius = 11f, blastAtEnd = true,
                },
                10, "通常弾も着弾時に爆発。大技は着弾か一定距離で起爆する"),
            Gun("ビームライフル", "ビーム", 2, "近接信管弾", 7f, new Vector3(0.5f, 0.9f, 5f),
                0, 0f, 12f, 10f,
                new DuelShot
                {
                    damage = 11f, range = 108f, interval = 0.38f, stagger = 2.5f, knock = 5f,
                    recoilPerShot = 1.1f, recoilMax = 3.2f, recoilRecover = 9f, aimKick = 1.8f, aimKickSide = 0.3f,
                },
                new DuelShot
                {
                    damage = 39f, range = 112f, interval = 1.65f, stagger = 15f, knock = 16f, shotSpeed = 200f,
                    blastRadius = 12f, proximityRadius = 5f,
                },
                15, "通常は EN で即着のビーム。大技は敵のそばを通るだけで起爆する"),
            Gun("拡散散弾銃", "ショットガン", 2, "拡散爆裂弾", 7.5f, new Vector3(0.75f, 0.95f, 3.9f),
                6, 1f, 0f, 16f,
                new DuelShot
                {
                    damage = 3.4f, range = 34f, interval = 0.6f, stagger = 0.85f, knock = 2.2f, shotSpeed = 165f,
                    pellets = 7, spread = 8f, recoilPerShot = 1.6f, recoilMax = 4f, recoilRecover = 9f, aimKick = 3.5f, aimKickSide = 0.55f,
                },
                new DuelShot
                {
                    damage = 16f, range = 62f, interval = 1.8f, stagger = 4.5f, knock = 10f, shotSpeed = 145f,
                    pellets = 5, spread = 19f, blastRadius = 8f, blastAtEnd = true,
                },
                17, "通常は近距離の 7 粒散弾。大技は広げた 5 発がそれぞれ爆発する"),

            Back("背部スラスター", 0, "速さと燃費のバランス型", 1.05f, 1.15f, 1.1f, 0.85f, new Vector3(2.4f, 2.8f, 1.3f), 5f),
            Back("省エネスラスター", 0, "速さは控えめだが、長く飛び続けられる", 1f, 1.05f, 1f, 0.6f, new Vector3(2f, 2.4f, 1.1f), 5f),
            Back("軽量スラスター", 1, "軽く速い。回避が伸びる", 1.15f, 1.3f, 1.2f, 0.75f, new Vector3(1.6f, 2f, 0.8f), 2f),
            Back("高速スラスター", 2, "とても速く、燃費も良い", 1.25f, 1.6f, 1.35f, 0.6f, new Vector3(2.8f, 3.2f, 1.6f), 4.5f),
        };

        public static List<DuelWeapon> Defaults() => new(DropCatalog);
    }
}
