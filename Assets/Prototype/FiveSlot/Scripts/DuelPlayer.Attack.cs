using HuntProto;
using UnityEngine;

namespace PrototypeFiveSlot
{
    // DuelPlayer の回避と攻撃:回避・通常攻撃・強攻撃・回避斬り・当たり(partial。本体と全体の説明は DuelPlayer.cs)
    public partial class DuelPlayer
    {
        // ───────── 回避 ─────────

        bool TryDodge(float now, Vector3 worldMove)
        {
            if (now - dodgePressedAt > attackBuffer || now - dodgeEndAt < dodgeCooldown) return false;
            if (Overheated) return false;   // ENが0以下の間は避けられない(押したことは少しの間覚えておく)
            dodgePressedAt = -10f;
            Energy -= dodgeCost * EnCostMul;   // 足りなければマイナスになり、その分だけ回復を待つ
            energyUsedAt = now;
            dodgeDir = worldMove.sqrMagnitude > 0.01f ? worldMove.normalized : -transform.forward;
            Enter(State.Dodge);
            return true;
        }

        bool Invincible => Current == State.Dodge && stateT < dodgeInvincible;

        // ───────── 攻撃 ─────────

        /// <summary>その手(0=右 / 1=左)の剣(剣でなければ null)。</summary>
        DuelWeapon SwordIn(int hand)
        {
            var w = Arms.Hand(hand);
            return w != null && w.IsBlade ? w : null;   // 両手の近接は R1 でも L1 でも振る
        }

        /// <summary>
        /// その手(ボタン)の剣の技の表(剣がなければ素の剣の表)。両手の近接は、R1 が連撃、L1 が別の技(回避の後の L1 も別の技)。
        /// 強攻撃(△+R1/L1)はどちらのボタンでも同じ表。
        /// 素手(Unarmed)のときは、どれも素手の表のパンチ(素の剣の技に化けないように。回避の後もふつうのパンチ)。
        /// </summary>
        DuelSwing[] LightOf(int hand) => Unarmed ? DuelMoveSet.Bare.light : (SwordIn(hand) ?? DuelWeapon.BaseSword).LightFor(hand);
        DuelSwing[] HeavyOf(int hand) => Unarmed ? DuelMoveSet.Bare.light : (SwordIn(hand) ?? DuelWeapon.BaseSword).HeavyMoves;
        DuelSwing DodgeOf(int hand) => Unarmed ? DuelMoveSet.Bare.light[0] : (SwordIn(hand) ?? DuelWeapon.BaseSword).DodgeFor(hand);

        /// <summary>
        /// 素手(両手持ちの素手):右手・左手が両方とも空(DuelArms.Unarmed)。R1 で右手、L1 で左手(左右反転)のパンチ。
        /// 武装ではないので倍率は掛からず(Armed は剣がなければそのまま返す)、強い別の技もない(△+R1/L1・回避の直後でも同じパンチ)。
        /// </summary>
        bool Unarmed => Arms.Unarmed;
        /// <summary>△+攻撃で強攻撃を出すか。素手に強攻撃はないので、△を押していてもふつうのパンチ。</summary>
        bool HeavyPressed => pressHeavy && !Unarmed;

        bool TryAttack(float now)
        {
            if (now - lightPressedAt > attackBuffer) return false;
            if (Overheated) return false;   // ENが0以下の間は攻撃できない(押したことは少しの間覚えておく)
            if (!Arms.CanMelee(pressHand)) { lightPressedAt = -10f; return false; }   // 押した後に剣を持ち替えた(素手でもなくなった)
            atkHand = pressHand;
            if (HeavyPressed) { StartHeavy(0); return true; }   // △+攻撃は回避の直後でも強攻撃
            // 回避が終わってすぐなら回避斬り(素手は同じパンチ)
            if (now - dodgeEndAt <= dodgeSlashWindow) { StartDodgeSlash(); return true; }
            combo = 1;
            StartLight(LightOf(atkHand)[0]);
            return true;
        }

        void StartLight(DuelSwing m)
        {
            lightPressedAt = -10f;
            StartAttack(m);
        }

        /// <summary>強攻撃の step 段目を始める(表の長さで回す)。この後の通常攻撃は連撃の1段目から。</summary>
        void StartHeavy(int step)
        {
            lightPressedAt = -10f;
            pressHeavy = false;
            var h = HeavyOf(atkHand);
            heavyStep = step % h.Length;
            combo = 0;
            StartAttack(h[heavyStep]);
        }

