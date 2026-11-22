using HuntProto;
using UnityEngine;

namespace Prototype
{
    // DuelEnemy の技:選ぶ・予備動作・出ている間・当たり(partial。本体と全体の説明は DuelEnemy.cs)
    public partial class DuelEnemy
    {
        // ───────── 技を選ぶ ─────────

        void Think(float gap)
        {
            // 同時に攻撃している敵が多ければ、順番を待つ(回り込みながら少しして考え直す)
            if (!game.CanStartAttack(this)) { thinkAt = Time.time + Random.Range(0.3f, 0.6f); return; }
            // 持ち場のある敵は、自機が持ち場の近くへ来るまで技を出さない(段差の下へ突きで飛び降りてこない)
            if (leashRadius > 0f)
            {
                Vector3 ph = player.transform.position - home;
                ph.y = 0f;
                if (ph.magnitude > leashRadius + 6f) { thinkAt = Time.time + 0.3f; return; }
            }
            // 自機が段差の上・下にいて斬り合いが届かない(飛んでいない)ときは、技を出さない(届かない横薙ぎを振り続けて、同時に攻撃する枠をふさがない)
            if (!player.Airborne && TooFarVertically(player)) { thinkAt = Time.time + 0.3f; return; }
            thinkAt = Time.time + Random.Range(Kind.thinkMin, Kind.thinkMax);
            // 自機が頭上を飛んでいる:跳び上がり斬りで落とす(届かない高さ・選ばなかったときは見上げて待つ)
            float above = player.transform.position.y - transform.position.y;
            if (player.Airborne && above > game.mechHeight * Size * 0.8f)
            {
                if (CanFlyNow) return;   // 飛べる剣士は、飛び上がって高さを合わせてから斬る
                if (above <= Kind.antiAirHeight && gap <= 30f && Random.value < Kind.antiAirChance) StartSkill(Skill.AntiAir);
                return;
            }
            float r = Random.value;
            if (IsBoss) { ThinkBoss(gap, r); return; }
            // 自機と高さが大きく違う(段差の上と下)ときは突きを選ばない(台の上から突きで飛び降りない)
            bool thrustOk = Mathf.Abs(above) < ThrustMaxHeightGap;
            if (gap <= SweepReach)
            {
                if (!Kind.canSlam) StartSkill(r < 0.7f || !thrustOk ? Skill.Sweep : Skill.Thrust);
                else if (r < 0.5f) StartSkill(Skill.Sweep);
                else if (r < 0.8f) StartSkill(Airborne ? Skill.Sweep : Skill.Slam);   // 空中では叩きつけは使わない
                else StartSkill(thrustOk ? Skill.Thrust : Skill.Sweep);
            }
            else if (thrustOk && gap <= 26f && r < (Kind.canSlam ? 0.6f : 0.5f)) StartSkill(Skill.Thrust);
        }

