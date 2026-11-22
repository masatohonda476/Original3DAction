using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelPlayer の回避と攻撃:回避・通常攻撃・強攻撃・回避斬り・致命攻撃・当たり(partial。本体と全体の説明は DuelPlayer.cs)
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

        /// <summary>攻撃が当たらない間:回避の出だしと、致命攻撃の踏み込み〜振り抜きの間(ほかの敵に割り込まれて止められないように)。</summary>
        bool Invincible => (Current == State.Dodge && stateT < dodgeInvincible) || FatalCommitted;

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
            if (TryFatal(now)) return true;   // 崩れた敵の近く(地上):ふつうの振りの代わりに致命攻撃(EN は使わないので、0以下でも出る)
            if (Overheated) return false;   // ENが0以下の間は攻撃できない(押したことは少しの間覚えておく)
            if (!Arms.CanMelee(pressHand)) { lightPressedAt = -10f; return false; }   // 押した後に剣を持ち替えた(素手でもなくなった)
            atkHand = pressHand;
            if (CanDrop) { StartDrop(); return true; }   // 成功しているジャンプ中なら落下攻撃(△ を押していても。DuelPlayer.Jump.cs)
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
            dropping = false;   // 落下攻撃なら、この後で StartDrop が立てる
            fatal = false;      // 致命攻撃なら、この後で StartFatal が立てる
            m = Armed(m, atkHand);
            if (m.cost > 0f)
            {
                Energy -= m.cost;   // 足りなければマイナスになり、その分だけ回復を待つ
                energyUsedAt = Time.time;   // EN を使わない技(致命攻撃)は、回復も待たせない
            }
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
            if (dropping) return UpdateDrop(dt);   // 落下攻撃は別の流れ(降下 → 着地で斬る → 着地の隙)
            if (FatalCommitted) return UpdateFatal(dt);   // 致命攻撃の踏み込みと振り抜き(取り消せない)。戻りは下のふつうの流れ
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
                    // 崩れた敵の近く(地上)なら、次の段の代わりに致命攻撃(EN が0以下でも出る)
                    if (stateT >= cur.ChainAt && TryFatal(now)) return Vector3.zero;
                    if (stateT >= cur.ChainAt && !Overheated && now - lightPressedAt <= attackBuffer && Arms.CanMelee(pressHand))
                    {
                        // 押した手で次を振る(両手に剣なら、R1/L1 を交互に押すと左右の剣で交互に振る。素手も R1/L1 で左右のパンチ)
                        bool sameHand = pressHand == atkHand;
                        // 両手の近接は R1/L1 で同じ武装(強攻撃の段は、ボタンが替わっても続ける)
                        bool sameBlade = sameHand || (SwordIn(atkHand) is { IsTwoHand: true } tw && SwordIn(pressHand) == tw);
                        atkHand = pressHand;
                        // 空中で振っている間に成功した(当たる瞬間に跳んでいた):次の一振りは落下攻撃
                        if (CanDrop) { StartDrop(); return Vector3.zero; }
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

        /// <summary>
        /// 振り抜いた瞬間:扇の中の敵に当てる。当たった敵がいたかを返す。
        /// 怯みゲージへの量には DuelGame.meleeStaggerMul が掛かる(ふつうの殴りではたまりにくい)。
        /// 落下攻撃を、このジャンプで攻撃を跳び越えた敵に当てたときだけ、倍率を掛けずに DuelJumpRules.dropSuccessStagger をためる。
        /// </summary>
        bool Strike()
        {
            bool any = false, big = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                if (TooFarVertically(e) && !DropOnTopOf(e)) continue;   // 上下に離れすぎていれば届かない(落下攻撃を敵の体の上で振ったときは別)
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (GapTo(e) > cur.reach) continue;
                if (to.magnitude > Radius + e.Radius + 0.5f && Vector3.Angle(transform.forward, to) > cur.arc) continue;
                bool wasBroken = e.Flinching;   // 崩れた敵を斬る(大ダメージ)
                bool jumped = dropping && JumpedOver(e);   // 攻撃を跳び越えた敵への落下攻撃(大きく崩す)
                float stagger = jumped ? game.jump.dropSuccessStagger : cur.stagger * game.meleeStaggerMul;
                e.TakeHit(cur.damage, stagger, Flat(to) * cur.knock, cur.flinch);
                bool broke = !wasBroken && e.Flinching;   // この一太刀で崩した
                // 当たった所(敵の胸の、こちらを向いた面)から火花。強攻撃は多く大きく。崩した・崩れた敵を斬った・跳び越えた敵への落下攻撃はさらに多く大きく
                Vector3 hitAt = e.Chest - Flat(to) * e.Radius;
                float fx = wasBroken || broke || jumped ? 1.6f : 1f;
                DuelSpark.Burst(hitAt, -Flat(to), new Color(1f, 0.88f, 0.55f), Mathf.RoundToInt((cur.heavy ? 26 : 14) * fx), (cur.heavy ? 1.4f : 1f) * fx);
                ProtoFx.Blast(hitAt, (cur.heavy ? 2f : 1.2f) * fx, Color.white, 0.08f);
                any = true;
                big |= wasBroken || broke || jumped;
            }
            // ヒットストップ(画面は揺らさない)。崩した・崩れた敵を斬ったときは少し長く
            if (any) game.HitStop((cur.heavy ? 0.12f : 0.06f) + (big ? 0.04f : 0f));
            // 素手のパンチ:当てるたびに自機の HP が減る(1振りにつき1回)
            if (any && cur.selfDamage > 0f) TakeRecoil(cur.selfDamage);
            return any;
        }

        // ───────── 致命攻撃 ─────────
        // 作者「致命攻撃も必要ですね」「崩れた敵の近くで R1」(試す版。数値は DuelGame の「致命攻撃」、技の時間・姿勢は DuelMoveSet.Fatal)。
        //   崩れている敵(怯みゲージが満ちて怯んでいる敵。「崩れた敵への近接 2 倍」と同じ条件)の近くで、地上で近接のボタン(△+ でも・回避の直後でも)を押すと、
        //   ふつうの振りの代わりに必ず出る。相手は、ロックオンの相手(崩れていて近ければ)→ 崩れている一番近い敵。
        //   相手へ踏み込んで一撃:ダメージは max(fatalMinDamage, 相手の最大 HP × fatalHpPart)。武装の威力の倍率・崩れた敵への 2 倍は掛からない。
        //   当てると相手の崩れは解ける(1 回の崩れに致命攻撃は 1 回)。EN は使わない(0 以下でも出る)。
        //   踏み込み〜振り抜きの間は無敵で、回避・ジャンプで取り消せない。出したら相手の崩れは振り抜くまで続く(出したのに届く前に立ち直ることはない)。
        //   戻りは、ふつうの近接と同じ(少し後から回避・ジャンプ・次の攻撃で取り消せる)。
        //   空中では出ない(ジャンプ中・飛行中に崩れた敵の近くで押すと、成功していれば落下攻撃、していなければ空中のふつうの振り)。

        /// <summary>今の攻撃が致命攻撃か(攻撃の状態を出たら false。Enter・StartAttack が戻す)と、その相手。</summary>
        bool fatal;
        DuelEnemy fatalAim;
        /// <summary>致命攻撃の踏み込み〜振り抜きの間(無敵。回避・ジャンプ・次の攻撃で取り消せない)。</summary>
        bool FatalCommitted => fatal && Current == State.Attack && phase < 2;
        /// <summary>致命攻撃で、相手の手前に空けるすき間(m)。</summary>
        const float FatalStopGap = 1.5f;

        /// <summary>致命攻撃を入れられる敵か:生きていて、崩れていて、近く(すき間が fatalRange 以内。上下に離れすぎていない)。</summary>
        bool FatalOk(DuelEnemy e) =>
            e != null && !e.Dead && e.Flinching && !e.invulnerable && !TooFarVertically(e) && GapTo(e) <= game.fatalRange;

        /// <summary>致命攻撃の相手:ロックオンの相手(入れられるなら)→ 入れられる一番近い敵。いなければ null。</summary>
        DuelEnemy FatalTarget()
        {
            if (game.fatalRange <= 0f) return null;
            if (FatalOk(Target)) return Target;
            DuelEnemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (!FatalOk(e)) continue;
                float d = GapTo(e);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>
        /// 近接のボタンを押してあり(受付時間の内)、地上で、崩れた敵が近くにいれば、致命攻撃を始める。
        /// 通常攻撃・強攻撃・回避斬り・次の段のどれよりも先に見る(EN が 0 以下でも出る)。
        /// </summary>
        bool TryFatal(float now)
        {
            if (now - lightPressedAt > attackBuffer || jumping || Airborne || !Arms.CanMelee(pressHand)) return false;
            var e = FatalTarget();
            if (e == null) return false;
            atkHand = pressHand;
            lightPressedAt = -10f;
            pressHeavy = false;
            combo = 0;
            StartAttack(DuelMoveSet.Fatal);
            fatal = true;
            fatalAim = e;
            Vector3 to = Flat(e.transform.position - transform.position);
            if (to != Vector3.zero) attackDir = to;
            e.HoldBroken(cur.windup + 0.1f);   // 振り抜くまでは崩れたままにする
            return true;
        }

        /// <summary>致命攻撃の踏み込み(phase 0)と振り抜き(phase 1)。踏み込みは、振りかぶりの 7 割の時間で相手の手前へ着くように詰める。</summary>
        Vector3 UpdateFatal(float dt)
        {
            if (phase == 1)
            {
                if (stateT >= cur.active) { phase = 2; stateT = 0f; }
                return Vector3.zero;
            }
            Vector3 vel = Vector3.zero;
            if (fatalAim != null && !fatalAim.Dead)
            {
                Vector3 to = Flat(fatalAim.transform.position - transform.position);
                if (to != Vector3.zero) attackDir = to;
                float remain = GapTo(fatalAim) - FatalStopGap;
                float left = Mathf.Max(cur.windup * 0.7f - stateT, Mathf.Max(dt, 0.001f));
                if (remain > 0f) vel = attackDir * Mathf.Min(remain / left, game.fatalLungeSpeed);
            }
            Face(attackDir, 20f, dt);
            if (stateT >= cur.windup) { phase = 1; stateT = 0f; StrikeFatal(); }
            return vel;
        }

        /// <summary>
        /// 致命攻撃を振り抜いた瞬間:相手がまだ崩れていて届けば、大ダメージを入れて崩れを解く。
        /// 手応えは、今ある仕組みを大きめに(火花・白い光・長めのヒットストップ・橙の数字。画面は揺らさない・カメラは動かさない)。
        /// </summary>
        void StrikeFatal()
        {
            var e = fatalAim;
            if (e == null || e.Dead || !e.Flinching || TooFarVertically(e) || GapTo(e) > cur.reach) return;   // 相手が倒れた・届かなかった:空振り
            Vector3 to = Flat(e.transform.position - transform.position);
            if (to == Vector3.zero) to = transform.forward;
            Vector3 hitAt = e.Chest - to * e.Radius;
            float dmg = Mathf.Max(game.fatalMinDamage, e.MaxHP * game.fatalHpPart);
            bool killed = e.TakeFatal(dmg, to * cur.knock);
            DuelSpark.Burst(hitAt, -to, new Color(1f, 0.82f, 0.4f), 44, 2f);
            ProtoFx.Blast(hitAt, 3.2f, Color.white, 0.12f);
            if (game.fatalHitStop > 0f) game.HitStop(game.fatalHitStop);
            OnFatalHit(e, killed);
        }

        /// <summary>
        /// 致命攻撃が当たった(1 回の致命攻撃につき 1 回。killed = この一撃で倒した)。
        /// 今は何もしない。後で「致命攻撃で弾が手に入る」仕組みを足すなら、ここに足す(作者「致命攻撃で弾薬を入手して銃でド派手に決めたい」。今回は入れない)。
        /// </summary>
        void OnFatalHit(DuelEnemy e, bool killed)
        {
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
