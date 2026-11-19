using UnityEngine;

namespace MeleeDuel
{
    // DuelEnemy の発覚(ステージ):立ち番・見る・聞く・仲間への伝達・見失う(partial。本体と全体の説明は DuelEnemy.cs)
    public partial class DuelEnemy
    {
        // ───────── 発覚(ステージ) ─────────

        /// <summary>発覚の数値(null なら発覚の仕組みを使わず、最初から戦う=部屋のモード)。</summary>
        DuelSenses senses;
        /// <summary>自機に気づいて戦っている。</summary>
        public bool Engaged { get; private set; } = true;
        /// <summary>警戒度(0〜1)。1 で発覚。画面には出さない。</summary>
        public float Awareness { get; private set; }
        /// <summary>不審(気づいてはいないが、何かを見聞きした)。</summary>
        public bool Suspicious => !Engaged && Awareness >= 0.12f;
        float quietFor, attentionFor, unseenFor, sightCheckAt, stuckFor, highFor, holdEngagedUntil, keptAt = -10f;
        int heardStep;
        bool seesPlayer;
        Vector3 lastKnown, stuckFrom;

        /// <summary>ステージ:発覚の仕組みを使い、持ち場で立ち番を始める(まだ気づいていない)。</summary>
        public void UseSenses(DuelSenses s)
        {
            senses = s;
            Engaged = false;
            ResetAwareness();
            heardStep = player != null ? player.FootstepSequence : 0;   // 置く前の足音は聞かない
        }

        /// <summary>
        /// 自機に気づける状態か(生きていて、持ち場から leashRange の内側にいる)。外にいる間は見る・聞く・伝達を受けない
        /// (持ち場から離れすぎで発覚を切った次のフレームに、また気づいてしまう往復を防ぐ)。
        /// </summary>
        bool CanSense()
        {
            if (player == null || player.Dead) return false;
            if (ignoreLeashRange || senses == null || senses.leashRange <= 0f) return true;
            Vector3 ph = player.transform.position - home;
            ph.y = 0f;
            return ph.magnitude <= senses.leashRange;
        }

        /// <summary>目の高さ(機体の高さの85%)。</summary>
        Vector3 Eye => transform.position + Vector3.up * game.mechHeight * Size * 0.85f;

        void ResetAwareness()
        {
            Awareness = 0f;
            quietFor = attentionFor = unseenFor = 0f;
        }

        /// <summary>
        /// 発覚する(戦い始める)。alert なら、見通せる近くの仲間(まだ気づいていない敵)に伝える。伝えられた側からは連鎖しない。
        /// </summary>
        public void Engage(bool alert = true)
        {
            if (Engaged || Dead || player == null || player.Dead) return;
            Engaged = true;
            Awareness = 1f;
            unseenFor = 0f;
            lastKnown = player.transform.position;
            if (Current is State.Approach or State.Strafe) Enter(State.Approach);
            thinkAt = Mathf.Max(thinkAt, Time.time + Random.Range(0.6f, 1.2f));   // 気づいてから少しして技を考え始める(気づかれても自機のロックオンは付けない。DuelPlayer.UpdateLock)
            if (!alert || senses == null) return;
            foreach (var other in game.Enemies)
            {
                if (other == null || other == this || other.Dead || other.Engaged || other.senses == null) continue;
                if ((other.transform.position - transform.position).sqrMagnitude > senses.alertRadius * senses.alertRadius) continue;
                if (DuelSenses.ClearSight(Eye, other.Eye)) other.ReceiveAlert(lastKnown);
            }
        }

        /// <summary>仲間から伝えられた:自機が見えていれば発覚、見えていなければ不審から(その場所のほうを向く)。</summary>
        void ReceiveAlert(Vector3 knownPos)
        {
            if (!CanSense()) return;
            if (SightRate(out _) > 0f) { Engage(false); return; }
            Awareness = Mathf.Max(Awareness, senses.alertAwareness);
            lastKnown = knownPos;
            quietFor = 0f;
            attentionFor = senses.attentionTime;
        }

        /// <summary>発覚を切る(見失った・持ち場から離れすぎた):警戒度を0に戻し、持ち場へ歩いて帰る。</summary>
        void Disengage()
        {
            Engaged = false;
            ResetAwareness();
            highFor = 0f;
            chain = 0;
            Enter(State.Approach);
        }

