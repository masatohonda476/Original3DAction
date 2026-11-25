using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelPlayer のロックオン:オン・オフ・自動で選ぶ・切り替え(partial。本体と全体の説明は DuelPlayer.cs)
    public partial class DuelPlayer
    {
        // ───────── ロックオン ─────────
        // 作者「ロックオンのオンオフをを電脳斬禅と同じ作りで作ってください」:電脳斬禅の PlayerTargeting と同じ作り。
        //   ロックオンのボタン(Q・中クリック / 十字左)はオン・オフを切り替える(敵に付ける・外すのではなく、自動で狙うかどうかの切り替え)。
        //   オンの間:狙う敵がいなければ、照準枠に体が掛かり・遮られず・距離の内の敵を自動で選ぶ(画面の中央に近く、近い敵ほど先)。
        //     狙っている敵を倒したら、爆発するまでは付けたままにし、爆発したら次へ(下の「倒した敵」)。遮られる・遠ざかるが lockLoseAfter 秒続いたら外して選び直す。
        //   オフの間:敵を狙わない(自動でも付かない)。切り替えのボタン(Tab / R3)を押すとオンに戻る。
        //   オン・オフの合図は DuelGame の照準枠(画面の縁寄りの四つの角。オンで外から締まり、オフで広がって消える)。
        // 右スティックを倒すと、その方向の敵へ切り替える。倒し続けている間は一定間隔で繰り返す。
        // 遠距離武器を構えている間は、右スティックとマウスでカメラを動かす。
        //
        // 付け始めは照準枠の中だけ(2026-11-21。作者「ロックオンで出てくるサイトの枠内に入った場合のみロックオン開始するようにする」
        //   「擬似的にエイムを表現するために追加します」→ 選んだのは「1-A,2-A」):
        //   枠 = DuelGame の照準枠が待機中に止まっている大きさ(DuelGame.LockSightTan。見えている枠と判定が同じ。出入りの動き・敵を囲んで縮む動きは入れない)。
        //   1-A:敵の胸の点ではなく、敵の体が枠に掛かれば付く(近くて大きく映る敵ほど付きやすい。DuelArms.BodyInCenterFrame=銃の照準の枠と同じ体の見え方)。
        //   2-A:枠を使うのは、自動で付くとき・オンにした瞬間・倒した(見失った)あとの次の敵・狙う敵がいないときの切り替えボタン(どれも PickBest)。
        //        ロックオン中の切り替え(右スティック SwitchInDirection・切り替えボタン NearestToView)は、画面内で見通せる敵から選ぶ。
        //   枠から出ても追い続ける。壁・床ごしになった状態が lockLoseAfter 秒続くと外れる。
        //
        // 距離は1つ(2026-11-21。距離の段の質問に、作者「4でロックオン距離の2倍にしてください」。選択肢 4 =「1つにそろえて長くする(枠で狙う分、40m より遠くまで付くようにする)」):
        //   前の 自動 40m(autoLockRange)/ オンにした瞬間・切り替え 130m(lockRange)の2段をやめ、DuelStage.lockOnRange(80m = 40m の2倍)だけにした。
        //   自動・オンにした瞬間・狙う敵がいないときの切り替えボタンは同じ距離。その場合は1.5倍より遠い状態が続くと外れる。
        //   捕捉中に対象を切り替える場合は、切り替え時もその後の維持も距離を問わない。
        //   距離は足元どうしの直線距離(付け始めの判定も、枠の右辺の目盛りの印も同じ測り方。LockCandidate・DuelGame.DrawLockGauge)。
        //
        // 倒した敵(2026-11-21。作者「撃破後も爆発するまではロックオンを残してください」):
        //   狙っている敵を倒しても、撃墜の演出で爆発する(DuelFighter.Exploded。倒してから約 0.6 秒)まではロックオンを外さない。爆発したフレームに外して次の敵を選ぶ。
        //   その間は遮られても・遠ざかっても外さない。カメラ・角の枠・自機の向き・剣の向かう先は、付けたままの相手(倒した敵)を追う。
        //   切り替え(右スティック・切り替えボタン)はその間もできる。倒した敵に新しく付くことはない(付け始め・切り替え先は生きている敵だけ)。
        //   弾・剣が倒した敵に当たらないのは今までどおり。

        /// <summary>ロックオンを付け始める距離(DuelStage.lockOnRange。0 なら制限なし)。</summary>
        float LockRange => game.stage.lockOnRange;

        /// <summary>
        /// 枠の右辺の距離の目盛りに印を出す敵(DuelGame.DrawLockGauge):距離のほかの付け始めの条件(体が照準枠に掛かる・遮られていない・生きている)を満たす敵のうち、
        /// 一番近い敵(作者が選んだ「一番近い敵」:「枠に掛かって遮られていない敵のうち、一番近い敵の距離を示す。『印が刻みに届いたら必ず付く』がいつも成り立つ。」)。
        /// 実際に付く敵は点数で選ぶので、この敵とは限らない(一番近い敵がロックオン距離に入れば、距離の内に付ける敵が必ずいるので、そのフレームにどれかに付く)。
        /// ロックオンがオンで狙う敵がいない間だけ。銃の構え中は出さない。
        /// なので印が出ている敵は、いつもロックオン距離より遠い。目盛りの上端のさらに LockCandidateBeyond 倍より遠い敵は見ない。
        /// </summary>
        public DuelEnemy LockCandidate { get; private set; }
        /// <summary>
        /// 印に出す敵を探す距離の上限 = 目盛りの上端(ロックオン距離 × DuelGame.LockGaugeFarMul)× これ(80m × 2 × 1.5 = 240m)。
        /// これより遠い敵には見通しのレイを飛ばさない(上端より遠い敵は上端に止めて薄く出すので、上端の少し先までは見る)。
        /// </summary>
        const float LockCandidateBeyond = 1.5f;
        /// <summary>LockCandidate までの距離(m。付け始めの判定と同じ測り方=足元どうしの直線距離)。</summary>
        public float LockCandidateDistance { get; private set; }

        /// <summary>やり直し(シーンの読み直し)でも、最後に切り替えたオン・オフを覚えておく(電脳斬禅はセーブデータに持つ)。Play を始めるたびに忘れる。</summary>
        static bool? lockOnMemory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLockOnMemory() => lockOnMemory = null;

        /// <summary>狙っている敵が見えない(遮られる・遠すぎる)まま続いている秒数。</summary>
        float lockLostFor;
        bool lockTargetWasSwitched;

        const float LockSwitchRepeatInterval = 0.3f;
        const float LockSwitchStickThreshold = 0.45f;
        const float LockSwitchStickReleaseThreshold = 0.25f;
        const float LockSwitchCardinalSlope = 0.41421356f;   // tan(22.5°)
        float nextLockSwitchAt;

        static bool InViewport(Camera cam, Vector3 v) =>
            v.z >= cam.nearClipPlane && v.z <= cam.farClipPlane && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;

        void SwitchLockTarget(DuelEnemy enemy)
        {
            if (enemy == null || enemy == Target) return;
            Target = enemy;
            lockTargetWasSwitched = true;
            lockLostFor = 0f;
        }

        /// <summary>
        /// ロックオンのオン・オフを切り替える(電脳斬禅の SetAutoLock)。オフにしたら外す。
        /// オンにしたときの相手は、同じフレームの UpdateLock の続きで選ぶ(照準枠に掛かっている、ロックオン距離の内の敵。銃の構えの間でも選ぶ。選ぶのは1フレームに1回)。
        /// </summary>
        void SetLockOn(bool on)
        {
            if (LockOn == on) return;
            LockOn = on;
            lockOnMemory = on;
            lockLostFor = 0f;
            lockTargetWasSwitched = false;
            nextLockSwitchAt = 0f;
            Target = null;
        }

        void UpdateLock()
        {
            // ロックオンのボタン:オン・オフを切り替える。切り替えのボタンは、オフならオンに戻す(電脳斬禅の Cycle)
            // 落ちている武装の選ぶ表示(付け先・分解)の間と閉じたフレームは、ロックオンのボタン(オン・オフ:十字左・Q・中クリック / 切り替え:R3・Tab)を
            // 読まない(DuelArms.ChoiceLocksInput。作者「ドロップした武装の操作中に移動とカメラ以外の操作ができないように」)。
            // 自動で選ぶ・見失って外す・倒した敵から次へ移るのは、選ぶ表示の間も今までどおり動く。
            // 選ぶ表示の間は右スティックでの対象変更も止める。
            bool wasOn = LockOn;
            bool choice = Arms.ChoiceLocksInput;
            bool toggle = !choice && ProtoInput.LockToggle;
            bool switchPressed = !choice && ProtoInput.SwitchTarget;
            if (toggle) SetLockOn(!LockOn);
            else if (switchPressed && !LockOn) SetLockOn(true);
            if (!LockOn)
            {
                Target = null;
                LockCandidate = null;
                LockCandidateDistance = 0f;
                lockLostFor = 0f;
                return;
            }
            bool justOn = !wasOn;   // このフレームにオンにした(ロックオンのボタンでも、切り替えのボタンでも)

            // 壁・床ごしの状態が lockLoseAfter 秒続くと外す。付け始めの相手には距離による解除もある。
            // 倒した敵は、爆発するまでは外さず(その間は遮られる・遠いも見ない)、爆発したら外す(作者「撃破後も爆発するまではロックオンを残してください」)
            if (Target != null)
            {
                bool seen = true;
                float range = LockRange;
                if (!Target.Dead && (range > 0f || lockTargetWasSwitched))
                {
                    bool withinRange = lockTargetWasSwitched ||
                        (Target.transform.position - transform.position).sqrMagnitude <= range * range * 2.25f;
                    seen = withinRange && DuelSenses.ClearSight(LockPoint, Target.LockPoint);
                }
                lockLostFor = seen ? 0f : lockLostFor + Time.deltaTime;
                if (Target.Exploded || lockLostFor >= lockLoseAfter) { Target = null; lockLostFor = 0f; }
            }
            // 付け始めは照準枠から選ぶ。遠距離の構え中は、明示的にオン・切り替えを押したときだけ選ぶ。
            if (Target == null)
            {
                if (!Arms.BlocksAutoLock || justOn || switchPressed) SearchLockTarget();
                else
                {
                    LockCandidate = null;
                    LockCandidateDistance = 0f;
                }
                nextLockSwitchAt = 0f;
                return;
            }

            LockCandidate = null;
            LockCandidateDistance = 0f;
            if (switchPressed && !justOn)
            {
                SwitchLockTarget(NearestToView(Target));
                nextLockSwitchAt = Time.unscaledTime + LockSwitchRepeatInterval;
                return;
            }
            if (Arms.FreeAim || choice)
            {
                nextLockSwitchAt = 0f;
                return;
            }
            UpdateDirectionalLockSwitch();
        }

        void UpdateDirectionalLockSwitch()
        {
            Vector2 stick = ProtoInput.RightStick;
            // 入力が境目で小さく揺れても、連続切り替えの待ち時間をリセットしない。
            float threshold = nextLockSwitchAt > 0f ? LockSwitchStickReleaseThreshold : LockSwitchStickThreshold;
            if (stick.sqrMagnitude < threshold * threshold)
            {
                nextLockSwitchAt = 0f;
                return;
            }
            float now = Time.unscaledTime;
            if (now < nextLockSwitchAt) return;
            nextLockSwitchAt = now + LockSwitchRepeatInterval;
            SwitchInDirection(stick.normalized);
        }

        void SearchLockTarget()
        {
            Target = PickBest(out var candidate, out var distance);
            lockTargetWasSwitched = false;
            // 構え中は武器のサイトを表示するため、距離の目盛りには候補の印を出さない。
            LockCandidate = Target == null && !Arms.BlocksAutoLock ? candidate : null;
            LockCandidateDistance = LockCandidate != null ? distance : 0f;
        }

        /// <summary>今の敵から見て、スティックを倒した方向の近い敵へ切り替える。候補がいなければ相手を保つ。</summary>
        void SwitchInDirection(Vector2 direction)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 origin = cam.WorldToViewportPoint(Target.LockPoint);
            if (origin.z <= 0f) origin = new Vector3(0.5f, 0.5f, 0f);

            // 軸に近い入力を揃え、左右は高さ、上下は横のずれにかかわらず、その側全体を候補にする。
            float x = Mathf.Abs(direction.x), y = Mathf.Abs(direction.y);
            if (y <= x * LockSwitchCardinalSlope) direction = new Vector2(Mathf.Sign(direction.x), 0f);
            else if (x <= y * LockSwitchCardinalSlope) direction = new Vector2(0f, Mathf.Sign(direction.y));
            bool diagonal = direction.x != 0f && direction.y != 0f;

            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == Target) continue;
                Vector3 view = cam.WorldToViewportPoint(e.LockPoint);
                if (!InViewport(cam, view)) continue;

                // 縦横を同じ画面上の長さで比べる。斜め入力はその方向から45度以内の候補を見る。
                Vector2 offset = new((view.x - origin.x) * cam.aspect, view.y - origin.y);
                float forward = Vector2.Dot(offset, direction);
                float sideways = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
                if (forward <= 0.005f || (diagonal && sideways > forward)) continue;

                // 入力方向に近く、画面上で隣り合う敵を優先する。
                float score = forward + 2f * sideways;
                if (score >= bestScore || !DuelSenses.ClearSight(LockPoint, e.LockPoint)) continue;
                bestScore = score;
                best = e;
            }
            SwitchLockTarget(best);
        }

        /// <summary>
        /// 切り替え先:今の敵のほかで、画面の中央に一番近い(向きの角度が小さい)敵。角度が同じくらいなら近い敵。
        /// カメラで狙いをずらしてから押せば、その方の敵へ移れる。ほかにいなければ今の敵のまま。
        /// </summary>
        DuelEnemy NearestToView(DuelEnemy current)
        {
            var cam = Camera.main;
            if (cam == null) return current;
            Vector3 o = cam.transform.position, f = cam.transform.forward;
            DuelEnemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e == current || !InViewport(cam, cam.WorldToViewportPoint(e.LockPoint))) continue;
                float score = Vector3.Angle(f, e.LockPoint - o) + (e.transform.position - transform.position).magnitude * 0.05f;
                if (score >= bestScore || !DuelSenses.ClearSight(LockPoint, e.LockPoint)) continue;
                bestScore = score;
                best = e;
            }
            return best != null ? best : current;
        }

        /// <summary>
        /// 付け始めの敵を選ぶ(電脳斬禅の PickBest。自動で付くとき・オンにした瞬間・倒した(見失った)あとの次の敵・狙う敵がいないときの切り替えボタン)。
        /// 条件:敵の体が照準枠(待機中に止まっている枠。DuelGame.LockSightTan)に掛かり、遮られず、ロックオン距離(LockRange)の内。
        ///   作者「ロックオンで出てくるサイトの枠内に入った場合のみロックオン開始する」「擬似的にエイムを表現するために追加します」。
        ///   体が掛かればよい(胸の点が枠の外・画面の外でもよい。近くて大きく映る敵ほど付きやすい)。前の「画面の端から lockPickScreenMargin より内側」はやめた。
        ///   カメラがないときは枠を見ない(前の「画面を見ない」と同じ扱い)。LockRange が 0 なら距離も見通しも見ない。
        /// 点数 = 画面の中央からのずれ(胸の少し上の点)× 10 + 距離 × lockPickDistanceWeight で、小さいほど先。いなければ null。
        /// 電脳斬禅の「こちらを狙っている敵を少し優先する」は入れない(作者「気づいたかどうかじゃなくて」)。
        /// nearest は、距離だけを見ないで同じ条件(体が枠に掛かる・遮られていない・生きている)を満たす一番近い敵(距離の目盛りの印に出す。LockCandidate。nearestDist はその距離)。
        ///   nearest がロックオン距離の内なら、返す敵は必ずいる(印が目盛りの刻みに届いたフレームに、どれかの敵に付く。点数で選ぶので nearest とは限らない)。
        ///   探すのは LockRange × DuelGame.LockGaugeFarMul × LockCandidateBeyond(240m)まで。それより遠い敵は見ない。
        /// 見通し(レイを飛ばす)は、今の一番近い敵より近い敵か、ロックオン距離の内で点数が今の一番より良い敵だけ調べる。
        ///   前のフレームの印の敵を最初に調べる(たいていそのまま一番近いので、それより遠い敵にはレイを飛ばさずに済む)。
        ///   レイの本数の目安:印の敵がいる間は 1 +(それより近くて遮られている、枠の中の敵の数)。枠の中に見通せる敵がいない間は、枠に掛かる 240m 以内の敵の数。
        /// </summary>
        DuelEnemy PickBest(out DuelEnemy nearest, out float nearestDist)
        {
            nearest = null;
            nearestDist = 0f;
            var cam = Camera.main;
            bool framed = game.LockSightTan(cam, out float frameW, out float frameH);
            float range = LockRange;
            float far = range > 0f ? range * DuelGame.LockGaugeFarMul * LockCandidateBeyond : float.MaxValue;
            DuelEnemy best = null;
            float bestScore = float.MaxValue, nearD = float.MaxValue;
            var list = game.Enemies;
            var prev = LockCandidate;
            for (int k = -1; k < list.Count; k++)   // -1 は前のフレームの印の敵(先に調べる。並びの中では飛ばす)
            {
                var e = k < 0 ? prev : list[k];
                if (e == null || e.Dead || (k >= 0 && e == prev)) continue;
                float d = Vector3.Distance(e.transform.position, transform.position);
                if (d > far) continue;
                bool inRange = range <= 0f || d <= range;
                bool nearer = d < nearD;
                if (!nearer && !inRange) continue;
                if (framed && !Arms.BodyInCenterFrame(cam, e, k, frameW, frameH)) continue;
                float score = float.MaxValue;
                if (inRange)
                {
                    float centerOff = 0f;
                    if (cam != null)
                    {
                        Vector3 v = cam.WorldToViewportPoint(e.LockPoint);
                        centerOff = Vector2.Distance(new Vector2(v.x, v.y), new Vector2(0.5f, 0.5f));   // 0(中央)〜0.7(角)
                    }
                    score = centerOff * 10f + d * lockPickDistanceWeight;
                }
                bool better = inRange && score < bestScore;
                if (!nearer && !better) continue;
                if (range > 0f && !DuelSenses.ClearSight(LockPoint, e.LockPoint)) continue;
                if (nearer) { nearD = d; nearest = e; }
                if (better) { bestScore = score; best = e; }
            }
            if (nearest != null) nearestDist = nearD;
            return best;
        }
    }
}