        /// <summary>
        /// ボスの技選び。遠ければ突進か跳躍叩きつけで詰め、近ければ横薙ぎ・叩きつけ・大薙ぎを混ぜる。
        /// HPが半分を切ったら、近い間合いで三段斬りも混ぜる。
        /// 固有の技(大薙ぎ・突進・跳躍叩きつけ・三段斬り)は続けて同じものを出さない。
        /// </summary>
        void ThinkBoss(float gap, float r)
        {
            bool enraged = HP < MaxHP * B.flurryFromHP;
            bool flurryOk = enraged && gap <= B.flurryReach * Size;   // 三段斬りは1段目が届く間合いだけ
            Skill s;
            if (gap > SweepReach)
            {
                // 遠い:詰める技だけから選ぶ(前回と同じ固有の技・空中で使えない技は除く)。どれも使えなければ歩いて詰める
                bool thrustOk = gap <= 26f && Mathf.Abs(player.transform.position.y - transform.position.y) < ThrustMaxHeightGap;
                bool chargeOk = gap <= B.chargeDistance - 10f && lastSkill != Skill.Charge;
                bool leapOk = gap <= B.leapMaxDistance && lastSkill != Skill.LeapSlam && !Airborne;
                if (thrustOk && r < 0.2f) s = Skill.Thrust;
                else if (chargeOk && (!leapOk || r < 0.6f)) s = Skill.Charge;
                else if (leapOk) s = Skill.LeapSlam;
                else if (thrustOk) s = Skill.Thrust;
                else return;
                StartSkill(s);
                return;
            }
            if (flurryOk && Random.value < B.flurryChance) s = Skill.Flurry;
            else if (r < 0.25f) s = Skill.WideSweep;
            else if (r < 0.55f) s = Skill.Sweep;
            else if (r < 0.75f) s = Skill.Slam;
            else if (r < 0.87f) s = Skill.LeapSlam;
            else s = Skill.Thrust;
            // 近い間合いでの置き換えは横薙ぎ(届く)
            if (s == lastSkill && s is Skill.WideSweep or Skill.Charge or Skill.LeapSlam or Skill.Flurry) s = Skill.Sweep;
            if (Airborne && s is Skill.Slam or Skill.LeapSlam) s = Skill.Sweep;   // 空中では叩きつけ・跳躍は使わない
            StartSkill(s);
        }

        float WindupTime => skill switch
        {
            Skill.WideSweep => B.wideWindup,
            Skill.Charge => B.chargeWindup,
            Skill.LeapSlam => B.leapWindup,
            Skill.Flurry => flurryIndex == 0 ? B.flurryWindup : B.flurryStepWindup,
            _ => Kind.windupMul * skill switch
            {
                Skill.Sweep => chain > 0 ? 0.35f : 0.55f,
                Skill.Slam => 1.0f,
                Skill.AntiAir => 0.7f,
                _ => 0.6f,
            },
        };

        /// <summary>突きを選んでよい、自機との高さの差(m)。これより大きいと突きで段差を飛び降りてしまう。</summary>
        const float ThrustMaxHeightGap = 2.5f;

        /// <summary>予備動作の最後に向きを固める時間(そこからは向きを変えない=横へ避ければ外れる)。</summary>
        float CommitTime => skill == Skill.Charge ? B.chargeCommit : 0.15f;

        /// <summary>横薙ぎの届くすき間の距離(大きさに比例)。</summary>
        float SweepReach => 10f * Size;

        void StartSkill(Skill s)
        {
            skill = s;
            lastSkill = s;
            if (s != Skill.Sweep) chain = 0;
            hitDone = false;
            chargeWall = false;
            flurryIndex = 0;
            Enter(State.Windup, WindupTime);
        }

        Vector3 SlamCenter => transform.position + transform.forward * 7f * Size;
        float SlamRadius => 6.5f * Size;

        void UpdateWindup(float dt, Vector3 dir)
        {
            // 最後の0.15秒(突進は長め)は向きを変えない(そこで横へ避ければ外れる)
            if (stateT < stateDur - CommitTime) { Face(dir, 10f, dt); commitDir = transform.forward; }
            if (stateT < stateDur) return;
            commitDir = transform.forward;
            thrustStart = transform.position;
            if (skill == Skill.AntiAir) { Jump(); return; }
            if (skill == Skill.LeapSlam) { Leap(); return; }
            if (skill == Skill.Charge) { Enter(State.Active, B.chargeDistance / Mathf.Max(1f, B.chargeSpeed) + 0.2f); return; }
            if (skill == Skill.WideSweep) { spinFrom = transform.rotation; Enter(State.Active, B.wideSpin); return; }
            Enter(State.Active, skill == Skill.Thrust ? 0.3f : 0.12f);
            if (skill != Skill.Thrust) DoHit();
        }

