using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelPlayer のジャンプと落下攻撃(partial。本体と全体の説明は DuelPlayer.cs。数値と技の区分は DuelJumpRules = DuelGame.jump)
    // 「敵の攻撃をジャンプで越えて、落下攻撃で大きく崩す」を試す版(作者「やってみてください」。形は仮)。
    //   ジャンプ中:地上で ✗ を押して踏み切ってから、着地・被弾・飛行に移る(頂点を過ぎても ✗ を押している)までの間。
    //     敵の体の上に乗っただけでは終わらない(水平に押し出されて滑り落ち、地面に着いたら終わる)。
    //   踏み切れるとき:地上で ✗ を押していて、ふつう・ガード・回避の間か、近接を取り消せる間(回避と同じ決まり:振りかぶりの間と、振り抜いて少し後から。CanTakeOff)。
    //     怯み・ガードの硬直・EN 切れなどで踏み切れなかったときは、✗ を押したまま踏み切れるようになったフレームに踏み切る(地上からゆっくり浮くことはない)。
    //   成功:ジャンプ中に、跳べる技(下段)が「水平の間合いは当たるのに、高さで外れた」(DuelEnemy.DoHit → OnJumpedOver)。そのジャンプが終わるまで有効。
    //   落下攻撃:ジャンプ中に近接のボタン(踏み切った直後でも)。成功していなくても出せるが、怯みが大きくたまるのは、跳び越えた敵に当てたときだけ。
    //     落下攻撃の間(降下・振り・着地の隙)は跳べず・飛べず、その間に押していた ✗ は一度離すまで効かない(DuelPlayer.Update の flyNeedRelease)。
    public partial class DuelPlayer
    {
        // ───────── ジャンプ ─────────

        bool jumping;
        /// <summary>このジャンプで攻撃を跳び越えた敵(成功)。ジャンプが終わったら空にする。</summary>
        readonly List<DuelEnemy> jumpedOver = new();

        /// <summary>ジャンプ中(地上で ✗ を押して踏み切ってから、着地・被弾・飛行に移るまで)。敵の跳べる技(下段)は、この間だけジャンプの高さで外れる。</summary>
        public bool Jumping => jumping;
        /// <summary>このジャンプで敵の攻撃を跳び越えた(成功)。着地・被弾・飛行に移るまで有効。</summary>
        public bool JumpSucceeded => jumping && jumpedOver.Count > 0;
        /// <summary>ロックオンのカメラの向きを固定しておく間(成功した瞬間から、そのジャンプが終わるまで。DuelCamera が見る)。</summary>
        public bool JumpCameraHold => JumpSucceeded;
        /// <summary>ジャンプで上がっている途中(噴射炎を出す・飛行にしない)。</summary>
        bool JumpRising => jumping && VerticalSpeed > 0f;

        /// <summary>ジャンプ中に敵の体の上に乗った・重なったとき、このフレームに足す水平の押し出し(滑り落とす)。Update が Move の前に足す。</summary>
        Vector3 slideOff;
        /// <summary>このジャンプで、敵の体の上にいた合計の秒数。MaxOnEnemy を超えたら押し出しをやめて、乗ったまま着地にする(壁ぎわで押し出せないときの保険)。</summary>
        float onEnemyFor;
        const float MaxOnEnemy = 1f;

        /// <summary>
        /// ✗ で踏み切れる状態か(地上にいること・EN は呼ぶ側が見る)。ふつう・ガード・回避の間はいつでも。
        /// 近接の間は、回避(□)で取り消せるのと同じ間だけ(UpdateAttack の TryDodge と同じ判定):振りかぶりの間と、振り抜いて dodgeLockAfterSwing 秒たってから。
        /// 当たり判定が出ている振り抜きの間・落下攻撃の間(降下・振り・着地の隙)・怯み・ガードの硬直・ガード崩れの間は踏み切れない。
        /// </summary>
        bool CanTakeOff => Current switch
        {
            State.Free or State.Guard or State.Dodge => true,
            State.Attack => !dropping && (phase == 0 || (phase == 2 && stateT >= dodgeLockAfterSwing)),
            _ => false,
        };

        /// <summary>
        /// ジャンプの1フレーム(飛行を決めるより前に呼ぶ)。flyHeld は「✗ を押している」(落下攻撃の間に押していた分は、一度離すまで false)。
        /// 地上で ✗ を押していて踏み切れる(CanTakeOff・EN が 0 より多い)なら踏み切る。押した瞬間でなくてもよい
        /// (怯み・ガードの硬直・EN 切れ・振り抜きの間に押して、押したまま踏み切れるようになったフレームにも踏み切る)。近接の途中なら、その振りを取り消す。
        /// 戻り値は「このフレームは ✗ で飛んでよいか」:
        ///   地上では飛ばない(跳ぶだけ。jumpSpeed が 0 のときだけ今までの上昇)。上がっている間・✗ を離して落ちている間・落下攻撃の間・敵の体の上にいる間も飛ばない。
        ///   頂点を過ぎても ✗ を押していれば、ジャンプを終えて今までの飛行に移る(成功もそこで消える)。空中で押した ✗ は今までどおり飛行。
        /// </summary>
        bool UpdateJump(float now, bool flyHeld)
        {
            var j = game.jump;
            slideOff = Vector3.zero;
            if (dropping) return false;   // 落下攻撃の間(降下・振り・着地の隙):跳ばない・飛ばない。着地は落下攻撃の側で見る(UpdateDrop)
            if (jumping)
            {
                if (VerticalSpeed > 0f)
                {
                    // 上がっている途中:天井に頭が当たったら、そこで上昇をやめて落ちる(張り付かない)
                    if ((cc.collisionFlags & CollisionFlags.Above) != 0) StopRising();
                    return false;
                }
                var below = onEnemyFor < MaxOnEnemy && j.slideOffSpeed > 0f ? EnemyBelow() : null;
                if (below != null)
                {
                    // 敵の体の上に乗った・乗りそう:水平に押し出して滑り落とす。地面に着くまではジャンプ中のまま(乗っただけで成功は消えない)
                    onEnemyFor += Time.deltaTime;
                    slideOff = AwayFrom(below) * j.slideOffSpeed;
                    return false;
                }
                if (!Airborne) EndJump();   // 着地(✗ を押したままなら、すぐ下でまた踏み切る)
                else if (flyHeld && Energy > 0f && Current is State.Free or State.Guard or State.Attack or State.Dodge) { EndJump(); return true; }   // 頂点を過ぎても押し続けている:飛行へ
                else return false;          // ✗ を離して落ちている途中
            }
            // 空中(上へ動いている間も):今までどおり ✗ で飛ぶ。jumpSpeed が 0 ならジャンプなし(地上からも今までの上昇)
            if (j.jumpSpeed <= 0f || Airborne || VerticalSpeed > 0f) return true;
            if (flyHeld && Energy > 0f && CanTakeOff)
            {
                if (Current == State.Attack)
                {
                    // 近接を取り消して跳ぶ(回避での取り消しと同じ。払った EN は戻らない)。
                    // ただし攻撃のボタンとほぼ同時(振りかぶりの出だし heavyChordWindow 秒)なら、落下攻撃として出し直す:その振りの EN を戻し、押したことを覚え直す
                    if (phase == 0 && stateT <= heavyChordWindow && j.dropSpeed > 0f && Arms.CanMelee(atkHand))
                    {
                        Energy += cur.cost;
                        lightPressedAt = now;
                        pressHand = atkHand;
                        pressHeavy = false;
                    }
                    combo = 0;
                    Enter(State.Free);
                }
                jumping = true;
                jumpedOver.Clear();
                onEnemyFor = 0f;
                Energy -= j.jumpCost * EnCostMul;   // 足りなければマイナスになり、その分だけ回復を待つ(回避と同じ)
                energyUsedAt = now;
                SetVerticalSpeed(j.jumpSpeed);
            }
            return false;   // 地上では飛ばない(前は、踏み切れない間に ✗ を押し続けると、跳ばずにゆっくり浮いた)
        }

        /// <summary>
        /// 自機が体の上にいる敵(水平に重なっていて、足もとが敵の体の上半分〜頭の少し上)。いなければ null。
        /// 機体どうしは当たり判定で重ならないので、水平に重なっていれば敵の上にいる。倒して爆発した後の残骸は当たり判定がないので数えない。
        /// </summary>
        DuelEnemy EnemyBelow()
        {
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Exploded) continue;
                Vector3 d = transform.position - e.transform.position;
                float top = game.mechHeight * e.Size;
                if (d.y < top * 0.5f || d.y > top + 1.5f) continue;
                d.y = 0f;
                if (d.magnitude < Radius + e.Radius + 0.2f) return e;
            }
            return null;
        }

        /// <summary>
        /// 敵の体の上から滑り落とす分を、このフレームの速さに足す(Update が Move の前に呼ぶ。空中の慣性 airVel には入れない)。
        /// 押し出す向きと逆(敵の中心へ向かう分)の速さは消す(スティックを敵の方へ倒していても、頭の上に居座れない)。
        /// </summary>
        Vector3 WithSlideOff(Vector3 vel)
        {
            if (slideOff == Vector3.zero) return vel;
            Vector3 away = slideOff.normalized;
            float inward = -Vector3.Dot(vel, away);
            if (inward > 0f) vel += away * inward;
            return vel + slideOff;
        }

        /// <summary>その敵から離れる水平の向き(真上にいれば、自機の背中側)。</summary>
        Vector3 AwayFrom(DuelEnemy e)
        {
            Vector3 away = Flat(transform.position - e.transform.position);
            return away != Vector3.zero ? away : -transform.forward;
        }

        /// <summary>ジャンプを終える(着地・被弾・飛行に移った・撃墜・落下攻撃の着地)。成功もここで消える(カメラの固定も解ける)。</summary>
        void EndJump()
        {
            jumping = false;
            jumpedOver.Clear();
        }

        /// <summary>
        /// 敵の跳べる技を、ジャンプの高さで外した(DuelEnemy.DoHit から。水平の間合いは当たる範囲だった)。
        /// 合図は、足もとから上へ散る青白い火花・自機が一瞬光る・短いヒットストップ(画面は揺らさない。音はない)。同じ敵の続きの段では合図をくり返さない。
        /// </summary>
        public void OnJumpedOver(DuelEnemy e)
        {
            if (!jumping || Dead || e == null || jumpedOver.Contains(e)) return;
            jumpedOver.Add(e);
            var c = new Color(0.6f, 1f, 0.95f);
            DuelSpark.Burst(transform.position + Vector3.up * 0.5f, Vector3.up, c, 16, 1.1f);
            Flash(c, 0.15f);
            if (game.jump.successHitStop > 0f) game.HitStop(game.jump.successHitStop);
        }

        /// <summary>このジャンプで攻撃を跳び越えた敵か(落下攻撃を当てたとき、怯みを大きくためる相手)。</summary>
        bool JumpedOver(DuelEnemy e) => jumping && jumpedOver.Contains(e);

        /// <summary>跳び越えた敵のうち、生きている一番近い敵(落下攻撃が向かう先)。いなければ null。</summary>
        DuelEnemy NearestJumpedOver()
        {
            DuelEnemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in jumpedOver)
            {
                if (e == null || e.Dead) continue;
                float d = GapTo(e);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ───────── 落下攻撃 ─────────

        /// <summary>今の攻撃が落下攻撃か(攻撃の状態を出たら false。Enter・StartAttack が戻す)。</summary>
        bool dropping;
        /// <summary>落下攻撃が向かう相手と、相手へ向かって降りるか(出したときに dropRange の内にいた)。</summary>
        DuelEnemy dropAim;
        bool dropHoming;

        /// <summary>落下攻撃で降りている途中(着地して振るまで)。横の速さは慣性を通さずそのまま使う。</summary>
        bool DropDiving => dropping && Current == State.Attack && phase == 0;
        /// <summary>
        /// 近接のボタンが落下攻撃になるか(ジャンプ中。踏み切った直後でまだ地面のすぐ上でも:✗ と攻撃をほぼ同時に押しても落下攻撃になる)。
        /// 飛行中(ジャンプ中でない空中)は今までどおりの振り。
        /// </summary>
        bool CanDrop => jumping && game.jump.dropSpeed > 0f;

        /// <summary>
        /// 落下攻撃を、敵の体の上に乗ったまま振った(押し出しきれずに保険の時間切れで振ったときなど)。そのときは高さの差で外さない(Strike)。
        /// 自機が敵の足もとより上で、頭の少し上までにいて、水平にほぼ重なっている。
        /// </summary>
        bool DropOnTopOf(DuelEnemy e)
        {
            if (!dropping) return false;
            float dy = transform.position.y - e.transform.position.y;
            return dy > 0f && dy <= game.mechHeight * e.Size + 2f && GapTo(e) <= 0.5f;
        }

        /// <summary>素手なら素手の落下攻撃(当てると自機の HP が減る)。武装の倍率は、ふつうの技と同じく Armed が掛ける。</summary>
        DuelSwing DropOf() => Unarmed ? DuelMoveSet.BareDrop : DuelMoveSet.Drop;

        /// <summary>
        /// 落下攻撃を始める(atkHand の手で)。向かう先は、跳び越えた敵(成功していれば。ロックオンの相手より先)→ ロックオンの相手 → 正面の近い敵。
        /// △ を押していても同じ技(ジャンプ中に強攻撃はない)。
        /// </summary>
        void StartDrop()
        {
            lightPressedAt = -10f;
            pressHeavy = false;
            combo = 0;
            StartAttack(DropOf());
            dropping = true;
            dropAim = NearestJumpedOver() ?? (Target != null ? Target : SoftAim());
            if (dropAim != null)
            {
                Vector3 to = Flat(dropAim.transform.position - transform.position);
                if (to != Vector3.zero) attackDir = to;
            }
            if (attackDir == Vector3.zero) attackDir = transform.forward;
            dropHoming = dropAim != null && GapTo(dropAim) <= game.jump.dropRange;
        }

        /// <summary>
        /// 落下攻撃の1フレーム(UpdateAttack から)。
        ///   降下(phase 0):相手の手前(dropStopGap)へまっすぐ向かう線の上を dropSpeed で降りる(下向きは dropMinFall より遅くしない)。
        ///     相手に近すぎる(すき間が dropStopGap より小さい)ときは、後ろへ下がりながら降りる。
        ///     敵の体の上にいる間(真上から出した・降りる途中で乗った)は、降りずに水平に外へ出てから降りる(頭の上には着地しない)。
        ///     相手が dropRange より遠い・いないときは、向いている方の斜め下へ。
        ///     着地したら(振りかぶりの時間 cur.windup は必ず待つ)、その瞬間に斬る。着地は、敵の体の上ではなく、地面・足場に足が着いたこと。
        ///   着地の隙(phase 2):当てたら dropRecoverHit、外したら dropRecoverMiss 秒。回避・次の攻撃・ジャンプで取り消せない。
        /// 降りている途中は回避で取り消せない。被弾して怯めば止まる(ふつうの攻撃と同じ)。
        /// </summary>
        Vector3 UpdateDrop(float dt)
        {
            var j = game.jump;
            switch (phase)
            {
                case 0:
                {
                    var below = EnemyBelow();
                    // 着地:地面・足場に足が着いた(敵の体の上は着地にしない。踏み切ったフレームは、まだ上向きの速さなので着地にしない)
                    bool landed = below == null && !Airborne && VerticalSpeed <= 0f;
                    if ((landed && stateT >= cur.windup) || stateT >= j.dropMaxTime)
                    {
                        phase = 1;
                        stateT = 0f;
                        bool hit = Strike();   // 成功しているかはここで見る(この後でジャンプを終える)
                        cur.recover = hit ? j.dropRecoverHit : j.dropRecoverMiss;
                        EndJump();
                        airVel = Vector3.zero;   // 降りてきた勢いはここで止める(着地しないまま振ったときも流れない)
                        return Vector3.zero;
                    }
                    if (landed) return Vector3.zero;   // 低い所から出した:振りかぶりが終わるまで、その場で待つ
                    Vector3 flat;
                    float fall;
                    bool aimed = dropAim != null && !dropAim.Dead;
                    if (aimed && (dropHoming || below == dropAim))
                    {
                        Vector3 to = Flat(dropAim.transform.position - transform.position);
                        if (to != Vector3.zero) attackDir = to;   // 向くのは相手の方(下がるときも)
                    }
                    float step = Mathf.Max(dt, 0.001f);
                    if (below != null)
                    {
                        // 敵の体の上:降りずに、水平に外へ出る(狙った相手なら手前の dropStopGap まで、ほかの敵なら体の外まで)
                        Vector3 off = transform.position - below.transform.position;
                        off.y = 0f;
                        float need = Radius + below.Radius + (below == dropAim ? j.dropStopGap : 0.3f) - off.magnitude;
                        flat = AwayFrom(below) * Mathf.Min(j.dropSpeed, Mathf.Max(0f, need) / step);
                        fall = 0f;
                    }
                    else if (dropHoming && aimed)
                    {
                        // 相手の手前(dropStopGap)の地面へ。近すぎれば(need が負)後ろへ下がる。行き過ぎない
                        float need = GapTo(dropAim) - j.dropStopGap;
                        float remain = Mathf.Abs(need);
                        float alt = Mathf.Max(0.1f, Altitude);
                        float len = Mathf.Sqrt(remain * remain + alt * alt);
                        fall = Mathf.Max(j.dropMinFall, j.dropSpeed * alt / len);
                        flat = (need >= 0f ? attackDir : -attackDir) * Mathf.Min(j.dropSpeed * remain / len, remain / step);
                    }
                    else
                    {
                        flat = attackDir * (j.dropSpeed * 0.5f);
                        fall = Mathf.Max(j.dropMinFall, j.dropSpeed * 0.85f);
                    }
                    Face(attackDir, 20f, dt);
                    SetVerticalSpeed(-fall);
                    return flat;
                }
                case 1:
                    if (stateT >= cur.active) { phase = 2; stateT = 0f; }
                    break;
                case 2:
                    if (stateT >= cur.recover) { combo = 0; Enter(State.Free); }
                    break;
            }
            return Vector3.zero;
        }
    }
}
