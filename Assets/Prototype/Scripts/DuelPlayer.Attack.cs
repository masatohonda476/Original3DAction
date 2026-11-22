using System.Collections;
using System.Collections.Generic;
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

        // 強攻撃の追加効果(秒、倍率、距離、速さ)。仮
        const float GuardPointWindow = 0.2f; // 仮
        const float GuardPointDamageMul = 0.5f; // 仮
        const float WaveRange = 34f; // 仮
        const float WaveSpeed = 65f; // 仮
        const float WaveHalfWidth = 4.5f; // 仮
        const float WaveHalfHeight = 1f; // 仮
        const float WaveDamageMul = 0.7f; // 仮
        const float WaveStaggerMul = 0.75f; // 仮

        bool strongAttack;
        DuelHeavyEffect heavyEffect;

        bool MeleeInputReady(float now) => !Arms.RangedHeld && now - meleePressedAt <= attackBuffer;
        bool LightInputReady(float now) => !heavyPressed && MeleeInputReady(now);
        bool HeavyGuardPoint => Current == State.Attack && phase == 0 && stateT <= GuardPointWindow &&
            heavyEffect == DuelHeavyEffect.GuardPoint;

        bool TryAttack(float now)
        {
            if (!MeleeInputReady(now)) return false;
            if (TryFatal(now)) return true;   // EN が 0 以下でも出る
            if (Overheated) return false;
            var w = Arms.MeleeWeapon;
            if (heavyPressed)
            {
                combo = 0;
                StartAttack(w.HeavyMove, w.MeleePower, true, w.HeavyEffect);
            }
            else if (CanDrop) StartDrop();
            else if (now - dodgeEndAt <= dodgeSlashWindow) StartDodgeSlash();
            else
            {
                var light = w.MeleeMoves.light;
                combo = combo % light.Length + 1;
                StartAttack(light[combo - 1], w.MeleePower);
            }
            return true;
        }

        void StartDodgeSlash()
        {
            combo = 1;   // 次は連撃の2段目へつながる
            var w = Arms.MeleeWeapon;
            StartAttack(w.MeleeMoves.dodgeSlash, w.MeleePower);
        }

        internal void ResetMeleeCombo() => combo = 0;

        internal bool TryGetGripMotion(out DuelSwing swing, out int attackPhase, out float progress)
        {
            swing = cur;
            attackPhase = phase;
            progress = Mathf.Clamp01(stateT / Mathf.Max(0.001f, phase == 0 ? cur.windup : phase == 1 ? cur.active : cur.recover));
            return Current == State.Attack;
        }

        void StartAttack(DuelSwing m, float power = 1f, bool strong = false, DuelHeavyEffect effect = DuelHeavyEffect.None)
        {
            Arms.RememberGripForSwing();
            meleePressedAt = -10f;
            dropping = false;   // 落下攻撃なら、この後で StartDrop が立てる
            fatal = false;      // 致命攻撃なら、この後で TryFatal が立てる
            strongAttack = strong;
            heavyEffect = effect;
            m.damage *= power;
            SpendEnergy(m.cost);
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
                    // 振りかぶりの間は回避で取り消せる(当たり判定が出ている振り抜きの間だけは取り消せない)
                    if (TryDodge(now, worldMove)) return Vector3.zero;
                    if (stateT >= cur.windup) { phase = 1; stateT = 0f; StrikeSwing(); }
                    break;
                case 1:
                    if (stateT >= cur.active) { phase = 2; stateT = 0f; }
                    break;
                case 2:
                    // 戻り:振り抜いてから dodgeLockAfterSwing 秒たったら回避で取り消せる。少し戻ったら次の段・強攻撃へつなげる
                    if (stateT >= dodgeLockAfterSwing && TryDodge(now, worldMove)) return Vector3.zero;
                    if (stateT >= DuelSwing.ChainAfter && TryAttack(now)) return Vector3.zero;
                    if (stateT >= cur.recover) Enter(State.Free);
                    break;
            }
            return vel;
        }

        void StrikeSwing()
        {
            if (heavyEffect != DuelHeavyEffect.Wave)
            {
                Strike();
                return;
            }
            var hit = new HashSet<DuelEnemy>();
            Strike(hit);
            StartCoroutine(FlyWave(Chest, transform.forward, cur, hit));
        }

        /// <summary>光波は振り抜いた方向へ飛び、地形で止まる。直接斬った敵と、一度光波が当たった敵には重ねて当てない。</summary>
        IEnumerator FlyWave(Vector3 origin, Vector3 dir, DuelSwing swing, HashSet<DuelEnemy> hit)
        {
            Color color = new(0.35f, 0.9f, 1f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            const int CurvePoints = 9; // 仮
            const float CurveDepth = 1.1f; // 仮
            const float GlowWidth = 0.5f; // 仮
            const float CleanupMargin = 0.1f; // 仮
            const int ImpactSparks = 20; // 仮
            const float SparkSize = 1.4f; // 仮
            var wave = new GameObject("光波");
            var line = wave.AddComponent<LineRenderer>();
            line.sharedMaterial = ProtoFx.Unlit(color);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = CurvePoints;
            line.widthMultiplier = GlowWidth;
            Destroy(wave, WaveRange / WaveSpeed + CleanupMargin);

            float distance = 0f;
            while (distance < WaveRange && line != null)
            {
                Vector3 start = origin + dir * distance;
                for (int i = 0; i < CurvePoints; i++)
                {
                    float across = 2f * i / (CurvePoints - 1) - 1f;
                    line.SetPosition(i, start + side * (across * WaveHalfWidth) - dir * (across * across * Mathf.Min(distance, CurveDepth)));
                }
                yield return null;   // 斬撃の火花を出したフレームの仮コライダーを避け、次のフレームから飛ばす

                float step = Mathf.Min(WaveSpeed * Time.deltaTime, WaveRange - distance);
                if (step <= 0f) continue;
                bool wall = Physics.Raycast(start, dir, out var wallHit, step, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (wall) step = wallHit.distance;
                float end = distance + step;
                foreach (var e in game.Enemies)
                {
                    if (e == null || e.Dead || e.invulnerable || hit.Contains(e)) continue;
                    Vector3 offset = e.Chest - origin;
                    float height = origin.y - e.transform.position.y;
                    if (height < -WaveHalfHeight || height > game.mechHeight * e.Size + WaveHalfHeight) continue;
                    float along = Vector3.Dot(offset, dir);
                    if (along < distance - e.Radius || along > end + (wall ? 0f : e.Radius)) continue;
                    if (Mathf.Abs(Vector3.Dot(offset, side)) > WaveHalfWidth + e.Radius) continue;
                    if (!DuelSenses.ClearSight(origin, e.Chest)) continue;
                    hit.Add(e);
                    e.TakeHit(swing.damage * WaveDamageMul, swing.stagger * WaveStaggerMul, dir * swing.knock, swing.flinch);
                    DuelSpark.Burst(e.Chest, -dir, color, ImpactSparks, SparkSize);
                }
                if (wall) break;
                distance = end;
            }
            Destroy(wave);
        }

        /// <summary>
        /// 振り抜いた瞬間:扇の中の敵に当てる。当たった敵がいたかを返す。
        /// 通常攻撃の怯みには DuelGame.meleeStaggerMul が掛かる。R2 の強攻撃は、技の値をそのまま足す。
        /// 落下攻撃を、このジャンプで攻撃を跳び越えた敵に当てたときだけ、倍率を掛けずに DuelJumpRules.dropSuccessStagger をためる。
        /// </summary>
        bool Strike(HashSet<DuelEnemy> hit = null)
        {
            bool any = false, big = false;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.Dead || e.invulnerable) continue;
                if (TooFarVertically(e) && !DropOnTopOf(e)) continue;   // 上下に離れすぎていれば届かない(落下攻撃を敵の体の上で振ったときは別)
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (GapTo(e) > cur.reach) continue;
                if (to.magnitude > Radius + e.Radius + 0.5f && Vector3.Angle(transform.forward, to) > cur.arc) continue;
                bool wasBroken = e.Flinching;   // 崩れた敵を斬る(大ダメージ)
                bool jumped = dropping && JumpedOver(e);   // 攻撃を跳び越えた敵への落下攻撃(大きく崩す)
                float stagger = jumped ? game.jump.dropSuccessStagger : cur.stagger * (strongAttack ? 1f : game.meleeStaggerMul);
                Vector3 strikeDir = to == Vector3.zero ? transform.forward : Flat(to);
                e.TakeHit(cur.damage, stagger, strikeDir * cur.knock, cur.flinch, forceKnock: heavyEffect == DuelHeavyEffect.Knockback);
                hit?.Add(e);
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
            return any;
        }

        // ───────── 致命攻撃 ─────────
        // 崩れている敵の近くで、地上の近接 R1 を押すと致命攻撃が出る。ロックオン中の敵が先、次に近い敵。
        // 当たると崩れが解け、共通の弾を得る。EN は使わず、踏み込みから振り抜きまでは無敵。

        /// <summary>今の攻撃が致命攻撃か(攻撃の状態を出たら false。Enter・StartAttack が戻す)と、その相手。</summary>
        bool fatal;
        DuelEnemy fatalAim;
        /// <summary>致命攻撃の踏み込み〜振り抜きの間(無敵。回避・ジャンプ・次の攻撃で取り消せない)。</summary>
        bool FatalCommitted => fatal && Current == State.Attack && phase < 2;
        /// <summary>致命攻撃で、相手の手前に空けるすき間(m)。</summary>
        const float FatalStopGap = 1.5f;

        /// <summary>致命攻撃を入れられる敵か:生きていて、崩れていて、近く(すき間が fatalRange 以内。上下に離れすぎていない)。</summary>
        bool FatalOk(DuelEnemy e, float reach) =>
            e != null && !e.Dead && e.Flinching && !e.invulnerable && !TooFarVertically(e) && GapTo(e) <= reach;

        /// <summary>致命攻撃の相手:ロックオンの相手(入れられるなら)→ 入れられる一番近い敵。いなければ null。</summary>
        DuelEnemy FatalTarget()
        {
            if (game.fatalRange <= 0f) return null;
            if (FatalOk(Target, game.fatalRange)) return Target;
            DuelEnemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in game.Enemies)
            {
                if (!FatalOk(e, game.fatalRange)) continue;
                float d = GapTo(e);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>
        /// 地上で近接 R1 を先行入力してあり、崩れた敵が近くにいれば、致命攻撃を始める。
        /// </summary>
        bool TryFatal(float now)
        {
            if (!LightInputReady(now) || jumping || Airborne) return false;
            var e = FatalTarget();
            if (e == null) return false;
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
            if (!FatalOk(e, cur.reach)) return;   // 相手が倒れた・届かなかった:空振り
            Vector3 to = Flat(e.transform.position - transform.position);
            if (to == Vector3.zero) to = transform.forward;
            Vector3 hitAt = e.Chest - to * e.Radius;
            float dmg = Mathf.Max(game.fatalMinDamage, e.MaxHP * game.fatalHpPart);
            e.TakeFatal(dmg, to * cur.knock);
            Arms.AddFatalAmmo();
            DuelSpark.Burst(hitAt, -to, new Color(1f, 0.82f, 0.4f), 44, 2f);
            ProtoFx.Blast(hitAt, 3.2f, Color.white, 0.12f);
            if (game.fatalHitStop > 0f) game.HitStop(game.fatalHitStop);
        }
    }
}
