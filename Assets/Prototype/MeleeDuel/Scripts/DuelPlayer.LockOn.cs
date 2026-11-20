using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelPlayer のロックオン:オン・オフ・自動で選ぶ・切り替え(partial。本体と全体の説明は DuelPlayer.cs)
    public partial class DuelPlayer
    {
        // ───────── ロックオン ─────────
        // 作者「ロックオンのオンオフをを電脳斬禅と同じ作りで作ってください」:電脳斬禅の PlayerTargeting と同じ作り。
        //   ロックオンのボタン(Q・中クリック / 十字左)はオン・オフを切り替える(敵に付ける・外すのではなく、自動で狙うかどうかの切り替え)。
        //   オンの間:狙う敵がいなければ、画面に映り・遮られず・距離の内の敵を自動で選ぶ(画面の中央に近く、近い敵ほど先)。
        //     狙っている敵を倒したらすぐ次へ。遮られる・遠ざかるが lockLoseAfter 秒続いたら外して選び直す。
        //   オフの間:敵を狙わない(自動でも付かない)。切り替えのボタン(Tab / R3)を押すとオンに戻る。
        //   オン・オフの合図は DuelGame の照準枠(画面の縁寄りの四つの角。オンで外から締まり、オフで広がって消える)。
        // この試作で残したもの:自動で選ぶ距離(DuelStage.autoLockRange)とボタンで付ける距離(lockRange)の2段、
        // 銃の構えの間は自動で選ばない、ロックオン中の右スティック・マウスの左右の弾きでの切り替え(SwitchSide。照準を動かしている間は除く)。

        /// <summary>ロックオンできる距離(DuelStage の値。0 なら制限なし)。</summary>
        float LockRange => game.stage.lockRange;
        float AutoLockRange => game.stage.autoLockRange;

        /// <summary>やり直し(シーンの読み直し)でも、最後に切り替えたオン・オフを覚えておく(電脳斬禅はセーブデータに持つ)。Play を始めるたびに忘れる。</summary>
        static bool? lockOnMemory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLockOnMemory() => lockOnMemory = null;

        /// <summary>狙っている敵が見えない(遮られる・遠すぎる)まま続いている秒数。</summary>
        float lockLostFor;

        /// <summary>
        /// ロックオンしてよい敵か。作者「画面内で障害物に遮られず一定距離内ならロックオンし、次にロックオンするときも同様に条件です」:
        /// 画面に映っていること(margin はビューポートの端から内側へ空ける割合)、ロックオンできる距離の内側で、壁・床ごしでないこと。
        /// 自動で選ぶ相手・オンにしたときの相手・切り替え先のどれもこの条件で選ぶ。
        /// </summary>
        bool Lockable(DuelEnemy e, float margin = 0f)
        {
            if (e == null || e.Dead) return false;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 v = cam.WorldToViewportPoint(e.LockPoint);
                if (v.z <= 0f || v.x < margin || v.x > 1f - margin || v.y < margin || v.y > 1f - margin) return false;
            }
            float r = LockRange;
            if (r <= 0f) return true;
            return (e.transform.position - transform.position).sqrMagnitude <= r * r && DuelSenses.ClearSight(LockPoint, e.LockPoint);
        }

        /// <summary>ロックオンのオン・オフを切り替える(電脳斬禅の SetAutoLock)。オンにしたらその場で選び(ボタンなので lockRange まで)、オフにしたら外す。</summary>
        void SetLockOn(bool on)
        {
            if (LockOn == on) return;
            LockOn = on;
            lockOnMemory = on;
            lockLostFor = 0f;
            Target = on ? PickBest(LockRange) : null;
        }

        void UpdateLock()
        {
            // ロックオンのボタン:オン・オフを切り替える。切り替えのボタンは、オフならオンに戻す(電脳斬禅の Cycle)
            // 落ちている武装の分解を選んでいる間と、抜けた後に十字左を離すまでは、十字左では切り替えない(分解の「弾にする」と同じボタン。DuelArms.LockDpadTaken)
            bool turnedOn = false;
            bool toggle = Arms.LockDpadTaken ? ProtoInput.LockToggleNoDpad : ProtoInput.LockToggle;
            if (toggle) SetLockOn(!LockOn);
            else if (ProtoInput.SwitchTarget && !LockOn) { SetLockOn(true); turnedOn = true; }
            if (!LockOn)
            {
                Target = null;
                lockLostFor = 0f;
                stickArmed = false;
                mouseFlick = 0f;
                return;
            }
            // 狙っている敵が、壁・床ごし、またはロックオンできる距離の1.5倍より遠いまま lockLoseAfter 秒たったら外す。倒したらすぐ外す
            if (Target != null)
            {
                bool seen = !Target.Dead;
                if (seen && LockRange > 0f)
                    seen = (Target.transform.position - transform.position).sqrMagnitude <= LockRange * LockRange * 2.25f &&
                           DuelSenses.ClearSight(LockPoint, Target.LockPoint);
                lockLostFor = seen ? 0f : lockLostFor + Time.deltaTime;
                if (Target.Dead || lockLostFor >= lockLoseAfter) { Target = null; lockLostFor = 0f; }
            }
            // 狙う敵がいなければ自動で選ぶ(作者「気づいたかどうかじゃなくて画面に入っていて、距離も遠くないかで判定してください」:
            // 画面に映り・遮られず・autoLockRange の内。敵が気づいたかは見ない)。銃を構えている間は選ばない
            if (Target == null && !Arms.Aiming) Target = PickBest(AutoLockRange);
            if (ProtoInput.SwitchTarget && !turnedOn) Target = Target == null ? PickBest(LockRange) : NearestToView(Target);
            // 上の自動で選ぶのは、銃の構えの間はしない(撃っている最中に、相手が替わってカメラが回り出さないように。構えが終わってから選ぶ)。
            // ロックオン中:右スティックを左右へはじくと、その側の敵へ切り替える(一度戻すまで次は切り替えない)
            // (作者「ロックが吸い付いている時は右スティックでターゲットを変更できるようにしてください」)。
            // 照準を動かしている間(DuelArms.CursorActive。DuelArmsRules.cursorMovable がオンの構えの間)は、右スティック・マウスが
            // 画面の上の照準を動かす(カメラは回さない)ので切り替えない。照準を動かせない既定では、構えの間も切り替えられる
            if (Target == null || Arms.CursorActive) { stickArmed = false; mouseFlick = 0f; return; }
            float sx = ProtoInput.RightStick.x;
            if (Mathf.Abs(sx) < 0.3f) stickArmed = true;
            else if (stickArmed && Mathf.Abs(sx) > 0.75f) { stickArmed = false; SwitchSide(Mathf.Sign(sx)); }
            // マウス:左右に大きく振ると同じように切り替える
            mouseFlick = mouseFlick * Mathf.Exp(-6f * Time.unscaledDeltaTime) + ProtoInput.MouseDelta.x;
            if (Mathf.Abs(mouseFlick) > 160f) { SwitchSide(Mathf.Sign(mouseFlick)); mouseFlick = 0f; }
        }

        // 照準を動かす構え・ロックオンの始まりでスティックが倒れたままなら、一度戻すまで切り替えない(構えの終わりに勝手に切り替わらないように)
        bool stickArmed;
        float mouseFlick;

        /// <summary>今の敵から見て side(+1=右 / -1=左)の側にいる、向きの一番近い敵へ切り替える(いなければそのまま)。</summary>
        void SwitchSide(float side)
        {
            Vector3 toCur = Flat(Target.transform.position - transform.position);
            DuelEnemy best = null;
            float bestAngle = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == Target || !Lockable(e)) continue;
                float a = Vector3.SignedAngle(toCur, Flat(e.transform.position - transform.position), Vector3.up);
                if (a * side <= 0f) continue;
                if (Mathf.Abs(a) < bestAngle) { bestAngle = Mathf.Abs(a); best = e; }
            }
            if (best != null) Target = best;
        }

        /// <summary>
        /// 切り替え先:今の敵のほかで、画面の中央に一番近い(向きの角度が小さい)敵。角度が同じくらいなら近い敵。
        /// カメラで狙いをずらしてから押せば、その方の敵へ移れる。ほかにいなければ今の敵のまま。
        /// </summary>
        DuelEnemy NearestToView(DuelEnemy current)
        {
            var cam = Camera.main;
            if (cam == null) return Nearest(current);
            Vector3 o = cam.transform.position, f = cam.transform.forward;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == current || !Lockable(e)) continue;
                float score = Vector3.Angle(f, e.LockPoint - o) + (e.transform.position - transform.position).magnitude * 0.05f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best != null ? best : current;
        }

        /// <summary>一番近い敵(except は除く。ほかにいなければ except のまま)。</summary>
        DuelEnemy Nearest(DuelEnemy except = null)
        {
            DuelEnemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == except) continue;
                float d = (e.transform.position - transform.position).sqrMagnitude;
                if (d < bestD && Lockable(e)) { bestD = d; best = e; }
            }
            return best != null ? best : except != null && !except.Dead ? except : null;
        }

        /// <summary>
        /// 自動で狙う敵を選ぶ(電脳斬禅の PickBest)。条件:画面の端から lockPickScreenMargin より内側に映り、遮られず、range の内(0 なら距離を見ない。部屋)。
        /// 点数 = 画面の中央からのずれ × 10 + 距離 × lockPickDistanceWeight で、小さいほど先。いなければ null。
        /// 電脳斬禅の「こちらを狙っている敵を少し優先する」は入れない(作者「気づいたかどうかじゃなくて」)。
        /// </summary>
        DuelEnemy PickBest(float range)
        {
            var cam = Camera.main;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead) continue;
                float d = Vector3.Distance(e.transform.position, transform.position);
                if (range > 0f && d > range) continue;
                if (!Lockable(e, lockPickScreenMargin)) continue;
                float centerOff = 0f;
                if (cam != null)
                {
                    Vector3 v = cam.WorldToViewportPoint(e.LockPoint);
                    centerOff = Vector2.Distance(new Vector2(v.x, v.y), new Vector2(0.5f, 0.5f));   // 0(中央)〜0.7(角)
                }
                float score = centerOff * 10f + d * lockPickDistanceWeight;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }
    }
}