        /// <summary>
        /// 跳び上がり斬り:予備動作の終わりの自機の胸の高さへ、ちょうど頂点で届くように跳ぶ(横も同じ時間で詰める)。
        /// 跳んだ後に自機が流れて位置がずれれば外れる。
        /// </summary>
        void Jump()
        {
            Vector3 target = player.Chest;
            float h = Mathf.Clamp(target.y - Chest.y, 2f, Kind.antiAirHeight);
            float vUp = Mathf.Sqrt(2f * Gravity * h);
            float tApex = vUp / Gravity;
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            jumpVel = Vector3.ClampMagnitude(flat / Mathf.Max(0.2f, tApex), 70f);
            SetVerticalSpeed(vUp);
            Enter(State.Active, tApex + 0.1f);
        }

        /// <summary>跳躍叩きつけ:予備動作の終わりに自機のいた所へ、leapAirTime 秒で落ちるように跳ぶ。</summary>
        void Leap()
        {
            Vector3 flat = player.transform.position - transform.position;
            flat.y = 0f;
            flat = Vector3.ClampMagnitude(flat, B.leapMaxDistance);
            float t = Mathf.Max(0.4f, B.leapAirTime);
            leapVel = flat / t;
            SetVerticalSpeed(Gravity * t * 0.5f);   // ちょうど t 秒で同じ高さへ落ちる
            Enter(State.Active, t + 1.5f);          // 着地で終わる(落ちきらないときの保険)
        }

        Vector3 leapVel;

        /// <summary>跳躍叩きつけの着地:落ちた所を叩き(直撃)、衝撃波を広げる。</summary>
        void Land()
        {
            DoHit();   // 直撃の範囲にいれば impactInRange が立つ
            Vector3 at = transform.position;
            ProtoFx.Blast(at + Vector3.up, B.leapImpactRadius * Size, new Color(1f, 0.6f, 0.3f), 0.35f);
            DuelSpark.Burst(at + Vector3.up, Vector3.up, new Color(1f, 0.75f, 0.45f), 40, 1.6f * Size);
            var wave = new DuelHit
            {
                damage = B.waveDamage * Kind.damageMul,
                stagger = B.waveStagger * Kind.playerStaggerMul, hurtTime = 0.3f, knock = B.waveKnock, from = at,
            };
            DuelShockwave.Spawn(at, B.leapImpactRadius * Size, B.waveMaxRadius, B.waveSpeed, B.waveWidth, wave, impactInRange);   // 直撃の範囲にいた相手には衝撃波は当てない
            Enter(State.Recover, B.leapRecover);
        }