        void StartDodgeSlash()
        {
            lightPressedAt = -10f;
            combo = 1;   // 次は連撃の2段目へつながる
            StartAttack(DodgeOf(atkHand));
        }

        /// <summary>
        /// 同時押しのずれを許す:通常攻撃の振りかぶりの出だし(heavyChordWindow 秒)に△が押されたら、強攻撃の1段目に切り替える
        /// (足りないENを足して払う)。
        /// </summary>
        bool TryChord()
        {
            if (!cur.chargeable || stateT > heavyChordWindow || !ProtoInput.AimHeld || Overheated) return false;
            if (Time.time - Arms.ReloadChordAt <= heavyChordWindow) return false;   // その△は銃のリロードに使った(△+撃つボタン)
            if (SwordIn(atkHand) == null) return false;   // 振っている手に剣がない(素手のパンチも。素の剣の強攻撃に化けないように)
            float poseNow = Mathf.Lerp(cur.pose0, cur.pose1, Mathf.Clamp01(stateT / cur.windup));
            var h = Armed(HeavyOf(atkHand)[0], atkHand);
            Energy -= h.cost - cur.cost;
            energyUsedAt = Time.time;
            h.pose0 = Mathf.Min(poseNow, h.pose1);
            cur = h;
            heavyStep = 0;
            stateT = 0f;
            combo = 0;
            return true;
        }

        /// <summary>
        /// その手の剣の倍率を掛けた技:威力・怯ませ・届く範囲・吹き飛ばし・扇の広さ(片側180°まで)、振りの速さ(振りかぶり・振り抜き・戻りが短くなる)。
        /// 踏み込み・ENの量・姿勢は変えない(振り方の違いは技の表で出す)。
        /// </summary>
        DuelSwing Armed(DuelSwing m, int hand)
        {
            var w = SwordIn(hand);
            if (w == null) return m;
            m.damage *= w.damageMul;
            m.stagger *= w.staggerMul;
            m.reach *= w.reachMul;
            m.knock *= w.knockMul;
            m.arc = Mathf.Min(180f, m.arc + w.arcAdd);
            float sp = Mathf.Max(0.3f, w.speedMul);
            m.windup /= sp;
            m.active /= sp;
            m.recover /= sp;
            return m;
        }

        void StartAttack(DuelSwing m)
        {
            // 左手の片手の剣・素手の左のパンチは左右反転して振る(両手の近接は L1 の技も右手のクリップのまま、両手で振る)
            atkMirror = atkHand == 1 && SwordIn(1) is not { IsTwoHand: true };
            m = Armed(m, atkHand);
            Energy -= m.cost;   // 足りなければマイナスになり、その分だけ回復を待つ
            energyUsedAt = Time.time;
            cur = m;
            phase = 0;
            Enter(State.Attack);
            // 狙い:ロックオン中はその敵、なければ正面の近い敵、いなければ今の向き
            var aim = Target != null ? Target : SoftAim();
            attackDir = aim != null ? Flat(aim.transform.position - transform.position) : transform.forward;
        }

