using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// 近接の試作。平らな部屋で、雑魚(兵卒)の群れと中型(剣士)が交互に出てくる。全滅させると次の組が出る。
    /// 確かめたいこと:剣士との1対1を続けて面白いか / 群れを位置取りと回避斬りでさばけるか /
    /// 回避が軸になっているか / ガードが便利すぎないか。
    /// F1(または SHARE+△)でやり直し。
    /// stage(DuelStage)があれば、波で出す代わりに、墜落した船の中を登るステージになる(MeleeDuelStage.unity)。
    /// ファイルは役割ごとに分けている:DuelGame.Hud.cs(画面のゲージ・照準・ロックオンの表示・操作説明)/
    /// DuelGame.ArmsHud.cs(右下の武装の欄・拾う案内・付け先のグラフ)/ DuelGame.EquipView.cs(装備の確認)。
    /// </summary>
    public partial class DuelGame : MonoBehaviour
    {
        public static DuelGame I;

        [Header("機体(シーンを組み立てるときに入る)")]
        public GameObject mechPrefab;
        public float mechHeight = 9f;

        [Header("部屋")]
        [Tooltip("部屋の半分の広さ。0 以下なら外へ出ないようにはしない(ステージは壁で止める)")]
        public float arenaHalf = 60f;

        [Header("ステージ(あれば波の代わりにステージを進む)")]
        public DuelStage stage;

        [Header("敵")]
        [Tooltip("剣士(中型)のHP")]
        public float enemyHP = 47f;
        [Tooltip("兵卒(雑魚)のHP")]
        public float gruntHP = 28f;
        [Tooltip("同時に攻撃してくる敵の数の上限(ほかは回り込んで順番を待つ)")]
        public int maxAttackers = 2;
        public float respawnDelay = 2f;

        [Header("崩し(銃で崩しにいき、剣で仕留める。部屋・ステージ・ボス共通)")]
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

        /// <summary>怯みのバーを白く点滅させ始める割合(8割か、射撃の上限の少し手前の低いほう)。射撃で止まった所が「斬りにいけ」の合図になる。</summary>
        public float StaggerBlinkFrom => Mathf.Min(0.8f, rangedStaggerCap - 0.02f);

        [Header("頭上のゲージ(電脳斬禅と同じ。近くの敵の頭の上に、HPと怯みの小さなバー)")]
        [Tooltip("自機からこの距離(m)より遠い敵には出さない")]
        public float overheadRange = 90f;
        [Tooltip("この距離(m)より遠い敵は、ゲージを小さくしていく(最小で7割)")]
        public float overheadShrinkFrom = 45f;

        /// <summary>出てくる組の順番(最後まで行ったら最初に戻る)。G = 兵卒、D = 剣士。</summary>
        static readonly string[] Waves = { "GGG", "D", "GGGG", "DGG" };

        public List<DuelEnemy> Enemies { get; } = new();

        /// <summary>付ける所の数(DuelSlot の数。装備の確認の行数・右下の武装の欄の所の数も同じ)。</summary>
        const int ArmRows = 5;

        DuelPlayer player;
        int kills, wave;
        float startTime, restartAt = -1f, warpUntil, nextSpawnAt = -1f;
        readonly List<(string msg, float t)> log = new();
        GUIStyle small, label, big, center, richCenter, rightSmall, richSmall, richRight, viewHeadStyle, viewTitleStyle, chartHeadStyle, chartLegendStyle, chartNoteStyle;
        GUIStyle cellTiny, cellTinyRight, cellNameStyle, cellNameSmall, cellBack;
        /// <summary>頭上のゲージで、減ったHPを少し遅れて追う白い部分(敵ごと)。</summary>
        readonly Dictionary<DuelEnemy, (float ghost, float hp, float holdUntil)> overheadGhost = new();
        float uiScale = 1f;
        float SW => Screen.width / uiScale;
        float SH => Screen.height / uiScale;

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            ProtoInput.Paused = false;
            // SHARE+△ でやり直した直後:押したままの SHARE を離しても、装備の確認を開かない
            ProtoInput.SelectHeldAsCombo();
            DuelRadar.ClearCache();   // 前のシーンの武装の組をためこまない
        }

        void OnDestroy()
        {
            if (I != this) return;
            // 装備の確認を開いたまま抜けた(やり直し・Play の停止):止めた時間と入力を戻す
            if (equipOpen) Time.timeScale = 1f;
            equipOpen = false;
            ProtoInput.Paused = false;
        }

        void Start()
        {
            Vector3 startPos = new(0f, 0f, -20f);
            Quaternion startRot = Quaternion.identity;
            if (stage != null)
            {
                stage.Prepare(this);   // 地形を組み立てる
                startPos = stage.StartPos;
                startRot = stage.StartRot;
            }
            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(startPos, startRot);
            player = go.AddComponent<DuelPlayer>();
            player.Init(this);

            var cam = Camera.main != null ? Camera.main.GetComponent<DuelCamera>() : null;
            if (cam != null) cam.scale = mechHeight / 2.1f;   // 人間サイズのカメラを機体の大きさへ

            startTime = Time.time;
            if (stage != null)
            {
                stage.Begin(player);
                Log("ステージの試作:敵が落とす武装を拾って強くなりながら船の中を登り、頭頂部のボスを倒せばクリア。F1で最初から");
            }
            else
            {
                SpawnWave();
                Log("近接の試作:兵卒の群れと剣士が交互に出る。F1でやり直し");
            }
        }

        /// <summary>次の組を、自機から少し離れた所に扇状に並べて出す。</summary>
        void SpawnWave()
        {
            string w = Waves[wave % Waves.Length];
            wave++;
            Log($"第{wave}組:" + (w.Contains('D') ? "剣士" : "") + (w.Contains('D') && w.Contains('G') ? "と" : "") + (w.Contains('G') ? $"兵卒×{w.Split('G').Length - 1}" : ""));
            Vector3 basePos = player != null ? player.transform.position : Vector3.zero;
            Vector3 center = new Vector3(0f, 0f, 0f) - basePos;
            center.y = 0f;
            Vector3 fwd = center.sqrMagnitude > 1f ? center.normalized : Vector3.forward;
            for (int i = 0; i < w.Length; i++)
            {
                // 部屋の中心の側へ、40m先に扇状に(±70°)
                float ang = w.Length == 1 ? 0f : Mathf.Lerp(-70f, 70f, i / (float)(w.Length - 1));
                Vector3 pos = basePos + Quaternion.Euler(0f, ang, 0f) * fwd * 40f;
                pos.x = Mathf.Clamp(pos.x, -arenaHalf + 6f, arenaHalf - 6f);
                pos.z = Mathf.Clamp(pos.z, -arenaHalf + 6f, arenaHalf - 6f);
                pos.y = 0f;
                var go = new GameObject($"Enemy{i}");
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(Flat(basePos - pos));
                var e = go.AddComponent<DuelEnemy>();
                e.Init(this, player, w[i] == 'D' ? DuelEnemy.Duelist(enemyHP) : DuelEnemy.Grunt(gruntHP));
                Enemies.Add(e);
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
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
            if (ProtoInput.Restart || (restartAt > 0f && Time.unscaledTime >= restartAt))
            {
                Restart();
                return;
            }
            UpdateEquipView();
            // 装備の確認を開いている間は時間を止める。ほかはヒットストップ:短い間だけ時間をほぼ止める
            Time.timeScale = equipOpen ? 0f : Time.unscaledTime < warpUntil ? 0.05f : 1f;
            if (equipOpen) return;

            Enemies.RemoveAll(e => e == null);
            if (nextSpawnAt > 0f && Time.time >= nextSpawnAt)
            {
                nextSpawnAt = -1f;
                SpawnWave();
            }
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
            if (stage != null)
            {
                // ステージ:倒した敵は復活しないので、そのまま数える。ボスならクリア
                stage.OnEnemyDied(e);
                return;
            }
            // 組を全滅させたら、少しして次の組
            bool anyAlive = Enemies.Exists(x => x != null && !x.Dead);
            if (!anyAlive && nextSpawnAt < 0f) nextSpawnAt = Time.time + respawnDelay;
        }

        public void OnPlayerDied()
        {
            Log("撃墜された");
            if (stage != null) stage.OnPlayerDied();   // 最後に触れたセーブポイントから再開する
            else restartAt = Time.unscaledTime + 2.5f;
        }

        public void Log(string msg)
        {
            log.Add((msg, Time.unscaledTime));
            if (log.Count > 6) log.RemoveAt(0);
        }
    }
}