        /// <summary>技が出ている間。突きは前へ一気に詰め、途中で自機に触れたら当てる。</summary>
        Vector3 UpdateActive(float dt)
        {
            Vector3 vel = Vector3.zero;
            if (skill == Skill.Thrust)
            {
                vel = commitDir * 80f;
                if (!hitDone && player != null && GapTo(player) < 3f) DoHit();
                if (Vector3.Distance(thrustStart, transform.position) > 28f) stateT = stateDur;
            }
            else if (skill == Skill.Sweep) vel = commitDir * 10f;
            else if (skill == Skill.AntiAir)
            {
                // 跳んでいる間、自機の胸に近づいたら当てる
                vel = jumpVel;
                if (!hitDone && player != null && Vector3.Distance(Chest, player.Chest) < Radius + player.Radius + 4f * Size) DoHit();
            }
            else if (skill == Skill.Charge)
            {
                // 突進:一直線に突っ込み、途中で自機に触れたら当てる。進めなくなったら(壁・自機に阻まれた)止まる
                vel = commitDir * B.chargeSpeed;
                dashing = true;
                if (!hitDone && player != null && GapTo(player) < 3f && !TooFarVertically(player)) DoHit();
                float movedLast = (transform.position - lastPos).magnitude;
                if (stateT > 0.12f && movedLast < B.chargeSpeed * lastDt * 0.3f) { chargeWall = !hitDone; stateT = stateDur; }
                if (Vector3.Distance(thrustStart, transform.position) > B.chargeDistance) stateT = stateDur;
            }
            else if (skill == Skill.WideSweep)
            {
                // 大薙ぎ:その場で1回転(終わりはゆっくり)。半分回ったところで全周に当てる
                float k = Mathf.Clamp01(stateT / Mathf.Max(0.01f, stateDur));
                float turn = 1f - (1f - k) * (1f - k);
                transform.rotation = spinFrom * Quaternion.Euler(0f, 360f * turn, 0f);
                if (!hitDone && k >= 0.5f) DoHit();
                if (k >= 1f) transform.rotation = spinFrom;
            }
            else if (skill == Skill.LeapSlam)
            {
                // 跳んでいる間は噴射炎。落ちたら叩きつける
                dashing = true;
                if ((stateT > 0.25f && !Airborne) || stateT >= stateDur) { Land(); return Vector3.zero; }
                return leapVel;
            }
            if (stateT >= stateDur && skill == Skill.Flurry && flurryIndex < 2)
            {
                // 三段斬り:次の段へ(踏み込みながら振りかぶる)
                flurryIndex++;
                hitDone = false;
                Enter(State.Windup, WindupTime);
                return vel;
            }
            if (stateT >= stateDur)
            {
                // 横薙ぎは半分の確率で2発目へ。それ以外は隙(叩きつけが一番長い)
                if (skill == Skill.Sweep && chain == 0 && Random.value < Kind.sweepChain) { chain = 1; StartSkill(Skill.Sweep); }
                else Enter(State.Recover, skill switch
                {
                    Skill.Slam => 1.1f, Skill.Thrust => 0.8f, Skill.AntiAir => 1.2f,   // 跳び上がり斬りは落ちてくる間も隙
                    Skill.WideSweep => B.wideRecover,
                    Skill.Flurry => B.flurryRecover,
                    Skill.Charge => chargeWall ? B.chargeWallRecover : B.chargeRecover,   // 壁にぶつかったら長くよろける
                    _ => 0.6f,
                });
            }
            return vel;
        }