        /// <summary>
        /// やり直し(セーブポイントから再開):HPを満タンにして持ち場へ戻し、発覚・警戒を切る。
        /// 発覚の仕組みを使わない敵(部屋のモード)は、発覚したままにする。
        /// </summary>
        public void ResetToHome()
        {
            if (Dead) return;
            Revive();
            Stagger = 0f;
            chain = 0;
            flyTime = 0f;
            Teleport(home, homeRotation);
            Enter(State.Approach);
            ResetAwareness();
            stuckFor = highFor = 0f;
            holdEngagedUntil = 0f;
            if (senses != null)
            {
                Engaged = false;
                heardStep = player != null ? player.FootstepSequence : 0;
            }
        }

        /// <summary>
        /// 見えたときの警戒度の溜まる速さ(1秒あたり)。0 なら見えていない。inFront は中央の視野で体の3点のうち2点以上が見えた(即発覚)。
        /// 視野:前方 viewAngle、近くは横 closeSideAngle まで。体の3点(腰・胸・頭)のうち見えた数で速さが変わる。
        /// </summary>
        float SightRate(out bool inFront)
        {
            inFront = false;
            if (!CanSense()) return 0f;
            var s = senses;
            Vector3 to = player.transform.position - transform.position;
            float distance = to.magnitude;
            if (distance >= s.sightRange) return 0f;
            to.y = 0f;
            float facing = to.sqrMagnitude > 0.001f ? Vector3.Dot(transform.forward, to.normalized) : 1f;
            bool touching = GapTo(player) < s.touchGap && !TooFarVertically(player);
            bool closeSide = distance <= s.closeSideRange && facing >= s.CloseSideCos;
            if (!touching && !closeSide && facing < s.ViewEdgeCos) return 0f;
            Vector3 eye = Eye, p = player.transform.position;
            float h = game.mechHeight * player.Size;
            int visible = 0;
            if (DuelSenses.ClearSight(eye, p + Vector3.up * h * 0.6f)) visible++;
            if (DuelSenses.ClearSight(eye, p + Vector3.up * h * 0.88f)) visible++;
            if (DuelSenses.ClearSight(eye, p + Vector3.up * h * 0.3f)) visible++;
            if (visible == 0) return 0f;
            // 即発覚は、体の3点のうち2点以上が見えるとき(物陰から頭だけ・足だけがのぞく程度なら、徐々に警戒する)
            inFront = facing >= s.ViewCenterCos && visible >= 2;
            if (touching) return 8f;
            if (closeSide) return s.closeSideRate * (visible / 3f);
            float central = Mathf.Lerp(s.edgeMul, 1f, Mathf.InverseLerp(s.ViewEdgeCos, s.ViewCenterCos, facing));
            float near = 1f - Mathf.Clamp01(distance / s.sightRange);
            return Mathf.Lerp(s.farRate, s.nearRate, near * near) * central * (visible / 3f);
        }

        /// <summary>自機の最新の足音が届いた強さ(0〜1)。壁・床ごしは届く範囲が縮む。</summary>
        float HearingStrength()
        {
            if (!CanSense() || player.FootstepRadius <= 0f || Time.time - player.FootstepTime > 0.8f) return 0f;
            Vector3 sound = player.FootstepPosition + Vector3.up;
            Vector3 ear = Eye;
            float distance = Vector3.Distance(ear, sound);
            float radius = player.FootstepRadius;
            if (distance >= radius) return 0f;   // 届かない足音は、遮蔽を調べる前に打ち切る
            if (!DuelSenses.ClearSight(ear, sound)) radius *= senses.muffled;
            return Mathf.Clamp01(1f - distance / radius);
        }

        /// <summary>まだ気づいていない間の、見る・聞くで警戒度を上げ下げする。1 になったら発覚。</summary>
        void TickAwareness(float dt)
        {
            if (!CanSense())
            {
                // 気づけない間(持ち場から離れすぎ)は、足音を聞いたことにもしない
                ResetAwareness();
                if (player != null) heardStep = player.FootstepSequence;
                return;
            }
            var s = senses;
            // 見えも聞こえもしない遠さなら、レイを撃たずに減らすだけ
            float far = Mathf.Max(s.sightRange, s.boostStep, s.flyStep);
            bool inReach = (player.transform.position - transform.position).sqrMagnitude < far * far;
            float sight = 0f;
            bool inFront = false;
            if (inReach) sight = SightRate(out inFront);
            if (inFront) { Engage(); return; }
            bool heard = false;
            if (heardStep != player.FootstepSequence)
            {
                heardStep = player.FootstepSequence;
                float strength = inReach ? HearingStrength() : 0f;
                if (strength > 0f)
                {
                    Awareness = Mathf.Max(Awareness, Mathf.Min(s.hearingCap, Awareness + strength * s.hearingGain));
                    lastKnown = player.FootstepPosition;
                    heard = true;
                }
            }
            if (sight > 0f)
            {
                Awareness = Mathf.Min(1f, Awareness + sight * dt);
                lastKnown = player.transform.position;
            }
            if (sight > 0f || heard) { quietFor = 0f; attentionFor = s.attentionTime; }
            else
            {
                quietFor += dt;
                attentionFor = Mathf.Max(0f, attentionFor - dt);
                if (quietFor > s.quietDelay) Awareness = Mathf.MoveTowards(Awareness, 0f, s.decayRate * dt);
            }
            if (Awareness >= 1f) Engage();
        }

