using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// 敵機体。付いている部位がそのまま能力になる(ガトリング=射撃、スラスター=高速、キャノン=範囲砲撃、刃腕=近接強化)。
    /// 部位を剥がされると、その能力を失う。
    /// </summary>
    public class ProtoEnemy : MonoBehaviour
    {
        /// <summary>Normal = ステージ攻略型の通常の敵 / Hunt・Horde = 平地モードの獲物・群れ / Monster = ボス</summary>
        public enum Kind { Hunt, Horde, Monster, Normal }
        enum State { Approach, Shoot, Windup, Recover }

        [Header("基本")]
        public float moveSpeed = 4.5f;
        public float turnSpeed = 90f;
        public float bodyRadius = 2.2f;

        [Header("近接")]
        public float meleeRange = 8f;
        public float meleeDamage = 25f;
        public float meleeWindup = 0.9f;
        public float meleeRecover = 1.1f;
        public float meleeArc = 70f;

        [Header("部位の能力")]
        public float gunDamage = 3f;
        [Header("射撃のぶれ(正規分布)。狙いのずれ = 正規分布 × (aimSpread + 自機の速さ × spreadPerSpeed)")]
        [Tooltip("自機が止まっているときのずれ(m)。1.5 で約86%当たる")]
        public float aimSpread = 1.5f;
        [Tooltip("自機の速さ 1m/s ごとに増えるずれ(m)")]
        public float spreadPerSpeed = 0.04f;
        [Tooltip("キャノン(グレネード)の着弾点のずれの倍率(射撃のずれに対して)。爆風の範囲はそのまま")]
        public float cannonSpreadMul = 2f;
        [Tooltip("狙いからこの距離(m)以内にずれた弾は当たる(機体の胴の大きさ)")]
        public float hitRadius = 3f;
        public float cannonDamage = 35f;
        public float cannonRadius = 9f;

        [Tooltip("その場から動かず撃つ(高台の砲台役)。近づかれたら殴る")]
        public bool hold;
        [Tooltip("倒されたとき、付いている武装からランダムで落とす(ドロップ制)")]
        public bool drops;

        public Kind EnemyKind { get; private set; }
        /// <summary>強さの段階(0=雑兵。後半の区画ほど高い)。</summary>
        public int Tier { get; private set; }
        public float Scale { get; private set; } = 1f;
        public float Radius => bodyRadius * Scale;
        public float MaxHP { get; private set; }
        public float HP { get; private set; }
        public bool Dead { get; private set; }
        public IReadOnlyList<EnemyPart> Parts => parts;

        readonly List<EnemyPart> parts = new();
        ProtoGame game;
        Renderer body;
        MechRig rig;
        float walkPhase, idleT;
        MaterialPropertyBlock mpb;
        Color baseTint;
        State state;
        float stateT, meleeCd, gunCd, cannonCd, shotT, flashT;
        Vector3 lockDir, knock;
        CharacterController cc;
        float vy;

        public void Init(ProtoGame g, Kind kind, PartDefinition[] defs, bool harvestable, float scale, float hp, Color tint,
            ProtoGame.EnemyTier tierDef = null, int tier = 0)
        {
            game = g;
            EnemyKind = kind;
            // 強さの段階:体力・攻撃・速さ・正確さを上げ、色と大きさで見分けられるようにする
            meleeDamage *= g.enemyDamageMul;
            gunDamage *= g.enemyDamageMul;
            cannonDamage *= g.enemyDamageMul;
            float tierPartHp = 1f;
            if (tierDef != null)
            {
                Tier = tier;
                scale *= tierDef.scale;
                hp *= tierDef.hp;
                tint = tierDef.tint;
                meleeDamage *= tierDef.damage;
                gunDamage *= tierDef.damage;
                cannonDamage *= tierDef.damage;
                moveSpeed *= tierDef.speed;
                aimSpread *= tierDef.spread;
                tierPartHp = 1f + (tierDef.hp - 1f) * 0.5f;
            }
            Scale = scale;
            MaxHP = HP = hp;
            baseTint = tint;
            transform.localScale = Vector3.one * scale;

            // 地形にぶつかり、段差から落ちる(大きさは transform の拡大に合わせて広がる)
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = g.mechHeight;
            cc.radius = bodyRadius;
            cc.center = new Vector3(0f, g.mechHeight * 0.5f + 0.1f, 0f);
            cc.skinWidth = 0.2f;
            cc.stepOffset = 0.6f;

            rig = MechFactory.CreateMech(g, transform, out body);
            if (rig != null && rig.sword != null) rig.sword.gameObject.SetActive(false); // 敵は剣を持たない(素手+部位)
            mpb = new MaterialPropertyBlock();
            SetTint(baseTint);

            var slotCount = new Dictionary<MountSlot, int>();
            float partHpMul = (kind == Kind.Monster ? 4f : kind == Kind.Horde ? g.hordePartHpMul : kind == Kind.Normal ? 0.8f : 1f) * tierPartHp;
            foreach (var baseDef in defs)
            {
                // 品質:群れは粗悪が多く、狩りの獲物は上物も、バケモノは上物以上
                int grade = kind switch
                {
                    Kind.Horde => PartDefinition.RollGrade(50f, 40f, 10f, 0f),
                    Kind.Monster => PartDefinition.RollGrade(0f, 0f, 50f, 50f),
                    Kind.Normal => PartDefinition.RollGrade(25f, 45f, 22f, 8f),
                    _ => PartDefinition.RollGrade(10f, 50f, 30f, 10f),
                };
                // パラメータ制では、バケモノ(ボス)は3回振って一番良いもの、狩りの獲物は2回
                var def = g.MakeLoot(baseDef, grade, kind == Kind.Monster ? 3 : kind == Kind.Hunt ? 2 : 1);
                slotCount.TryGetValue(def.slot, out int idx);
                slotCount[def.slot] = idx + 1;
                var parent = g.MountParent(rig, transform, def.slot, idx, out var lp);
                var go = MechFactory.CreatePartVisual(def, parent, lp, true);
                var part = go.AddComponent<EnemyPart>();
                part.Init(this, def, harvestable, partHpMul);
                parts.Add(part);
            }

            if (kind == Kind.Monster)
            {
                meleeDamage *= 1.8f;
                meleeWindup = 1.2f;
                meleeRecover = 1.3f;
                moveSpeed = 3.8f;
                cannonDamage *= 1.5f;
                gunDamage *= 1.5f;
            }

            meleeCd = Random.Range(0.5f, 1.5f);
            gunCd = Random.Range(1f, 3f);
            cannonCd = Random.Range(2f, 5f);
        }

        bool Has(PartType t)
        {
            foreach (var p in parts) if (!p.Severed && p.Def.type == t) return true;
            return false;
        }

        EnemyPart First(PartType t)
        {
            foreach (var p in parts) if (!p.Severed && p.Def.type == t) return p;
            return null;
        }

        void SetTint(Color c)
        {
            mpb.SetColor("_BaseColor", c);
            body.SetPropertyBlock(mpb);
        }

        public void TakeDamage(float dmg)
        {
            if (Dead) return;
            HP -= dmg;
            flashT = 0.07f;
            if (HP <= 0f) Die();
        }

        /// <summary>壊されずに残っている部位から、ランダムで武装を落とす。</summary>
        void DropLoot()
        {
            var pool = parts.FindAll(x => !x.Severed);
            int n = EnemyKind switch
            {
                Kind.Monster => 2,
                Kind.Hunt => Random.value < game.huntExtraDropChance ? 2 : 1,
                Kind.Normal => Random.value < game.normalDropChance ? 1 : 0,
                _ => Random.value < game.hordeDropChance ? 1 : 0,
            };
            // 通常のドロップ:付いている部位から等しい確率で選ぶ
            for (int i = 0; i < n && pool.Count > 0; i++)
            {
                var pick = pool[Random.Range(0, pool.Count)];
                pool.Remove(pick);
                DropPart(pick);
            }
            // 部位破壊した部位は、それとは別に追加で落とすことがある
            foreach (var x in pool)
                if (x.Broken && Random.value < game.brokenExtraDropChance) DropPart(x);
        }

        void DropPart(EnemyPart pick)
        {
            Vector3 kick = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 6f;
            ProtoLoot.Drop(pick.Def, pick.transform.position, kick);
            parts.Remove(pick);                 // 落とした部位は機体から消す
            Destroy(pick.gameObject);
        }

        public void Knockback(Vector3 velocity)
        {
            if (Dead) return;
            velocity.y = 0f;
            knock += velocity / Scale;
        }

        void Die()
        {
            Dead = true;
            if (drops) DropLoot();
            float healChance = EnemyKind switch { Kind.Monster => game.healDropMonster, Kind.Hunt => game.healDropHunt, Kind.Normal => game.healDropNormal, _ => game.healDropHorde };
            if (Random.value < healChance)
                ProtoLoot.DropHeal(transform.position + Vector3.up * game.mechHeight * 0.6f * Scale,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 5f);
            foreach (var p in parts)
            {
                if (p.Severed) continue;
                if (p.Harvestable && EnemyKind == Kind.Hunt) game.lostParts++;   // 取り逃しの集計は狩りの獲物だけ
                p.Break();
            }
            var col = GetComponent<Collider>();
            if (col) col.enabled = false;
            SetTint(new Color(0.15f, 0.15f, 0.15f));
            ProtoFx.Blast(transform.position + Vector3.up * game.mechHeight * 0.5f * Scale, 6f * Scale, new Color(1f, 0.45f, 0.1f), 0.4f);
            ProtoCamera.Shake(0.8f);
            if (EnemyKind == Kind.Monster)
            {
                // ボスは大爆発し、自機の武装をすべて消し飛ばす(落とした武装は残る)
                Vector3 c = transform.position + Vector3.up * game.mechHeight * 0.5f * Scale;
                ProtoFx.Blast(c, 16f * Scale, new Color(1f, 0.4f, 0.1f), 0.9f);
                ProtoFx.Blast(c, 9f * Scale, new Color(1f, 0.85f, 0.4f), 0.6f);
                for (int i = 0; i < 6; i++)
                    ProtoFx.Blast(c + Random.insideUnitSphere * 8f * Scale, 5f * Scale, new Color(1f, 0.5f, 0.15f), 0.5f + i * 0.1f);
                if (ProtoPlayer.I != null && !ProtoPlayer.I.IsDead) ProtoPlayer.I.LoseAllWeapons();
            }
            game.OnEnemyDied(this);
            Destroy(gameObject, 1.5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Dead)
            {
                transform.position += Vector3.down * 6f * dt;
                transform.Rotate(25f * dt, 0f, 0f);
                return;
            }

            var p = ProtoPlayer.I;
            if (p == null) return;

            Vector3 to = p.transform.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : transform.forward;
            Vector3 eye = transform.position + Vector3.up * game.mechHeight * 0.75f * Scale;
            bool sight = !ProtoStage.Blocked(eye, p.ChestPosition);
            bool sameLevel = Mathf.Abs(dy) < game.mechHeight * Scale;
            Vector3 move = Vector3.zero;

            meleeCd -= dt; gunCd -= dt; cannonCd -= dt;

            bool thruster = Has(PartType.Thruster);
            bool blade = Has(PartType.Blade);
            float range = meleeRange * Scale * (blade ? 1.3f : 1f) + p.Radius;
            float dmgMul = blade ? 1.4f : 1f;

            if (p.IsDead)
            {
                state = State.Approach;
            }
            else switch (state)
            {
                case State.Approach:
                    Face(dir, turnSpeed * (thruster ? 2f : 1f), dt);
                    // 段差の縁(6m以上の落差)では止まる。橋や段から落ちて自滅しないように
                    bool ledge = ProtoStage.I != null &&
                        ProtoStage.GroundHeight(transform.position + dir * (Radius + 3f) + Vector3.up * 3f) < transform.position.y - 6f;
                    if (!hold && !ledge && dist > range * 0.8f)
                    {
                        float v = moveSpeed * (thruster ? 1.9f : 1f);
                        move = dir * v;
                        walkPhase += dt * v / (4.5f * Scale); // 1歩4.5m(大きさに比例)
                    }

                    if (Has(PartType.Cannon) && cannonCd <= 0f && dist > 12f && dist < 65f * Scale && sight)
                    {
                        // 着弾点も射撃と同じく正規分布でずれる(自機が速いほど外れやすい)。爆風は普通に当たる
                        Vector2 g = Gauss2() * (aimSpread + p.Speed * spreadPerSpeed) * cannonSpreadMul;
                        Vector3 land = p.transform.position + new Vector3(g.x, 0f, g.y);
                        land.y = ProtoStage.I != null ? ProtoStage.GroundHeight(land + Vector3.up * 3f) : 0f;
                        game.StartCoroutine(CannonStrike(land));
                        cannonCd = 7f;
                    }

                    if (Has(PartType.Gatling) && gunCd <= 0f && (dist > range + 4f || !sameLevel) && dist < 55f * Scale && sight)
                    {
                        state = State.Shoot; stateT = 1.4f; shotT = 0f;
                    }
                    else if (dist <= range && sameLevel && meleeCd <= 0f && Vector3.Angle(transform.forward, dir) < 25f)
                    {
                        state = State.Windup; stateT = meleeWindup; lockDir = transform.forward;
                    }
                    break;

                case State.Shoot:
                    Face(dir, turnSpeed, dt);
                    stateT -= dt; shotT -= dt;
                    if (shotT <= 0f) { shotT = 0.16f; FireShot(p); }
                    if (stateT <= 0f) { state = State.Approach; gunCd = 3.5f; }
                    break;

                case State.Windup:
                    stateT -= dt;
                    if (stateT <= 0f)
                    {
                        Strike(p, range, dmgMul);
                        state = State.Recover; stateT = meleeRecover;
                    }
                    break;

                case State.Recover:
                    stateT -= dt;
                    if (stateT <= 0f) { state = State.Approach; meleeCd = 0.4f; }
                    break;
            }

            // 移動とノックバック(押し合いは CharacterController 同士でぶつかる)。重力で段差を落ちる
            move += knock;
            knock = Vector3.MoveTowards(knock, Vector3.zero, 40f * dt);
            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy -= 30f * dt;
            cc.Move((move + Vector3.up * vy) * dt);
            if (transform.position.y < -60f) TakeDamage(HP + 1f);   // 奈落へ落ちた

            // 色:被弾で白、構え中は黄色へ
            Color c = baseTint;
            if (state == State.Windup) c = Color.Lerp(baseTint, new Color(1f, 0.85f, 0.1f), 1f - stateT / meleeWindup);
            if (flashT > 0f) { flashT -= Time.unscaledDeltaTime; c = Color.white; }
            SetTint(c);
            Animate(dt, p, dist, range);
        }

        void Animate(float dt, ProtoPlayer p, float dist, float range)
        {
            if (rig == null) return;
            switch (state)
            {
                case State.Shoot:
                    rig.Pose(MechRig.Aim, 0f, 8f);
                    return;
                case State.Windup:
                    rig.Pose(MechRig.Attack, MechRig.AttackRaised * (1f - stateT / meleeWindup), 15f);
                    return;
                case State.Recover:
                    float e = meleeRecover - stateT;
                    const float strike = 0.15f;
                    float t = e < strike
                        ? Mathf.Lerp(MechRig.AttackRaised, MechRig.AttackStruck, e / strike)
                        : Mathf.Lerp(MechRig.AttackStruck, MechRig.AttackEnd, (e - strike) / Mathf.Max(0.01f, meleeRecover - strike));
                    rig.Pose(MechRig.Attack, t, 20f);
                    return;
            }
            if (!p.IsDead && dist > range * 0.8f)
                rig.Pose(MechRig.Walk, walkPhase * 0.5f * rig.Length(MechRig.Walk), 6f);
            else
            {
                idleT += dt;
                rig.Pose(MechRig.Idle, idleT, 4f);
            }
        }

        void Face(Vector3 dir, float speed, float dt)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), speed * dt);
        }

        void Strike(ProtoPlayer p, float range, float dmgMul)
        {
            Vector3 front = transform.position + lockDir * range * 0.6f + Vector3.up * game.mechHeight * 0.5f * Scale;
            ProtoFx.Blast(front, 3f * Scale, new Color(1f, 0.9f, 0.4f), 0.2f);
            Vector3 to = p.transform.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            if (to.magnitude <= range && Mathf.Abs(dy) < game.mechHeight * Scale && Vector3.Angle(lockDir, to) <= meleeArc)
                p.TakeDamage(meleeDamage * dmgMul);
        }

        void FireShot(ProtoPlayer p)
        {
            var gun = First(PartType.Gatling);
            Vector3 from = gun != null ? gun.transform.position : transform.position + Vector3.up * game.mechHeight * 0.7f * Scale;
            Vector3 aim = p.ChestPosition;
            // 狙いのずれは正規分布。自機が速く動いているほど広がる(当たりにくくなるが、ゼロにはならない)
            float sigma = aimSpread + p.Speed * spreadPerSpeed;
            Vector3 dir = (aim - from).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 up = Vector3.Cross(dir, right);
            Vector2 g = Gauss2() * sigma;
            Vector3 off = right * g.x + up * g.y;
            bool hit = off.magnitude < hitRadius && !ProtoStage.Blocked(from, aim);   // 壁越しには当たらない
            Vector3 end = aim + off;
            if (!hit) end += dir * 30f;   // 外れた弾は自機の横を抜けていく
            ProtoFx.Tracer(from, end, new Color(1f, 0.4f, 0.3f), 0.25f);
            if (hit) p.TakeDamage(gunDamage);
        }

        /// <summary>標準正規分布に従う2つの値(Box-Muller 法)。</summary>
        static Vector2 Gauss2()
        {
            float u1 = 1f - Random.value, u2 = Random.value;
            float r = Mathf.Sqrt(-2f * Mathf.Log(u1));
            return new Vector2(r * Mathf.Cos(2f * Mathf.PI * u2), r * Mathf.Sin(2f * Mathf.PI * u2));
        }

        IEnumerator CannonStrike(Vector3 targetPos)
        {
            float r = cannonRadius * Scale;
            float dmg = cannonDamage;
            var tele = ProtoFx.Telegraph(targetPos, r);
            yield return new WaitForSeconds(1.3f);
            if (tele) Destroy(tele);
            if (!Dead)
            {
                var gun = First(PartType.Cannon);
                Vector3 from = gun != null ? gun.transform.position : transform.position + Vector3.up * game.mechHeight * 0.9f * Scale;
                ProtoFx.Tracer(from, targetPos + Vector3.up * 2f, new Color(1f, 0.5f, 0.2f), 0.6f, 0.1f);
            }
            ProtoFx.Blast(targetPos + Vector3.up * 2f, r, new Color(1f, 0.3f, 0.1f), 0.35f);
            ProtoCamera.Shake(0.5f);
            var p = ProtoPlayer.I;
            if (p != null)
            {
                Vector3 d = p.transform.position - targetPos;
                float dy = d.y;
                d.y = 0f;
                if (d.magnitude <= r + p.Radius && Mathf.Abs(dy) < r) p.TakeDamage(dmg);
            }
        }
    }
}