        /// <summary>
        /// 技の当たりを調べて、当たっていれば自機に当てる。1 つの技で当たりを何度か調べるもの(横薙ぎの 2 発目・三段斬りの各段)は、その 1 回ずつ呼ばれる。
        /// 触れたら当たる技(突き・突進)は、初めて触れたフレームに 1 回だけ呼ばれる(それが「当たる瞬間」)。水平の間合い(inRange)は技ごと。
        ///   自機がジャンプ中(DuelPlayer.Jumping)のとき … 技の区分(DuelJumpRules.Jumpable)で決める。
        ///     跳べる技(下段):
        ///       当たる瞬間に跳んだ(踏み切ってから justWindow 秒以内。この敵にまだ成功していない)… 高さに関係なく当たらない。
        ///         水平の間合いが当たる範囲(=地上にいたら当たっていた)なら、自機の成功(DuelPlayer.OnJumpedOver)。遠くの空振りは成功にしない。
        ///       それより前から跳んでいた・この敵にはもう成功している(続きの段)… 自機の足もとが敵の足もとより clearHeight 以上高ければ外れる(成功ではない)。
        ///         越えていなければ当たる。
        ///     跳べない技(上段):highReach の高さまで届く(ジャンプでは外れない。成功にもならない)。
        ///   それ以外(地上・飛行中)… 今までどおり、高さの差が機体の高さを超えていれば届かない(TooFarVertically。対空は別)。
        /// 作者「当たる瞬間にジャンプがいいです」、受付の幅は「当たる 0.2 秒前まで」、早く跳んだときは「当たらないが成功でもない」。
        /// </summary>
        void DoHit()
        {
            hitDone = true;
            if (player == null || player.Dead) return;
            var h = new DuelHit { from = transform.position };
            bool inRange;
            switch (skill)
            {
                case Skill.Slam:
                    Vector3 c = SlamCenter;
                    Vector3 d = player.transform.position - c;
                    d.y = 0f;
                    inRange = d.magnitude - player.Radius <= SlamRadius;
                    h.damage = 30f; h.stagger = 25f; h.hurtTime = 0.45f; h.knock = 22f;
                    ProtoFx.Blast(c + Vector3.up, SlamRadius, new Color(1f, 0.5f, 0.3f), 0.3f);
                    break;
                case Skill.Thrust:
                    inRange = true;   // 触れた時点で呼ばれる
                    h.damage = 20f; h.stagger = 15f; h.hurtTime = 0.3f; h.knock = 18f;
                    break;
                case Skill.Flurry:
                {
                    Vector3 tf = player.transform.position - transform.position;
                    tf.y = 0f;
                    inRange = GapTo(player) <= B.flurryReach * Size && (tf.magnitude < Radius + player.Radius + 1f || Vector3.Angle(commitDir, tf) <= B.flurryArc);
                    bool last = flurryIndex >= 2;
                    h.damage = last ? B.flurryLastDamage : B.flurryDamage;
                    h.stagger = last ? B.flurryLastStagger : B.flurryStagger;
                    h.hurtTime = last ? 0.5f : 0.25f;
                    h.knock = last ? B.flurryLastKnock : B.flurryKnock;
                    break;
                }
                case Skill.WideSweep:
                    inRange = GapTo(player) <= B.wideReach * Size;   // 全周
                    h.damage = B.wideDamage; h.stagger = B.wideStagger; h.hurtTime = 0.5f; h.knock = B.wideKnock;
                    break;
                case Skill.Charge:
                    inRange = true;   // 触れた時点で呼ばれる
                    h.damage = B.chargeDamage; h.stagger = B.chargeStagger; h.hurtTime = 0.5f; h.knock = B.chargeKnock;
                    break;
                case Skill.LeapSlam:
                    Vector3 dl = player.transform.position - transform.position;
                    dl.y = 0f;
                    inRange = dl.magnitude - player.Radius <= B.leapImpactRadius * Size;
                    impactInRange = inRange && !TooFarVertically(player);
                    h.damage = B.leapDamage; h.stagger = B.leapStagger; h.hurtTime = 0.55f; h.knock = B.leapKnock;
                    break;
                case Skill.AntiAir:
                    inRange = true;   // 跳んでいる間に近づいた時点で呼ばれる
                    h.damage = 18f; h.stagger = 18f; h.hurtTime = 0.35f; h.knock = 12f;
                    break;
                default:
                    Vector3 to = player.transform.position - transform.position;
                    to.y = 0f;
                    inRange = GapTo(player) <= SweepReach && (to.magnitude < Radius + player.Radius + 1f || Vector3.Angle(commitDir, to) <= 90f);
                    h.damage = 12f; h.stagger = 10f; h.hurtTime = 0.2f; h.knock = 10f;
                    break;
            }
            h.damage *= Kind.damageMul;
            h.stagger *= Kind.playerStaggerMul;
            var jr = game.jump;
            float dy = player.transform.position.y - transform.position.y;
            bool low = jr.Jumpable(skill, flurryIndex);
            if (player.Jumping && low)
            {
                if (player.JumpAge <= jr.justWindow && !player.JumpedOver(this))
                {
                    // 当たる瞬間に跳んだ:高さに関係なく当たらない。水平の間合いが当たる範囲だったら成功
                    if (inRange) player.OnJumpedOver(this);
                    return;
                }
                if (dy >= jr.clearHeight) return;   // 早く跳んでいた・続きの段:高さで越えていれば当たらない(成功ではない)
            }
            bool reachesJumper = player.Jumping && !low && dy >= 0f && dy <= jr.highReach;   // 跳べない技は、ジャンプの高さでも届く
            if (skill != Skill.AntiAir && !reachesJumper && TooFarVertically(player)) inRange = false;   // 自機が高く飛んでいれば届かない(対空は別)
            if (inRange) player.ReceiveHit(h);
        }

        void EndAttack()
        {
            chain = 0;
            Enter(State.Strafe);
            thinkAt = Mathf.Max(thinkAt, Time.time + Random.Range(0.4f, 1f));
        }
    }
}
