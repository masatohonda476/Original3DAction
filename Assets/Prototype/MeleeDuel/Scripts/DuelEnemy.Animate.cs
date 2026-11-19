using HuntProto;
using UnityEngine;

namespace MeleeDuel
{
    // DuelEnemy の見た目:技ごとの予備動作の姿勢・怯み・歩き(partial。本体と全体の説明は DuelEnemy.cs)
    public partial class DuelEnemy
    {
        // ───────── 見た目 ─────────

        void Animate(float dt, Vector3 vel)
        {
            const string clip = MechRig.Attack;
            const float R = MechRig.AttackRaised, S = MechRig.AttackStruck, E = MechRig.AttackEnd;
            float k = Mathf.Clamp01(stateT / Mathf.Max(0.01f, stateDur));
            // 機体全体の傾き(前が正)・ひねり(右が正)・浮き。技ごとに予備動作の形を変える
            float tLean = 0f, tTwist = 0f, tLift = 0f, rate = 12f;
            switch (Current)
            {
                case State.Windup:
                    switch (skill)
                    {
                        case Skill.Slam:
                            // 剣を早めに掲げきり、のけぞりながら浮き上がって溜める
                            Pose(clip, R * Mathf.Clamp01(k * 1.6f), 20f);
                            tLean = -16f * k;
                            tLift = 2f * k;
                            break;
                        case Skill.Thrust:
                            // 低く前かがみに構える(後ろへ下がるのは Update で)
                            Pose(MechRig.Dash, 0f, 12f);
                            tLean = 20f * Mathf.Clamp01(k * 2f);
                            tLift = -0.7f * k;
                            break;
                        case Skill.AntiAir:
                            // 深く沈み込み、上を見上げるようにのけぞって溜める
                            Pose(MechRig.Dash, 0f, 12f);
                            tLean = -24f * Mathf.Clamp01(k * 1.5f);
                            tLift = -1.8f * k;
                            break;
                        case Skill.Flurry:
                            if (flurryIndex == 0)
                            {
                                // 三段斬り:剣を正面に半ば掲げたまま、左右に揺れてリズムを刻む。最後に右へひねって1段目へ
                                Pose(clip, R * 0.6f, 20f);
                                tTwist = Mathf.Lerp(12f * Mathf.Sin(k * Mathf.PI * 4f), 30f, Mathf.Clamp01((k - 0.7f) / 0.3f));
                                tLift = -1f * k;
                            }
                            else if (flurryIndex == 1)
                            {
                                // 2段目:剣を振り上げ直しながら左へひねる
                                Pose(clip, Mathf.Lerp(S, R, k), 25f);
                                tTwist = Mathf.Lerp(-30f, -35f, k);
                                tLean = 8f;
                            }
                            else
                            {
                                // 3段目:剣を頭上へ高く掲げ、のけぞる
                                Pose(clip, Mathf.Lerp(S, R, k), 25f);
                                tTwist = Mathf.Lerp(30f, 0f, k);
                                tLean = -12f * k;
                            }
                            rate = 20f;
                            break;
                        case Skill.WideSweep:
                            // 大薙ぎ:深く沈み、上半身を左へ大きくひねって剣を担ぐ(普通の横薙ぎは右へひねる)
                            Pose(clip, R * Mathf.Clamp01(k * 1.4f), 20f);
                            tTwist = -70f * k;
                            tLean = 12f * k;
                            tLift = -2.2f * k;
                            break;
                        case Skill.Charge:
                            // 突進:剣を担いで深く前かがみになり、じりじり下がる(後ろへ下がるのは Update で)
                            Pose(clip, R * Mathf.Clamp01(k * 2f), 20f);
                            tTwist = 45f * k;
                            tLean = 28f * Mathf.Clamp01(k * 2f);
                            tLift = -1.6f * k;
                            break;
                        case Skill.LeapSlam:
                            // 跳躍叩きつけ:剣を掲げたまま、膝を深く折ってしゃがみ込む
                            Pose(clip, R * Mathf.Clamp01(k * 1.5f), 20f);
                            tLean = 18f * k;
                            tLift = -3.2f * k;
                            break;
                        default:
                            // 剣を上げながら、上半身を右へひねって溜める
                            Pose(clip, R * k, 20f);
                            tTwist = 35f * k;
                            break;
                    }
                    break;
                case State.Active:
                    rate = 30f;
                    if (skill == Skill.Thrust) { Pose(MechRig.Dash, 0f, 20f); tLean = 24f; break; }
                    if (skill == Skill.AntiAir) { Pose(clip, Mathf.Lerp(R, S, k), 25f); tLean = -32f; break; }   // 上へ斬り上げながら跳ぶ
                    if (skill == Skill.Charge) { Pose(clip, hitDone ? S : R, 25f); tLean = 30f; tTwist = hitDone ? -20f : 45f; break; }   // 担いだまま突っ込む
                    if (skill == Skill.Flurry)
                    {
                        // 右→左 / 左→右 / 縦の振り下ろし
                        Pose(clip, Mathf.Lerp(R, S, k), 25f);
                        if (flurryIndex == 0) tTwist = Mathf.Lerp(30f, -30f, k);
                        else if (flurryIndex == 1) tTwist = Mathf.Lerp(-35f, 30f, k);
                        else tLean = Mathf.Lerp(-12f, 24f, k);
                        break;
                    }
                    if (skill == Skill.WideSweep) { Pose(clip, Mathf.Lerp(R, S, k), 25f); tTwist = Mathf.Lerp(-70f, 0f, k); tLift = Mathf.Lerp(-2.2f, -1f, k); break; }   // 機体ごと回る
                    if (skill == Skill.LeapSlam)
                    {
                        // 上がる間は剣を掲げてのけぞり、落ちる間は前へ振りかぶる
                        bool rising = stateT < B.leapAirTime * 0.5f;
                        Pose(clip, R, 20f);
                        tLean = rising ? -14f : 20f;
                        rate = 10f;
                        break;
                    }
                    Pose(clip, Mathf.Lerp(R, S, k), 25f);
                    if (skill == Skill.Slam) { tLean = Mathf.Lerp(-16f, 24f, k); tLift = Mathf.Lerp(2f, 0f, k); }
                    else tTwist = Mathf.Lerp(35f, -30f, k);
                    break;
                case State.Recover:
                    Pose(clip, Mathf.Lerp(S, E, k), 20f);
                    if (skill == Skill.AntiAir) tLean = -32f * (1f - k);
                    if (skill == Skill.WideSweep) tLift = -1f * (1f - k);
                    if (skill == Skill.Flurry) { tLean = 24f * (1f - k); tLift = -1.2f * (1f - k); }   // 振り下ろしたまま沈んで、ゆっくり起き上がる
                    if (skill == Skill.Charge)
                    {
                        // 壁にぶつかったら、のけぞって左右によろける
                        tLean = chargeWall ? -20f * (1f - k) : 24f * (1f - k);
                        tTwist = chargeWall ? 14f * Mathf.Sin(k * Mathf.PI * 3f) * (1f - k) : 0f;
                    }
                    if (skill == Skill.LeapSlam) { tLean = 28f * (1f - k); tLift = -2f * (1f - k); }   // 剣を叩きつけたまま、深く沈んで起き上がる
                    if (skill == Skill.Slam) tLean = 24f * (1f - k);
                    else if (skill == Skill.Sweep) tTwist = -30f * (1f - k);
                    break;
                case State.Flinch:
                    // 怯み:大きくのけぞり、沈んで、よろめきながら戻る
                    Pose(MechRig.Idle, 0f, 10f);
                    FlinchPose(k, out tLean, out tTwist, out tLift);
                    rate = 30f;
                    break;
                case State.Dead:
                    Pose(MechRig.Idle, 0f, 10f);
                    tLean = -30f;
                    tLift = -1.5f;
                    rate = 6f;
                    break;
                default:
                    float sp = new Vector3(vel.x, 0f, vel.z).magnitude;
                    if (dashing || Airborne) { Pose(MechRig.Dash, 0f, 12f); tLean = 8f; }   // ダッシュ中・空中
                    else if (sp > 0.5f)
                    {
                        stepPhase += sp * dt / 6f;
                        Pose(MechRig.Walk, stepPhase * 0.5f * (rig != null ? rig.Length(MechRig.Walk) : 1f), 10f);
                    }
                    else
                    {
                        // 待機は微動だにしない(作者「待機アニメーションは微動だにしないようにしてください」)。待機のクリップの最初の姿勢で止める
                        Pose(MechRig.Idle, 0f, 6f);
                    }
                    break;
            }
            BodyOffset(tLean, tTwist, tLift, rate, dt);
        }
    }
}
