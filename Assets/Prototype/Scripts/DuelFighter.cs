using System.Collections;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    /// <summary>攻撃1回ぶんの当たり方(敵 → 自機)。</summary>
    public struct DuelHit
    {
        public float damage;
        /// <summary>ガードできる攻撃か(叩きつけ・跳躍叩きつけの直撃はできない。どの技かは色ではなく予備動作の姿勢で見分ける)。</summary>
        public bool guardable;
        /// <summary>ガードしたときに減るガードのゲージ。</summary>
        public float guardCost;
        /// <summary>怯みゲージへの量。</summary>
        public float stagger;
        /// <summary>この攻撃で怯みゲージがいっぱいになったときに怯む時間(秒)。</summary>
        public float hurtTime;
        /// <summary>吹き飛ばす速さ(向きは攻撃した側から外向き)。</summary>
        public float knock;
        public Vector3 from;
    }

    /// <summary>
    /// 自機と敵の共通部分:機体の見た目・HP・重力・ノックバック・被弾の色。
    /// 機体のリグ(MechRig)は武装ハントのプレハブを使い回す。
    /// </summary>
    public abstract class DuelFighter : MonoBehaviour
    {
        public float MaxHP { get; private set; }
        public float HP { get; protected set; }
        public bool Dead => HP <= 0f;
        /// <summary>
        /// 撃墜の演出で爆発し終えたか(倒してから約 0.6 秒後。DeathSequence)。倒してからここまでの間は、火花を散らしてまだ立っている。
        /// ロックオンは爆発するまで残す(DuelPlayer.UpdateLock)。
        /// </summary>
        public bool Exploded { get; private set; }
        /// <summary>機体の大きさの倍率(見た目・当たりの大きさ・技の届く範囲に掛かる)。</summary>
        public float Size { get; private set; } = 1f;
        public float Radius => 2f * Size;
        public Vector3 Chest => transform.position + Vector3.up * game.mechHeight * 0.6f * Size;
        /// <summary>ロックオンの印を出す所で、カメラの注視点にも使う(機体の高さの7割)。</summary>
        public Vector3 LockPoint => transform.position + Vector3.up * game.mechHeight * 0.7f * Size;
        /// <summary>今の動きの速度(m/s。CharacterController が最後に動いた量から)。撃った弾が受け継ぐ(DuelArms.Fly)。</summary>
        public Vector3 Velocity => cc != null ? cc.velocity : Vector3.zero;

        protected DuelGame game;
        protected CharacterController cc;
        protected MechRig rig;
        /// <summary>ノックバック(だんだん止まる)。</summary>
        protected Vector3 push;
        float vy;
        /// <summary>飛んでいる(上昇し続ける)。false なら重力で落ちる。</summary>
        protected bool flying;
        /// <summary>噴射炎を出す(飛行中。自機はブースト移動・回避中も、敵はダッシュ中も)。</summary>
        public virtual bool Thrusting => flying;
        protected float riseSpeed = 20f, riseAccel = 80f;
        /// <summary>上下の速さを直接決める(跳び上がる)。</summary>
        protected void SetVerticalSpeed(float v) => vy = v;
        /// <summary>今の上下の速さ(上が正。m/s)。自機のジャンプが、上がっている途中か・頂点を過ぎたかを見るのに使う。</summary>
        protected float VerticalSpeed => vy;
        /// <summary>上昇の勢いだけを止める(下へ押しつけはしない)。空中で怯んだときに使う。</summary>
        protected void StopRising() => vy = Mathf.Min(vy, 0f);
        /// <summary>重力の強さ(m/s毎秒)。</summary>
        public const float Gravity = 40f;
        /// <summary>空中にいる(地面から少しでも浮いている)。</summary>
        public bool Airborne => cc != null && !cc.isGrounded && Altitude > 0.5f;
        /// <summary>足もとの地面(床・段差・坂の上面)からの高さ。上下の位置の差を比べるときは transform.position.y を使う。</summary>
        public float Altitude => transform.position.y - groundY;
        /// <summary>足もとの地面の高さ(移動のたびに真下へのレイで測る。何もなければ0)。</summary>
        float groundY;
        /// <summary>機体の層。レイ(地面の高さ・カメラの壁よけ)が機体に当たらないよう「Ignore Raycast」に置く。</summary>
        public const int FighterLayer = 2;
        float flashT;
        Color flashColor;
        Color? holdTint;

        protected void Setup(DuelGame g, float hp, float size = 1f)
        {
            game = g;
            MaxHP = hp;
            HP = hp;
            Size = size;
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = g.mechHeight * size;
            cc.radius = Radius;
            cc.center = new Vector3(0f, g.mechHeight * size * 0.5f + 0.1f, 0f);
            cc.skinWidth = 0.2f;
            cc.stepOffset = 0.6f;
            gameObject.layer = FighterLayer;
            UpdateGround();
            if (g.mechPrefab != null)
            {
                var go = Instantiate(g.mechPrefab, transform, false);
                go.name = "Visual";
                go.transform.localScale = Vector3.one * size;
                rig = go.GetComponent<MechRig>();
            }
        }

        /// <summary>真下の地面の高さを測り直す(機体自身・見えない壁は Ignore Raycast なので当たらない)。</summary>
        protected void UpdateGround()
        {
            groundY = Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var hit, 500f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
        }

        /// <summary>
        /// これより奥(+z)へは動けない(既定は制限なし)。DuelStage が、ボス以外の敵を大ホールの入口の手前で止めるのに入れる
        /// (見えない壁は機体みなに当たるので使えない:自機とボスは通す)。歩き・ダッシュ・技の踏み込み・跳び上がり・ノックバックのどれも Move を通るので越えない。
        /// 越える分の移動を切るだけなので、線に沿った横の動きはそのまま(引っかからない)。
        /// </summary>
        [System.NonSerialized] public float maxZ = float.PositiveInfinity;

        /// <summary>平面の速度で動かす(ノックバック・重力を足す)。外へ出ないのは、ステージの壁(見えない壁も)で止める。</summary>
        protected void Move(Vector3 planar, float dt)
        {
            push = Vector3.MoveTowards(push, Vector3.zero, 70f * dt);
            if (!cc.enabled) { FallAsWreck(dt); return; }
            if (flying)
            {
                // 上昇:高さの上限はなし(ENが続く限り上がれる)
                vy = Mathf.MoveTowards(vy, riseSpeed, riseAccel * dt);
            }
            else
            {
                if (cc.isGrounded && vy < 0f) vy = -2f;
                vy -= Gravity * dt;
            }
            planar.y = 0f;
            Vector3 step = (planar + push + Vector3.up * vy) * dt;
            float room = maxZ - transform.position.z;
            if (step.z > 0f && step.z > room) step.z = Mathf.Max(0f, room);   // 奥の限りを越える分は進まない(手前へ戻る動きは止めない)
            cc.Move(step);
            UpdateGround();
        }

        /// <summary>当たり判定を消した残骸が、空中から落ちている途中か。</summary>
        bool wreckFalling;

        /// <summary>
        /// 当たり判定を消す(爆発した後の残骸)。ほかの機体がぶつからずに通り抜けられる。
        /// 地面にいればその場に残り、空中なら真下の地面まで落ちる(押されない・横へは動かない)。
        /// </summary>
        void DropCollision()
        {
            wreckFalling = !cc.isGrounded && Altitude > 0f;
            cc.enabled = false;
        }

        /// <summary>当たり判定を消した残骸を、真下の地面(最後に測った高さ)まで落とす。</summary>
        void FallAsWreck(float dt)
        {
            if (!wreckFalling) return;
            vy = Mathf.Min(vy, 0f) - Gravity * dt;
            Vector3 p = transform.position;
            p.y += vy * dt;
            if (p.y <= groundY) { p.y = groundY; wreckFalling = false; }
            transform.position = p;
        }

        /// <summary>その場所へ瞬間移動する(やり直しの出し直しなど)。</summary>
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            cc.enabled = true;
            UpdateGround();
        }

        /// <summary>撃墜から立ち直らせる:HPを満タンにし、撃墜の演出・色・勢い・姿勢を消す。</summary>
        protected void Revive()
        {
            StopAllCoroutines();   // 撃墜の演出を止める
            cc.enabled = true;     // 残骸として消した当たり判定を戻す
            wreckFalling = false;
            Exploded = false;
            HP = MaxHP;
            flashT = 0f;
            holdTint = null;
            push = Vector3.zero;
            vy = 0f;
            flying = false;
            bodyLean = bodyTwist = bodyLift = bodyRoll = jolt = 0f;
            if (rig != null) rig.SetTint(null);
        }

        public void Knockback(Vector3 v)
        {
            v.y = 0f;
            push = v;
        }

        /// <summary>水平の向きへ、rate の速さで向き直る。</summary>
        protected void Face(Vector3 dir, float rate, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-rate * dt));
        }

        /// <summary>水平にして長さ1にした向き(ほぼ真上・真下なら zero)。</summary>
        internal static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }

        /// <summary>上下に離れすぎていて、斬り合いが届かないか(高さの差が、高いほうの機体の高さを超える)。</summary>
        protected bool TooFarVertically(DuelFighter other) =>
            Mathf.Abs(other.transform.position.y - transform.position.y) > game.mechHeight * Mathf.Max(Size, other.Size);

        /// <summary>相手との水平の距離(お互いの半径を引いた、すき間の距離)。</summary>
        protected float GapTo(DuelFighter other)
        {
            Vector3 d = other.transform.position - transform.position;
            d.y = 0f;
            return d.magnitude - Radius - other.Radius;
        }

        // ───────── 色 ─────────

        /// <summary>一瞬だけ色を付ける(被弾・ガード)。</summary>
        protected void Flash(Color c, float t)
        {
            flashColor = c;
            flashT = t;
        }

        /// <summary>続けて付けておく色(撃墜の黒焦げ)。null で外す。</summary>
        protected void SetHoldTint(Color? c) => holdTint = c;

        protected void UpdateTint(float dt)
        {
            flashT -= dt;
            if (rig != null) rig.SetTint(flashT > 0f ? flashColor : holdTint);
        }

        protected void Pose(string clip, float time, float blend = 20f)
        {
            if (rig != null) rig.Pose(clip, time, blend);
        }

        // ───────── 撃墜の演出 ─────────

        static readonly Color Charred = new(0.07f, 0.06f, 0.06f);

        /// <summary>
        /// 撃墜:火花が飛び散り、体のあちこちから火花がはじけ続けたあと、爆発して黒焦げになる。
        /// dropCollision なら、爆発したところで当たり判定を消す(黒焦げの残骸にぶつからない)。
        /// </summary>
        protected void PlayDeath(bool dropCollision = false) => StartCoroutine(DeathSequence(dropCollision));

        IEnumerator DeathSequence(bool dropCollision)
        {
            var sparkColor = new Color(1f, 0.85f, 0.45f);
            DuelSpark.Burst(Chest, Vector3.zero, sparkColor, 30, 1.3f * Size);
            Flash(Color.white, 0.12f);
            // 0.6秒:体のあちこちから火花がはじける
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(0.1f);
                Vector3 at = transform.position + Vector3.up * game.mechHeight * Size * Random.Range(0.3f, 0.9f)
                           + Random.insideUnitSphere * Radius;
                DuelSpark.Burst(at, Random.onUnitSphere, sparkColor, 8, 0.9f * Size);
                Flash(new Color(1f, 0.6f, 0.3f), 0.05f);
            }
            // 爆発して黒焦げ
            Exploded = true;   // ここでロックオンが外れる
            ProtoFx.Blast(Chest, 9f * Size, new Color(1f, 0.55f, 0.15f), 0.55f);
            ProtoFx.Blast(Chest, 4.5f * Size, new Color(1f, 0.95f, 0.7f), 0.25f);
            DuelSpark.Burst(Chest, Vector3.zero, new Color(1f, 0.7f, 0.3f), 45, 1.8f * Size);
            game.HitStop(0.08f);
            SetHoldTint(Charred);
            if (dropCollision) DropCollision();
        }

        // ───────── 機体全体の姿勢(モーションの上に重ねる) ─────────

        float bodyLean, bodyTwist, bodyLift, bodyRoll, jolt;

        /// <summary>
        /// 機体全体の傾き(前が正)・ひねり(右が正)・沈み/浮き(m)・横の傾き(右へ傾くのが正)を、rate の速さで目標へ寄せて見た目に反映する。
        /// 被弾の一瞬ののけぞり(Jolt)も足す。毎フレーム1回呼ぶ。
        /// </summary>
        protected void BodyOffset(float lean, float twist, float lift, float rate, float dt, float roll = 0f)
        {
            float a = 1f - Mathf.Exp(-rate * dt);
            bodyLean = Mathf.Lerp(bodyLean, lean, a);
            bodyTwist = Mathf.Lerp(bodyTwist, twist, a);
            bodyLift = Mathf.Lerp(bodyLift, lift, a);
            bodyRoll = Mathf.Lerp(bodyRoll, roll, a);
            jolt *= Mathf.Exp(-14f * dt);
            if (rig == null) return;
            rig.transform.localRotation = Quaternion.Euler(bodyLean + jolt, bodyTwist, -bodyRoll);
            rig.transform.localPosition = Vector3.up * bodyLift * Size;
        }

        /// <summary>当たった一瞬だけ、のけぞらせる(deg は負で後ろへ)。怯まない被弾でも当たったと分かるように。</summary>
        protected void Jolt(float deg) => jolt = deg;

        /// <summary>
        /// 怯みの姿勢(k は怯みの進み 0〜1):最初に大きくのけぞり、膝が折れて沈み、左右によろめきながら戻る。
        /// </summary>
        protected static void FlinchPose(float k, out float lean, out float twist, out float lift)
        {
            float back = 1f - k;
            lean = -26f * back;
            twist = 16f * Mathf.Sin(k * Mathf.PI * 3f) * back;
            lift = -1.1f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
        }
    }
}
