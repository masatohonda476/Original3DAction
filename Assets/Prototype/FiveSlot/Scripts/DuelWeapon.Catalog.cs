using System.Collections.Generic;
using UnityEngine;

namespace PrototypeFiveSlot
{
    // DuelWeapon の初期の一覧(Defaults)と、一覧を書くための作り方(partial。中身の項目と説明は DuelWeapon.cs)
    public partial class DuelWeapon
    {
        // ───────── 初期の一覧(仮置き) ─────────

        static DuelWeapon Sword(string name, int rarity, string feel, DuelMoveSet moves, float dmg, float reach, float stagger, float knock) => new()
        {
            name = name, kind = DuelKind.Sword, rarity = rarity, feel = feel, moves = moves,
            damageMul = dmg, reachMul = reach, staggerMul = stagger, knockMul = knock,
        };

        static DuelWeapon Shield(string name, int rarity, string feel, float guard, float costMul, float chip, float guardMove, float run, float stunMul, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.Shield, rarity = rarity, feel = feel, guardMax = guard, guardCostMul = costMul, guardChip = chip,
            guardMoveMul = guardMove, runMul = run, guardStunMul = stunMul, shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon Back(string name, int rarity, string feel, float run, float dash, float dodge, float en, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.Back, rarity = rarity, feel = feel, runMul = run, dashMul = dash, dodgeMul = dodge, enCostMul = en,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon HandGun(string name, int rarity, string feel, bool hold, float dmg, float interval, float range, float spread, int pellets, int mag, float reload,
                                  float stagger, float knock, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.HandGun, rarity = rarity, feel = feel, hold = hold, damage = dmg, interval = interval, range = range,
            spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, knock = knock,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero,
        };

        static DuelWeapon Gun(string name, int rarity, string feel, bool hold, float dmg, float interval, float range, float spread, int pellets, int mag, float reload, float stagger, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.ShoulderGun, rarity = rarity, feel = feel, hold = hold, damage = dmg, interval = interval, range = range,
            spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, knock = 4f, scale = scale,
        };

        static DuelWeapon Heavy(string name, int rarity, string feel, bool hold, float dmg, int salvo, float radius, float interval, float range, float stagger, float knock, PrimitiveType shape, Vector3 scale) => new()
        {
            name = name, kind = DuelKind.ShoulderHeavy, rarity = rarity, feel = feel, hold = hold, damage = dmg, salvo = salvo, radius = radius,
            interval = interval, range = range, stagger = stagger, knock = knock, shape = shape, scale = scale,
            euler = shape == PrimitiveType.Cylinder ? new Vector3(90f, 0f, 0f) : Vector3.zero,
        };

        /// <summary>両手持ちの出やすさ(同じレア度の中で、片手の武装1つを1としたときの重み。今の一覧が埋もれないように少なめ)。</summary>
        const float TwoHandDropWeight = 0.6f;

        /// <summary>両手の近接(技の表 + 倍率 + 見た目の太さ・柄の長さ)。</summary>
        static DuelWeapon TwoMelee(string name, int rarity, string feel, string altMove, DuelMoveSet moves, float dmg, float reach, float stagger, float knock,
                                   float thick, float haft) => new()
        {
            name = name, kind = DuelKind.TwoHandMelee, rarity = rarity, feel = feel, altMove = altMove, moves = moves,
            damageMul = dmg, reachMul = reach, staggerMul = stagger, knockMul = knock, thickMul = thick, haft = haft, dropWeight = TwoHandDropWeight,
        };

        /// <summary>両手の銃(主の撃ち方は弾)。alt は L1 の撃ち方。</summary>
        static DuelWeapon TwoGun(string name, int rarity, string feel, string mainMove, string altMove, bool hold, float dmg, float interval, float range, float spread,
                                 int pellets, int mag, float reload, float stagger, float knock, Vector3 scale, DuelWeapon alt, bool pierce = false, float beamWidth = 0f) => new()
        {
            name = name, kind = DuelKind.TwoHandGun, rarity = rarity, feel = feel, mainMove = mainMove, altMove = altMove, hold = hold, damage = dmg, interval = interval,
            range = range, spread = spread, pellets = pellets, magazine = mag, reload = reload, stagger = stagger, knock = knock, pierce = pierce, beamWidth = beamWidth,
            shape = PrimitiveType.Cube, scale = scale, euler = Vector3.zero, alt = alt, dropWeight = TwoHandDropWeight,
        };

        /// <summary>両手の砲(主の撃ち方は爆発。salvo が2以上ならミサイル)。alt は L1 の撃ち方。</summary>
        static DuelWeapon TwoCannon(string name, int rarity, string feel, string mainMove, string altMove, bool hold, float dmg, int salvo, float radius, float interval,
                                    float range, float stagger, float knock, PrimitiveType shape, Vector3 scale, DuelWeapon alt) => new()
        {
            name = name, kind = DuelKind.TwoHandGun, blast = true, rarity = rarity, feel = feel, mainMove = mainMove, altMove = altMove, hold = hold, damage = dmg,
            salvo = salvo, radius = radius, interval = interval, range = range, stagger = stagger, knock = knock, shape = shape, scale = scale,
            euler = shape == PrimitiveType.Cylinder ? new Vector3(90f, 0f, 0f) : Vector3.zero, alt = alt, dropWeight = TwoHandDropWeight,
        };

        /// <summary>L1 の撃ち方:弾(charge > 0 なら溜め撃ち)。</summary>
        static DuelWeapon AltShot(float dmg, float interval, float range, float spread, int pellets, float stagger, float knock, float charge = 0f, bool pierce = false,
                                  float beamWidth = 0f) => new()
        {
            name = "L1", kind = DuelKind.HandGun, hold = false, damage = dmg, interval = interval, range = range, spread = spread, pellets = pellets, magazine = 0,
            stagger = stagger, knock = knock, charge = charge, pierce = pierce, beamWidth = beamWidth,
        };

        /// <summary>L1 の撃ち方:爆発(salvo が2以上ならミサイル。charge > 0 なら溜め撃ち)。</summary>
        static DuelWeapon AltBlast(float dmg, int salvo, float radius, float interval, float range, float stagger, float knock, float charge = 0f) => new()
        {
            name = "L1", kind = DuelKind.ShoulderHeavy, hold = false, damage = dmg, salvo = salvo, radius = radius, interval = interval, range = range,
            stagger = stagger, knock = knock, charge = charge,
        };

        /// <summary>
        /// 初期の一覧。素の剣(通常攻撃9・強攻撃24、毎秒16ほど)・敵のHP(兵卒28・剣士47)に合わせた仮の値。
        /// 下(コモン)は素の剣を少し補う程度、上(エピック)は群れをまとめて倒せる強さ。
        /// 剣は種類ごとに技の表(振り方)が違う。盾・手の銃は右手・左手のどちらにも付けられる。
        /// </summary>
        public static List<DuelWeapon> Defaults() => new()
        {
            // 剣(手。技の表で振り方が変わる)
            Sword("鋼の剣", 0, "素の剣と同じ振り。重く、少し長い", DuelMoveSet.Standard(), 1.3f, 1.1f, 1.2f, 1.1f).Spec(6f),
            Sword("軽剣", 0, "浅く速い3段の連撃。一振りは軽い", DuelMoveSet.Quick(), 1.1f, 0.95f, 1f, 1f).Spec(4f),
            Sword("大剣", 1, "大きく振りかぶって叩き斬る。遅いが重く広い", DuelMoveSet.Great(), 1.25f, 1.3f, 1.2f, 1.2f).Spec(10f),
            Sword("高周波ブレード", 1, "速く鋭い4段の連撃と、踏み込む突き", DuelMoveSet.Blade(), 1.35f, 1.15f, 1.2f, 1f).Spec(6f),
            Sword("斬艦刀", 2, "長大な刃を体ごと回して、周りごと薙ぎ払う", DuelMoveSet.Giant(), 1.6f, 1.7f, 1.5f, 1.5f).Spec(14f),
            // 盾(手。付けた手のボタンを押している間ガード)
            Shield("鉄板の盾", 0, "軽く、構えたまま動きやすいが、すぐ削れる", 80f, 1.2f, 0.15f, 0.55f, 1f, 0.9f, new Vector3(0.3f, 2.6f, 2.2f)).Spec(6f),
            Shield("重装甲の盾", 0, "よく持つが重い。構えるとほとんど動けない", 140f, 0.9f, 0.08f, 0.3f, 0.92f, 0.8f, new Vector3(0.45f, 3.6f, 2.8f)).Spec(14f),
            Shield("複合装甲の盾", 1, "よく持ち、通す傷も少ない", 150f, 0.8f, 0.05f, 0.45f, 0.97f, 0.75f, new Vector3(0.4f, 3.2f, 2.6f)).Spec(11f),
            Shield("小型の盾", 1, "受けてもすぐ動ける。構えたまま走れる", 90f, 1f, 0.1f, 0.75f, 1f, 0.5f, new Vector3(0.3f, 2f, 1.8f)).Spec(4f),
            Shield("エネルギーシールド", 2, "ほとんど削れず、傷も通さない", 220f, 0.6f, 0f, 0.6f, 1f, 0.6f, new Vector3(0.2f, 3.8f, 3.2f)).Spec(7f),
            // 手の銃(手。付けた手のボタンで撃つ。肩の銃より一発ずつ狙って撃つ性格)
            HandGun("ハンドガン", 0, "押すたびに1発。弱いが狙いが正確", false, 4f, 0.2f, 55f, 0.4f, 1, 12, 1.3f, 1.5f, 3f, new Vector3(0.4f, 0.7f, 2.6f)).Spec(3f, 1f, 14f).Recoil(0.8f, 3f, 10f).Ballistic(170f, 1.2f, 0.4f),
            HandGun("サブマシンガン", 0, "押している間ばらまく。近い敵向け", true, 1.4f, 0.07f, 35f, 4f, 1, 30, 1.8f, 0.3f, 2f, new Vector3(0.45f, 0.8f, 3f)).Spec(4f, 0.5f, 16f).Recoil(0.35f, 5f, 6f).Ballistic(160f, 0.35f, 0.3f),
            HandGun("ライフル", 1, "一発が重く、遠くまで届く", false, 11f, 0.5f, 90f, 0.2f, 1, 6, 2f, 4f, 6f, new Vector3(0.45f, 0.8f, 4.6f)).Spec(6f, 3f, 6f).Recoil(1.5f, 4f, 9f).Ballistic(420f, 3f, 0.6f),
            HandGun("散弾銃", 1, "近くで撃てば怯みを一気にため、押し返す", false, 2.4f, 0.75f, 28f, 8f, 8, 4, 2.2f, 0.9f, 10f, new Vector3(0.6f, 0.9f, 3.6f)).Spec(6f, 4f, 18f).Recoil(1f, 3f, 6f).Ballistic(130f, 3.5f, 0.8f),
            HandGun("ビームライフル", 2, "弾を使わずENで撃つ。一発が重く遠くまで届く", false, 16f, 0.45f, 110f, 0f, 1, 0, 0f, 5f, 8f, new Vector3(0.5f, 0.9f, 5f)).Spec(7f, 0f, 7f, en: 12f).Recoil(1.2f, 3.5f, 9f).Ballistic(320f, 2.5f, 0.5f),
            // 背中
            Back("背部スラスター", 0, "速さと燃費のバランス型", 1.05f, 1.15f, 1.1f, 0.85f, new Vector3(2.4f, 2.8f, 1.3f)).Spec(5f),
            Back("省エネスラスター", 0, "速さは控えめだが、長く飛び続けられる", 1f, 1.05f, 1f, 0.6f, new Vector3(2f, 2.4f, 1.1f)).Spec(5f),
            Back("軽量スラスター", 1, "軽く速い。回避が伸びる", 1.15f, 1.3f, 1.2f, 0.75f, new Vector3(1.6f, 2f, 0.8f)).Spec(2f),
            Back("高速スラスター", 2, "とても速く、燃費も良い", 1.25f, 1.6f, 1.35f, 0.6f, new Vector3(2.8f, 3.2f, 1.6f)).Spec(4.5f),
            // 肩の銃(右肩・左肩のどちらにも)
            Gun("肩部機関銃", 0, "弱いが、離れた所から削れる", true, 1.2f, 0.12f, 55f, 2.5f, 1, 24, 2.2f, 0.4f, new Vector3(0.7f, 1.6f, 0.7f)).Spec(6f, 0.5f, 14f).Recoil(0.3f, 4f, 5f).Ballistic(170f, 0.3f, 0.25f),
            Gun("重機関銃", 1, "一発が重く、よく削れる", true, 2f, 0.09f, 75f, 2f, 1, 45, 2.5f, 0.6f, new Vector3(1.2f, 2f, 1.2f)).Spec(12f, 0.5f, 10f).Recoil(0.3f, 5f, 5f).Ballistic(200f, 0.4f, 0.3f),
            Gun("肩部散弾銃", 1, "近いほど多く当たる9粒の散弾", false, 2.2f, 0.6f, 35f, 7f, 9, 6, 2f, 0.8f, new Vector3(1f, 1.5f, 1f)).Spec(8f, 4f, 18f).Recoil(1f, 3f, 6f).Ballistic(130f, 3f, 0.8f),
            Gun("ビームガトリング", 2, "弾を使わずENで撃ち続けて溶かす", true, 1.8f, 0.06f, 95f, 1.5f, 1, 0, 0f, 0.5f, new Vector3(1.1f, 2.4f, 1.1f)).Spec(11f, 0f, 10f, en: 20f).Recoil(0.2f, 4f, 5f).Ballistic(240f, 0.25f, 0.2f),
            // 肩の砲・ミサイル(右肩・左肩のどちらにも)
            Heavy("速射砲", 0, "爆発は小さいが、すぐ次が撃てる", true, 8f, 1, 5f, 1.4f, 70f, 6f, 8f, PrimitiveType.Cylinder, new Vector3(0.6f, 1.4f, 0.6f)).Spec(8f, 2f, 10f).Ballistic(110f, 2f, 0.5f),
            Heavy("重榴弾砲", 1, "装填は遅いが、大きな爆発で群れをまとめて削る", false, 22f, 1, 12f, 3.5f, 80f, 14f, 22f, PrimitiveType.Cylinder, new Vector3(1.3f, 2f, 1.3f)).Spec(20f, 6f, 8f).Ballistic(80f, 4f, 0.8f),
            Heavy("ミサイルポッド", 1, "4発が狙える敵を追いかける", false, 6f, 4, 4f, 3f, 100f, 4f, 6f, PrimitiveType.Cube, new Vector3(1.2f, 1f, 1.4f)).Spec(8f, 5f, 22f),
            Heavy("多弾頭ミサイル", 2, "10発が周りの敵へ散って追いかける", false, 10f, 10, 5f, 4f, 110f, 6f, 8f, PrimitiveType.Cube, new Vector3(1.8f, 1.4f, 1.8f)).Spec(14f, 15f, 26f),
            Heavy("超重砲", 2, "装填はとても遅いが、群れごと吹き飛ばす", false, 40f, 1, 20f, 6f, 90f, 30f, 30f, PrimitiveType.Cylinder, new Vector3(1.9f, 3.2f, 1.9f)).Spec(25f, 12f, 6f).Ballistic(70f, 6f, 1f),

            // 両手の近接(右手と左手の両方を使う。R1 = 連撃 / L1 = 別の技 / △+R1・L1 = 強攻撃。盾がないのでガードはできない)
            // 同じレア度の片手の剣より、一振りの威力・届く範囲・怯ませがはっきり上。振りはやや遅く、ENも多めに使う
            TwoMelee("両手剣", 0, "重い3段の連撃。L1 で大きく踏み込む突進斬り", "突進斬り", DuelMoveSet.TwoHandSword(), 1.15f, 1.4f, 1.2f, 1.2f, 1.5f, 2f).Spec(12f),
            TwoMelee("長槍", 0, "遠くまで届く速い突き。L1 で周りをまとめて薙ぎ払う", "薙ぎ払い", DuelMoveSet.Spear(), 1.1f, 1.7f, 1.1f, 1.1f, 0.7f, 4f).Spec(9f),
            TwoMelee("薙刀", 1, "広く流れる4段の連撃。L1 で長い突き → 斬り上げ", "突き → 斬り上げ", DuelMoveSet.Glaive(), 1.7f, 1.55f, 1.3f, 1.2f, 0.8f, 3.5f).Spec(11f),
            TwoMelee("戦鎚", 1, "遅いが一振りで敵を崩す。L1 で周りごと叩きつける", "叩きつけ", DuelMoveSet.Hammer(), 1.5f, 1.3f, 1.8f, 1.6f, 2.6f, 2.5f).Spec(16f),
            TwoMelee("斬機大太刀", 2, "長大な刃を両手で回し、周りごと斬り払う。L1 で大回転", "大回転 → 回転斬り上げ", DuelMoveSet.GreatTachi(), 2.2f, 2f, 1.6f, 1.6f, 1.8f, 2.5f).Spec(15f),
            TwoMelee("突撃槍", 2, "とても遠くまで届く突き。L1 で敵まで一気に突撃する", "突撃", DuelMoveSet.Lance(), 2f, 2.1f, 1.6f, 1.8f, 0.9f, 5f).Spec(14f),

            // 両手の銃・砲(右手と左手の両方を使う。R1 = 主の撃ち方 / L1 = 別の撃ち方。溜め撃ちは L1 を押して溜め、離して撃つ)
            // 同じレア度の手の銃・肩の砲より強い
            TwoGun("大型ライフル", 0, "重い一発。L1 を溜めて離すと、敵を貫く強い一発", "1発ずつ", "溜め撃ち(貫通)",
                   false, 14f, 0.45f, 100f, 0.2f, 1, 8, 2.2f, 6f, 8f, new Vector3(0.55f, 0.9f, 6f),
                   AltShot(40f, 0.8f, 120f, 0f, 1, 16f, 16f, charge: 1f, pierce: true, beamWidth: 0.5f).Spec(0f, 8f).Ballistic(520f, 6f, 1f))
                   .Spec(12f, 3f, 6f).Recoil(1.5f, 4f, 9f).Ballistic(450f, 3.5f, 0.6f),
            TwoGun("ガトリング砲", 0, "押している間ばらまき続ける。L1 で榴弾", "押している間連射", "榴弾",
                   true, 1.6f, 0.05f, 60f, 3f, 1, 90, 3f, 0.35f, 2f, new Vector3(0.9f, 0.9f, 4.5f),
                   AltBlast(16f, 1, 6f, 2.5f, 70f, 8f, 12f).Spec(0f, 4f, 10f).Ballistic(100f, 3f, 0.6f))
                   .Spec(16f, 0.5f, 12f).Recoil(0.25f, 6f, 6f).Ballistic(180f, 0.3f, 0.3f),
            TwoCannon("バズーカ", 1, "砲弾が大きく爆ぜる。L1 を溜めて離すと大爆発", "砲弾", "溜めて大爆発",
                      false, 30f, 1, 9f, 1.8f, 90f, 16f, 22f, PrimitiveType.Cylinder, new Vector3(1f, 2.8f, 1f),
                      AltBlast(60f, 1, 15f, 3f, 90f, 30f, 30f, charge: 1.2f).Spec(0f, 12f).Ballistic(60f, 7f, 1f)).Spec(14f, 6f, 8f).Ballistic(70f, 5f, 1f),
            TwoGun("ショットキャノン", 1, "近くの敵を12粒で押し返す。L1 で遠くまで届く重いスラグ弾", "散弾", "スラグ弾",
                   false, 2.8f, 0.8f, 32f, 9f, 12, 5, 2.4f, 1.2f, 14f, new Vector3(0.8f, 1f, 4.4f),
                   AltShot(34f, 1.2f, 70f, 0f, 1, 20f, 26f).Spec(0f, 6f, 7f).Recoil(2f, 4f, 6f).Ballistic(260f, 5f, 0.8f))
                   .Spec(13f, 6f, 16f).Recoil(1f, 3f, 6f).Ballistic(140f, 4f, 1f),
            TwoGun("レーザー砲", 2, "押している間、敵を貫く光線を撃ち続ける。L1 を溜めて離すと極太の光線", "押している間照射(貫通)", "溜め撃ち(極太・貫通)",
                   true, 3.8f, 0.08f, 110f, 0f, 1, 0, 0f, 0.6f, 2f, new Vector3(0.8f, 1.1f, 6.5f),
                   AltShot(90f, 1.5f, 130f, 0f, 1, 40f, 30f, charge: 1.5f, pierce: true, beamWidth: 3f).Spec(0f, 0f, en: 45f).Ballistic(0f, 6f, 1f), pierce: true, beamWidth: 0.3f)
                   .Spec(15f, 0f, 7f, en: 25f).Recoil(0.1f, 1.5f, 4f).Ballistic(0f, 0.15f, 0.15f),   // 照射するレーザーと極太の光線は今までどおりすぐ届く(速さ 0)
            TwoCannon("多連装ロケット", 2, "8発のロケットが狙える敵を追う。L1 を溜めて離すと20発の一斉発射", "8発", "溜めて一斉発射",
                      false, 9f, 8, 5f, 2.5f, 110f, 5f, 8f, PrimitiveType.Cube, new Vector3(1.6f, 1.4f, 3.6f),
                      AltBlast(12f, 20, 6f, 5f, 120f, 6f, 10f, charge: 1.2f).Spec(0f, 30f)).Spec(18f, 12f, 24f),
        };
    }
}
