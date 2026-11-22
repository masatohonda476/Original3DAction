using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// プロトタイプ。墜落した船の中を登るステージ(DuelStage。シーンは PrototypeStage.unity)で、雑魚(兵卒)・中型(剣士)・ボスと戦う。
    /// ここは自機を出し、同時に攻撃してくる敵の数を絞り、ヒットストップ・撃破数・知らせ・画面の表示を受け持つ
    /// (地形・敵の配置・セーブポイント・クリアは DuelStage)。
    /// 確かめたいこと:剣士との1対1を続けて面白いか / 群れを位置取りと回避斬りでさばけるか /
    /// 回避とジャンプで敵の攻撃を越え、近接と遠距離を使い分けられるか。
    /// F1(または SHARE+△)でやり直し。
    /// 平らな部屋に波で敵を出す「部屋のモード」(MeleeDuel.unity。stage なし・武装なし)はやめた(作者「MeleeDuel消して」2026-11-20)。stage は必ず要る。
    /// ファイルは役割ごとに分けている:DuelGame.Hud.cs(画面のゲージ・照準・ロックオンの表示・左下のメニューの案内)/
    /// DuelGame.ArmsHud.cs(右下の武装の欄・拾う案内・装備の比較)/
    /// DuelGame.Menu.cs(OPTIONS / I で開く項目のメニュー・操作方法のページ。操作方法は画面に出し続けず、ここで見る)/
    /// DuelGame.EquipView.cs(装備の確認。メニューの「装備」のページ)。
    /// </summary>
    public partial class DuelGame : MonoBehaviour
    {
        public static DuelGame I;

        [Header("機体(シーンを組み立てるときに入る)")]
        public GameObject mechPrefab;
        public float mechHeight = 9f;

        [Header("ステージ(必ず要る。シーンを組み立てるときに入る)")]
        public DuelStage stage;

        [Header("敵")]
        [Tooltip("剣士(中型)のHP")]
        public float enemyHP = 47f;
        [Tooltip("兵卒(雑魚)のHP")]
        public float gruntHP = 28f;
        [Tooltip("同時に攻撃してくる敵の数の上限(ほかは回り込んで順番を待つ)")]
        public int maxAttackers = 2;

        [Header("崩し(近接で崩す。雑魚・ボス共通)")]
        [Tooltip("射撃で溜められる敵の怯みゲージの上限(最大の何割)。射撃だけでは怯ませられず、最後に崩す(怯ませ切る)のは近接だけ。" +
                 "怯みのバーはこの少し手前から白く点滅する。1 にすると射撃でも怯む(元の動き)")]
        [Range(0f, 1f)] public float rangedStaggerCap = 0.9f;
        [Tooltip("崩れている(怯んでいる)敵に近接を当てたときのダメージの倍率(射撃には掛からない)")]
        public float brokenMeleeDamageMul = 2f;
        [Tooltip("近接で崩したときの怯みの最短の秒数。技の怯みの時間がこれより短ければ延ばす(崩した後に一太刀入れる間)。0 なら技の値のまま")]
        public float breakFlinchMin = 1f;
        [Tooltip("射撃の1発ごとに軽く押し返す速さ(武装の knock に掛ける倍率)。怯ませず、技も止めない。0 なら押さない")]
        public float rangedPushMul = 1f;
        [Tooltip("武装の knock がこれ以上の射撃だけ1発ごとに押し返す(連射の銃で、寄ってくる敵を引き止め続けないように)")]
        public float rangedPushMin = 8f;
        [Tooltip("自機の近接(通常攻撃・回避斬り)が、敵の怯みゲージへためる量に掛ける倍率。" +
                 "ふつうに殴るだけでは崩しにくくする(1 で前と同じ)。攻撃の後の隙に当てると 1.6 倍・射撃の上限・崩れた敵への近接 2 倍はそのまま。" +
                 "攻撃を跳び越えた敵への落下攻撃には掛からない(下の jump.dropSuccessStagger)")]
        public float meleeStaggerMul = 0.3f;

        [Header("致命攻撃(試す版。崩れた敵の近くで、地上で近接のボタン。技の時間・姿勢は DuelMoveSet.Fatal。値は仮)")]
        [Tooltip("崩れた敵とのすき間がこれ(m)以内なら、近接のボタンが致命攻撃になる(相手へ踏み込む)。0 で致命攻撃なし")]
        public float fatalRange = 12f;
        [Tooltip("致命攻撃のダメージの下限(兵卒の HP 28・剣士 47 を 1 発で倒す量)。武装の威力の倍率・崩れた敵への近接の倍率は掛からない")]
        public float fatalMinDamage = 60f;
        [Tooltip("致命攻撃のダメージ = 相手の最大 HP × これ(上の下限より大きいほう)。ボス(HP 700)なら 0.25 で 175")]
        [Range(0f, 1f)] public float fatalHpPart = 0.25f;
        [Tooltip("致命攻撃の踏み込みの速さの上限(m/s)")]
        public float fatalLungeSpeed = 60f;
        [Tooltip("致命攻撃を当てた瞬間のヒットストップ(秒)。0 で止めない")]
        public float fatalHitStop = 0.2f;
        [Tooltip("致命攻撃を受けて崩れが解けた敵が、次の技を出すまでの最短の秒数")]
        public float fatalEnemyWait = 1f;

        [Header("ジャンプで越えて落下攻撃で崩す(試す版。数値・技の区分は仮)")]
        public DuelJumpRules jump = new();

        /// <summary>怯みのバーを白く点滅させ始める割合(8割か、射撃の上限の少し手前の低いほう)。射撃で止まった所が「斬りにいけ」の合図になる。</summary>
        public float StaggerBlinkFrom => Mathf.Min(0.8f, rangedStaggerCap - 0.02f);

        [Header("頭上のゲージ(電脳斬禅と同じ。近くの敵の頭の上に、HPと怯みの小さなバー)")]
        [Tooltip("自機からこの距離(m)より遠い敵には出さない")]
        public float overheadRange = 90f;
        [Tooltip("この距離(m)より遠い敵は、ゲージを小さくしていく(最小で7割)")]
        public float overheadShrinkFrom = 45f;

        public List<DuelEnemy> Enemies { get; } = new();

        /// <summary>付ける所の数(DuelSlot の数。装備の確認の行数・右下の武装の欄の所の数も同じ)。</summary>
        const int ArmRows = 3;

        DuelPlayer player;
        int kills;
        float startTime, warpUntil;
        readonly List<(string msg, float t)> log = new();
        GUIStyle small, label, big, center, rightSmall, richSmall, viewHeadStyle, viewTitleStyle;
        GUIStyle cellTiny, cellTinyRight, cellNameStyle, cellNameSmall;
        /// <summary>頭上のゲージで、減ったHPを少し遅れて追う白い部分(敵ごと)。</summary>
        readonly Dictionary<DuelEnemy, (float ghost, float hp, float holdUntil)> overheadGhost = new();
        float uiScale = 1f;
        float SW => Screen.width / uiScale;
        float SH => Screen.height / uiScale;
        /// <summary>
        /// 画面の表示の倍率(1280×720 の画面の単位 → 画素)を、今の画面の大きさから求め直す。
        /// OnGUI の頭と、OnGUI の外で画面の単位を使う所(ロックオンの付け始めの判定。LockSightTan)で呼ぶ(OnGUI がまだ走っていない最初のフレームでも同じ値になる)。
        /// </summary>
        void RefreshUiScale() => uiScale = Mathf.Max(0.0001f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));

        void Awake()
        {
            I = this;
            jump ??= new DuelJumpRules();
            Time.timeScale = 1f;
            ProtoInput.Paused = false;
        }

        void OnDestroy()
        {
            if (I != this) return;
            // メニュー(項目・装備の確認・操作方法)を開いたまま抜けた(やり直し・Play の停止):止めた時間と入力を戻す
            if (MenuOpen) Time.timeScale = 1f;
            menuPage = MenuPage.Closed;
            ProtoInput.Paused = false;
        }

        void Start()
        {
            if (stage == null)
            {
                // stage なしで波の敵を出す「部屋のモード」はやめた。ステージがなければ何も始めない(自機も出さない)
                Debug.LogError("[Prototype] DuelGame.stage が入っていません。ステージのシーン(PrototypeStage.unity)で動かしてください", this);
                enabled = false;
                return;
            }
            stage.Prepare(this);   // 地形を組み立てる
            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(stage.StartPos, stage.StartRot);
            player = go.AddComponent<DuelPlayer>();
            player.Init(this);

            var cam = Camera.main != null ? Camera.main.GetComponent<DuelCamera>() : null;
            if (cam != null) cam.scale = mechHeight / 2.1f;   // 人間サイズのカメラを機体の大きさへ

            startTime = Time.time;
            stage.Begin(player);
            Log("敵が落とす武装を拾って船の中を登り、頭頂部のボスを倒す");
        }

        /// <summary>その敵が技を出し始めてよいか(同時に攻撃している敵が maxAttackers 未満なら)。</summary>
        public bool CanStartAttack(DuelEnemy who)
        {
            int n = 0;
            foreach (var e in Enemies)
                if (e != null && e != who && !e.Dead && e.Attacking) n++;
            return n < maxAttackers;
        }

        void Update()
        {
            ProtoInput.TrackDevice();
            if (ProtoInput.Restart)
            {
                Restart();
                return;
            }
            UpdateMenu();
            // メニュー(項目・装備の確認・操作方法)を開いている間は時間を止める。ほかはヒットストップ:短い間だけ時間をほぼ止める
            Time.timeScale = MenuOpen ? 0f : Time.unscaledTime < warpUntil ? 0.05f : 1f;
            if (MenuOpen) return;

            Enemies.RemoveAll(e => e == null);
        }

        void Restart()
        {
            Time.timeScale = 1f;
            string path = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
#endif
        }

        /// <summary>当たった瞬間に、realSeconds 秒だけ時間をほぼ止める。</summary>
        public void HitStop(float realSeconds) => warpUntil = Mathf.Max(warpUntil, Time.unscaledTime + realSeconds);

        public void OnEnemyDied(DuelEnemy e)
        {
            kills++;
            stage.OnEnemyDied(e);   // 倒した敵は復活しないので、そのまま数える。ボスならクリア
        }

        public void OnPlayerDied()
        {
            Log("撃墜された");
            stage.OnPlayerDied();   // 最後に触れたセーブポイントから再開する
        }

        public void Log(string msg)
        {
            log.Add((msg, Time.unscaledTime));
            if (log.Count > 6) log.RemoveAt(0);
        }
    }
}