        DuelEnemy SoftAim()
        {
            DuelEnemy best = null;
            float bestD = 20f;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float d = GapTo(e);
                if (d < bestD && Vector3.Angle(transform.forward, e.transform.position - transform.position) < 100f) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>攻撃中の1フレーム。振りかぶりの間は少し踏み込み、振り抜いた瞬間に当たりを調べる。</summary>
        Vector3 UpdateAttack(float dt, float now, Vector3 worldMove)
        {
            Vector3 vel = Vector3.zero;
            switch (phase)
            {
                case 0:
                    // 振りかぶり:相手の方へ向き直りながら、前半だけ踏み込む(相手に密着していれば踏み込まない)
                    if (Target != null) attackDir = Flat(Target.transform.position - transform.position);
                    Face(attackDir, 20f, dt);
                    var near = Target != null ? Target : SoftAim();
                    float lungeTime = cur.windup * LungePart;
                    if (stateT < lungeTime && (near == null || GapTo(near) > 1.5f)) vel = attackDir * (cur.lunge / Mathf.Max(0.05f, lungeTime));
                    // 攻撃とほぼ同時に△を押していたら強攻撃へ
                    TryChord();
                    // 振りかぶりの間は回避で取り消せる(当たり判定が出ている振り抜きの間だけは取り消せない)
                    if (TryDodge(now, worldMove)) return Vector3.zero;
                    if (stateT >= cur.windup) { phase = 1; stateT = 0f; Strike(); }
                    break;
                case 1:
                    if (stateT >= cur.active) { phase = 2; stateT = 0f; }
                    break;
                case 2:
                    // 戻り:振り抜いてから dodgeLockAfterSwing 秒たったら回避で取り消せる。少し戻ったら次の段・強攻撃へつなげる
                    if (stateT >= dodgeLockAfterSwing && TryDodge(now, worldMove)) return Vector3.zero;
                    if (stateT >= cur.ChainAt && !Overheated && now - lightPressedAt <= attackBuffer && Arms.CanMelee(pressHand))
                    {
                        // 押した手で次を振る(両手に剣なら、R1/L1 を交互に押すと左右の剣で交互に振る。素手も R1/L1 で左右のパンチ)
                        bool sameHand = pressHand == atkHand;
                        // 両手の近接は R1/L1 で同じ武装(強攻撃の段は、ボタンが替わっても続ける)
                        bool sameBlade = sameHand || (SwordIn(atkHand) is { IsTwoHand: true } tw && SwordIn(pressHand) == tw);
                        atkHand = pressHand;
                        // △+攻撃:強攻撃。同じ剣の強攻撃の後なら次の段、それ以外は1段目
                        if (HeavyPressed) { StartHeavy(cur.heavy && sameBlade ? heavyStep + 1 : 0); return Vector3.zero; }
                        // 攻撃:連撃の表を順に(何回でも続く)。強攻撃の後・手が替わったら、その剣の1段目から
                        // (両手の近接は R1 の連撃と L1 の技が別の表なので、ボタンが替われば替えた側の1段目から)
                        var light = LightOf(atkHand);
                        if (!sameHand) combo = 0;
                        combo = combo % light.Length + 1;
                        StartLight(light[combo - 1]);
                        return Vector3.zero;
                    }
                    if (stateT >= cur.recover) { combo = 0; Enter(State.Free); }
                    break;
            }
            return vel;
        }

        /// <summary>振り抜いた瞬間:扇の中の敵に当てる。</summary>
        void Strike()
        {
            bool any = false, big = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if (TooFarVertically(e)) continue;   // 上下に離れすぎていれば届かない
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (GapTo(e) > cur.reach) continue;
                if (to.magnitude > Radius + e.Radius + 0.5f && Vector3.Angle(transform.forward, to) > cur.arc) continue;
                bool wasBroken = e.Flinching;   // 崩れた敵を斬る(大ダメージ)
                e.TakeHit(cur.damage, cur.stagger, Flat(to) * cur.knock, cur.flinch);
                bool broke = !wasBroken && e.Flinching;   // この一太刀で崩した
                // 当たった所(敵の胸の、こちらを向いた面)から火花。強攻撃は多く大きく。崩した・崩れた敵を斬ったときはさらに多く大きく
                Vector3 hitAt = e.Chest - Flat(to) * e.Radius;
                float fx = wasBroken || broke ? 1.6f : 1f;
                DuelSpark.Burst(hitAt, -Flat(to), new Color(1f, 0.88f, 0.55f), Mathf.RoundToInt((cur.heavy ? 26 : 14) * fx), (cur.heavy ? 1.4f : 1f) * fx);
                ProtoFx.Blast(hitAt, (cur.heavy ? 2f : 1.2f) * fx, Color.white, 0.08f);
                any = true;
                big |= wasBroken || broke;
            }
            // ヒットストップ(画面は揺らさない)。崩した・崩れた敵を斬ったときは少し長く
            if (any) game.HitStop((cur.heavy ? 0.12f : 0.06f) + (big ? 0.04f : 0f));
            // 素手のパンチ:当てるたびに自機の HP が減る(1振りにつき1回)
            if (any && cur.selfDamage > 0f) TakeRecoil(cur.selfDamage);
        }

        /// <summary>
        /// 自分の攻撃を当てた反動で自機の HP が減る(素手のパンチ。作者「ヒットでHPが2づつ減る」「HPが減るのは自機の話です」)。
        /// 被弾ではないので、怯み・押し返し・赤い光・ヒットストップはなく、回避の無敵やガードも関係ない。数字だけ自機の胸に出す。
        /// HP が 0 になれば、ふつうに撃墜される。
        /// </summary>
        void TakeRecoil(float dmg)
        {
            if (Dead) return;
            HP -= dmg;
            ProtoDamageText.Show(Chest, dmg);
            if (HP <= 0f) Die();
        }
    }
}
