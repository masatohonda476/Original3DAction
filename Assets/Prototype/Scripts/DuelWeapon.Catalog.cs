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

        static DuelWeapon Gun(string name, int rarity, string heavyName, float weight, Vector3 scale,
            int mag, float ammoCost, float enCost, float frame, DuelShot normal, DuelShot special, int specialAmmo, string feel) => new()
        {
            name = name, Slot = DuelSlot.Ranged, rarity = rarity, HeavyName = heavyName, feel = feel, weight = weight, scale = scale,
            magazine = mag, ammoCost = ammoCost, enCost = enCost, aimFrame = frame, Primary = normal, Special = special, SpecialAmmo = specialAmmo,
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

        // 仮。射撃の威力・射程・間隔・弾倉・共通の弾の消費。
        public static readonly DuelWeapon BaseGun = Gun("ハンドガン", 0, "三方向射撃", 3f, new Vector3(0.4f, 0.7f, 2.6f),
            8, 1f, 0f, 14f,
            new DuelShot
            {
                damage = 6f, range = 65f, interval = 0.32f, stagger = 1.5f, knock = 3f, shotSpeed = 250f,
                recoilPerShot = 0.8f, recoilMax = 3f, recoilRecover = 10f, aimKick = 1.2f, aimKickSide = 0.4f,
            },
            new DuelShot { damage = 10f, range = 50f, interval = 0.85f, stagger = 2f, knock = 4f, shotSpeed = 250f, spread = 7f, pellets = 3 },
            5, "まっすぐ撃つ銃。大技は中央と左右に一発ずつ撃つ");

        static readonly DuelWeapon[] DropCatalog =
        {
            // 仮。近接の通常威力倍率。違いは主に強攻撃へ。
            Blade("ブロードソード", 0, "振り下ろし", "上から強く振り下ろす", 1.05f, 4.5f, DuelMoveSet.CommonHeavy),
            Blade("電磁ブレード", 1, "吹き飛ばし斬り", "強攻撃で敵を押し飛ばす", 1.08f, 4f, DuelMoveSet.KnockbackHeavy, DuelHeavyEffect.Knockback),
            Blade("重斬刀", 1, "踏ん張り斬り", "強攻撃の出始めは傷を抑えて怯まない", 1.10f, 6f, DuelMoveSet.GuardPointHeavy, DuelHeavyEffect.GuardPoint),
            Blade("光波刀", 2, "光波斬り", "強攻撃で光波を飛ばす", 1.14f, 5f, DuelMoveSet.WaveHeavy, DuelHeavyEffect.Wave),

            // 仮。実弾銃の大技は共通の弾、ビームの通常射撃は EN、大技は共通の弾を使う。
            BaseGun,
            Gun("ライフル", 1, "貫通弾", 6f, new Vector3(0.45f, 0.8f, 4.6f),
                7, 1f, 0f, 10f,
                new DuelShot
                {
                    damage = 8f, range = 90f, interval = 0.36f, stagger = 2f, knock = 4f, shotSpeed = 420f,
                    recoilPerShot = 1.5f, recoilMax = 4f, recoilRecover = 9f, aimKick = 3f, aimKickSide = 0.6f,
                },
                new DuelShot { damage = 28f, range = 100f, interval = 1.15f, stagger = 10f, knock = 10f, shotSpeed = 460f, pierce = true, beamWidth = 0.4f },
                8, "射程の長い銃。大技は敵を貫く"),
            Gun("ビームライフル", 2, "極太ビーム", 7f, new Vector3(0.5f, 0.9f, 5f),
                0, 0f, 12f, 10f,
                new DuelShot
                {
                    damage = 10f, range = 100f, interval = 0.38f, stagger = 2.5f, knock = 5f, shotSpeed = 320f,
                    recoilPerShot = 1.2f, recoilMax = 3.5f, recoilRecover = 9f, aimKick = 2.5f, aimKickSide = 0.5f,
                },
                new DuelShot { damage = 54f, range = 120f, interval = 1.65f, stagger = 22f, knock = 18f, pierce = true, beamWidth = 3f },
                15, "通常射撃には EN を使う。大技は共通の弾で極太の光線"),

            Back("背部スラスター", 0, "速さと燃費のバランス型", 1.05f, 1.15f, 1.1f, 0.85f, new Vector3(2.4f, 2.8f, 1.3f), 5f),
            Back("省エネスラスター", 0, "速さは控えめだが、長く飛び続けられる", 1f, 1.05f, 1f, 0.6f, new Vector3(2f, 2.4f, 1.1f), 5f),
            Back("軽量スラスター", 1, "軽く速い。回避が伸びる", 1.15f, 1.3f, 1.2f, 0.75f, new Vector3(1.6f, 2f, 0.8f), 2f),
            Back("高速スラスター", 2, "とても速く、燃費も良い", 1.25f, 1.6f, 1.35f, 0.6f, new Vector3(2.8f, 3.2f, 1.6f), 4.5f),
        };

        public static List<DuelWeapon> Defaults() => new(DropCatalog);
    }
}
