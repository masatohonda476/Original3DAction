using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// サイクルの試作の、照準の枠・装備の重さ・共通の弾・分解の決まり(数値は仮)。ステージ(DuelStage.armsRules)のインスペクターで直せる。
    /// 武装ごとの値(重さ・弾の使う量・枠の大きさ・ENの使う量)は DuelWeapon.Catalog.cs。
    /// </summary>
    [System.Serializable]
    public class DuelArmsRules
    {
        /// <summary>武装に枠の大きさがないときの値(度)。</summary>
        public const float FallbackFrame = 12f;

        [Header("射撃のサイト(画面中央。武器ごとの枠に掛かった敵へ射撃を補正)")]
        [Tooltip("全部の武装の枠の大きさに掛ける倍率(1 で武装ごとの値のまま)")]
        public float frameMul = 1f;
        [Tooltip("枠の縦の大きさ(横に対する割合)")]
        [Range(0.2f, 1f)] public float frameAspect = 0.6f;
        [Tooltip("武装に枠の大きさがないときの値(度)")]
        public float defaultFrame = FallbackFrame;

        [Header("弾の速さ(武装ごとの値は DuelWeapon.Catalog.cs)")]
        [Tooltip("全部の武装の弾の速さに掛ける倍率(1 で武装ごとの値のまま。0 にするとすぐ届く光線)")]
        public float shotSpeedMul = 1f;
        [Tooltip("撃った自機の速度を弾が受け継ぐ割合(1 で全部、0 で受け継がない)。敵と相対速度を合わせて動きながら撃つと当たる。速さを持つ弾(実弾・ビームの弾)だけで、すぐ届く光線には効かない")]
        [Range(0f, 1f)] public float inheritShooterVelocity = 1f;
        [Tooltip("弾が受け継ぐ自機の速度が、今の速度へ追いつくまでの秒数(だいたい)。動き始め・止まり・回避で十字と弾の流れが跳ばないように(作者「1: なめらかにするだけ」)。0 で今の速度そのまま")]
        public float inheritSmoothTime = 0.2f;
        [Tooltip("銃の弾の球の大きさ(m。機体は約9m)/ 散弾の1粒の大きさ")]
        public float bulletSize = 0.45f, pelletSize = 0.3f;
        [Tooltip("銃の弾の尾(弾の筋)の長さ(秒。速い弾ほど長い筋になる)")]
        public float bulletTrail = 0.07f;

        [Header("反動(連射で十字と弾のばらつきが広がる。カメラは跳ね上げない・揺らさない。武装ごとの値は DuelWeapon.Catalog.cs)")]
        [Tooltip("全部の武装の広がり(1発ごと・上限)に掛ける倍率。0 で反動なし")]
        public float recoilMul = 1f;
        [Tooltip("最後に撃ってから、広がりが戻り始めるまでの秒数(連射の間は戻らずに広がっていく)")]
        public float recoilDelay = 0.12f;

        [Header("手応えの表示(ヒットマーカー・弾の筋)")]
        [Tooltip("当たったときに照準の真ん中に出す印の秒数(実時間)")]
        public float hitMarkTime = 0.22f;
        [Tooltip("当たった弾の筋が残る秒数(細くなって消える)")]
        public float tracerLife = 0.08f;
        [Tooltip("外れた弾の筋が残る秒数(当たったときより長く見せる)")]
        public float missTracerLife = 0.2f;
        [Tooltip("外れた弾の筋の太さ(m。機体は約9m)")]
        public float missTracerWidth = 0.35f;

        [Header("装備の重さ")]
        [Tooltip("この重さまでは遅くならない")]
        public float freeWeight = 15f;
        [Tooltip("それを超えた重さ1あたりに、ENを使う移動の速さが下がる割合。歩き・走りには効かない")]
        public float slowPerWeight = 0.004f;
        [Range(0.1f, 1f)] public float minMobility = 0.5f;

        [Header("共通の弾と自動装填")]
        [Min(0f)] public float ammoMax = 500f;
        [Min(0f)] public float ammoStart = 30f;
        [Tooltip("致命攻撃が当たったときに得る共通の弾")]
        [Min(0f)] public float fatalAmmo = 20f; // 仮
        [Tooltip("近接を構えている間、弾倉に一発詰めるまでの秒数")]
        [Min(0.01f)] public float reloadRoundTime = 0.35f; // 仮

        [Header("落とし物の解体")]
        [Tooltip("弾の取得量は基本値にレア度ごとの増分を足す。弾倉に残っている弾も返る")]
        [Min(0f)] public float salvageAmmoBase = 2f, salvageAmmoPerRarity = 2f; // 仮
        [Tooltip("回復量はレア度で決まる。既定はコモン 13、レア 24、エピック 35")]
        public float breakHealBase = 13f, breakHealPerRarity = 11f;

        /// <summary>今のステージの決まり(ステージがなければ既定の値)。性能表の「枠」などで使う。</summary>
        public static DuelArmsRules Current => Of(DuelGame.I);

        /// <summary>そのゲームのステージの決まり(ステージ・ゲームがなければ既定の値。書き換えないこと)。</summary>
        public static DuelArmsRules Of(DuelGame g) => g != null && g.stage != null && g.stage.armsRules != null ? g.stage.armsRules : Fallback;
        static readonly DuelArmsRules Fallback = new();

        /// <summary>射撃の照準枠の横半分の角度(度)。</summary>
        public float FrameDeg(DuelWeapon weapon)
        {
            float deg = weapon.aimFrame > 0f ? weapon.aimFrame : defaultFrame;
            return Mathf.Clamp(deg * frameMul, 0.5f, 70f);
        }

        public int BreakAmmo(DuelWeapon w) => Mathf.Max(0, Mathf.RoundToInt(salvageAmmoBase + salvageAmmoPerRarity * w.rarity));
        public int BreakHeal(DuelWeapon w) => Mathf.Max(0, Mathf.RoundToInt(breakHealBase + breakHealPerRarity * w.rarity));

        /// <summary>重さによる推力の倍率(ENを使う移動:ブースト移動・回避の距離・飛ぶときの上昇。歩き・走りには掛けない)。</summary>
        public float MobilityAt(float weight) => Mathf.Clamp(1f - Mathf.Max(0f, weight - freeWeight) * slowPerWeight, minMobility, 1f);
    }
}