        /// <summary>
        /// まだ気づいていない間の動き:不審ならその方向を向き、見通せて持ち場の範囲内ならゆっくり近づく(壁ごし・上下階・持ち場の外へは追わない)。
        /// 何もなければ持ち場へ歩いて戻り、決まった向きで立ち番する。
        /// </summary>
        Vector3 UpdateUnaware(float dt)
        {
            TickAwareness(dt);
            if (Engaged) return Vector3.zero;
            var s = senses;
            Vector3 vel = Vector3.zero;
            if (Suspicious && attentionFor > 0f)
            {
                Vector3 to = lastKnown - transform.position;
                to.y = 0f;
                Face(to, 5f, dt);
                Vector3 fromHome = lastKnown - home;
                fromHome.y = 0f;
                bool approach = Awareness >= s.investigateFrom && to.magnitude > Radius + 6f &&
                                Mathf.Abs(lastKnown.y - transform.position.y) < s.investigateMaxHeight &&
                                fromHome.magnitude < s.investigateRadius &&
                                DuelSenses.ClearSight(Eye, lastKnown + Vector3.up * game.mechHeight * 0.6f);
                if (approach) vel = to.normalized * approachSpeed * s.investigateSpeed;
                stuckFor = 0f;
                return vel;
            }
            Vector3 toHome = home - transform.position;
            float vertical = Mathf.Abs(toHome.y);
            toHome.y = 0f;
            if (toHome.magnitude > 2f || vertical > 4f)
            {
                Face(toHome, 6f, dt);
                vel = toHome.magnitude > 0.5f ? toHome.normalized * approachSpeed * 0.6f : Vector3.zero;
                // 持ち場へ帰れない(段差の下へ落ちた・何かに引っかかった)まま数秒たったら、持ち場へ戻す
                if (stuckFor <= 0f) stuckFrom = transform.position;
                stuckFor += dt;
                if (stuckFor > 4f)
                {
                    if ((transform.position - stuckFrom).magnitude < 3f) Teleport(home, homeRotation);
                    stuckFor = 0f;
                }
            }
            else
            {
                stuckFor = 0f;
                Face(homeRotation * Vector3.forward, 4f, dt);
            }
            return vel;
        }

        /// <summary>
        /// 発覚している間:自機が見えているかを数フレームおきに確かめ、見えない状態が loseSightTime 秒続いたら発覚を切る。
        /// 自機が持ち場から leashRange より離れても、上下に heightLoseGap 以上離れた状態が heightLoseTime 秒続いても(届かない)切る。
        /// 技の途中・斬られた直後(holdAfterHit 秒)・ボスの部屋にいる間は切らない。切ったら true。
        /// </summary>
        bool UpdateEngaged(float dt)
        {
            if (senses == null || player == null || player.Dead) return false;
            if (Time.time >= sightCheckAt)
            {
                sightCheckAt = Time.time + 0.15f;
                Vector3 p = player.transform.position;
                float h = game.mechHeight * player.Size;
                seesPlayer = (p - transform.position).sqrMagnitude < senses.keepSightRange * senses.keepSightRange &&
                             (DuelSenses.ClearSight(Eye, p + Vector3.up * h * 0.6f) || DuelSenses.ClearSight(Eye, p + Vector3.up * h * 0.88f));
            }
            if (seesPlayer) { unseenFor = 0f; lastKnown = player.transform.position; }
            else unseenFor += dt;
            if (Mathf.Abs(player.transform.position.y - transform.position.y) > senses.heightLoseGap) highFor += dt;
            else highFor = 0f;
            bool leashed = !CanSense();
            bool lost = unseenFor >= senses.loseSightTime || leashed || highFor >= senses.heightLoseTime;
            bool held = Time.time < holdEngagedUntil || Time.time - keptAt < 0.5f;
            if (!lost || held || !(Current is State.Approach or State.Strafe)) return false;
            Disengage();
            return true;
        }

        /// <summary>自機がいる間ずっと発覚させておく(ボスの部屋に入った)。見失いの時計も戻す。</summary>
        public void KeepEngaged()
        {
            if (!Engaged) Engage(false);
            unseenFor = 0f;
            keptAt = Time.time;
        }
    }
}
