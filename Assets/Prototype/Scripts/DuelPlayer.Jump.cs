using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelPlayer のジャンプと落下攻撃(partial。本体と全体の説明は DuelPlayer.cs。数値と技の区分は DuelJumpRules = DuelGame.jump)
    // 「敵の攻撃をジャンプで越えて、落下攻撃で大きく崩す」を試す版(作者「やってみてください」。形は仮)。
    //   ジャンプ中:地上で ✗ を押して踏み切ってから、着地・被弾・飛行に移る(頂点を過ぎても ✗ を押している)までの間。
    //   成功:ジャンプ中に、跳べる技(下段)が「水平の間合いは当たるのに、高さで外れた」(DuelEnemy.DoHit → OnJumpedOver)。そのジャンプが終わるまで有効。
    //   落下攻撃:ジャンプ中の空中で近接のボタン。成功していなくても出せるが、怯みが大きくたまるのは、跳び越えた敵に当てたときだけ。
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

        /// <summary>
        /// ジャンプの1フレーム(飛行を決めるより前に呼ぶ)。地上で ✗ を押した瞬間に踏み切る。戻り値は「このフレームは ✗ で飛んでよいか」:
        ///   上がっている間・✗ を離して落ちている間・落下攻撃で降りている間は飛ばない。
        ///   頂点を過ぎても ✗ を押していれば、ジャンプを終えて今までの飛行に移る(成功もそこで消える)。
        /// 踏み切れる状態は、今まで ✗ で上がれた状態と同じ(ふつう・ガード・攻撃・回避)。空中で押した ✗ は今までどおり飛行。
        /// </summary>
        bool UpdateJump(float now, bool flyHeld, bool flyPressed)
        {
            if (DropDiving) return false;   // 落下攻撃で降りている間:着地は落下攻撃の側で見る(UpdateDrop)。✗ を押していても飛ばない
            bool canFly = Current is State.Free or State.Guard or State.Attack or State.Dodge;
            if (jumping)
            {
                if (VerticalSpeed > 0f)
                {
                    // 上がっている途中:天井に頭が当たったら、そこで上昇をやめて落ちる(張り付かない)
                    if ((cc.collisionFlags & CollisionFlags.Above) != 0) StopRising();
                    return false;
                }
                if (!Airborne) EndJump();   // 着地(同じフレームに ✗ を押していれば、すぐ下でまた踏み切る)
                else if (flyHeld && Energy > 0f && canFly) { EndJump(); return true; }   // 頂点を過ぎても押し続けている:飛行へ
                else return false;          // ✗ を離して落ちている途中
            }
            if (flyPressed && game.jump.jumpSpeed > 0f && Energy > 0f && !Airborne && canFly)
            {
                jumping = true;
                jumpedOver.Clear();
                Energy -= game.jump.jumpCost * EnCostMul;   // 足りなければマイナスになり、その分だけ回復を待つ(回避と同じ)
                energyUsedAt = now;
                SetVerticalSpeed(game.jump.jumpSpeed);
                return false;
            }
            return true;
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

        /// <summary>落下攻撃で降りている途中(着地して振るまで)。この間は飛ばず、横の速さは慣性を通さずそのまま使う。</summary>
        bool DropDiving => dropping && Current == State.Attack && phase == 0;
        /// <summary>近接のボタンが落下攻撃になるか(ジャンプ中の空中)。飛行中(ジャンプ中でない空中)は今までどおりの振り。</summary>
        bool CanDrop => jumping && Airborne && game.jump.dropSpeed > 0f;

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
        ///     相手が dropRange より遠い・いないときは、向いている方の斜め下へ。着地したら(振りかぶりの時間 cur.windup は必ず待つ)、その瞬間に斬る。
        ///   着地の隙(phase 2):当てたら dropRecoverHit、外したら dropRecoverMiss 秒。回避・次の攻撃で取り消せない。
        /// 降りている途中は回避で取り消せない。被弾して怯めば止まる(ふつうの攻撃と同じ)。
        /// </summary>
        Vector3 UpdateDrop(float dt)
        {
            var j = game.jump;
            switch (phase)
            {
                case 0:
                {
                    bool landed = !Airborne;
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
                    if (dropHoming && dropAim != null && !dropAim.Dead)
                    {
                        Vector3 to = Flat(dropAim.transform.position - transform.position);
                        if (to != Vector3.zero) attackDir = to;
                        float remain = Mathf.Max(0f, GapTo(dropAim) - j.dropStopGap);
                        float alt = Mathf.Max(0.1f, Altitude);
                        float len = Mathf.Sqrt(remain * remain + alt * alt);
                        fall = Mathf.Max(j.dropMinFall, j.dropSpeed * alt / len);
                        // 相手の手前で止まる(行き過ぎない)
                        flat = attackDir * Mathf.Min(j.dropSpeed * remain / len, remain / Mathf.Max(dt, 0.001f));
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
