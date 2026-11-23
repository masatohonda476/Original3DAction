using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelEnemy
    {
        Transform rangedMount, muzzleCharge;
        Renderer muzzleRenderer;
        Vector3 shotDirection;
        float rangedReadyAt;

        bool IsRangedSkill => skill is Skill.Shot or Skill.Missile;
        Vector3 Muzzle => rangedMount.TransformPoint(Vector3.forward * (1.3f * Size));

        void BuildRangedMount()
        {
            rangedMount = new GameObject("遠距離武装").transform;
            rangedMount.SetParent(transform, false);
            rangedMount.localPosition = new Vector3(1.45f * Size, game.mechHeight * Size * 0.69f, 0.4f * Size);

            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(housing.GetComponent<Collider>());
            housing.name = "発射口";
            housing.transform.SetParent(rangedMount, false);
            housing.transform.localPosition = Vector3.forward * (0.45f * Size);
            housing.transform.localScale = new Vector3(0.76f, 0.76f, 1.55f) * Size;
            housing.GetComponent<Renderer>().sharedMaterial = ProtoFx.Unlit(new Color(0.23f, 0.28f, 0.33f));

            var charge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(charge.GetComponent<Collider>());
            charge.name = "発射予告";
            muzzleCharge = charge.transform;
            muzzleCharge.SetParent(rangedMount, false);
            muzzleCharge.localPosition = Vector3.forward * (1.3f * Size);
            muzzleRenderer = charge.GetComponent<Renderer>();
            charge.SetActive(false);
        }

        void UpdateRangedMount()
        {
            if (muzzleCharge == null) return;
            bool charging = Current == State.Windup && IsRangedSkill;
            if (muzzleCharge.gameObject.activeSelf != charging) muzzleCharge.gameObject.SetActive(charging);
            if (!charging) return;
            Color color = skill == Skill.Missile ? DuelGame.EnemyMissileColor : DuelGame.EnemyShotColor;
            muzzleRenderer.sharedMaterial = ProtoFx.Unlit(color);
            muzzleCharge.localScale = Vector3.one * (Size * Mathf.Lerp(0.65f, 1.5f, stateT / stateDur));
        }

        bool InRangedAttackBand(float gap)
        {
            if (rangedMount == null || invulnerable || Time.time < rangedReadyAt || game.stage.Cleared) return false;
            // 近接が届く場所では剣を使う。段差や空中なら高低差も距離として扱う。
            if (!Kind.hovers && gap < Mathf.Max(18f, SweepReach + 4f) && !TooFarVertically(player)) return false;
            return (Chest - player.Chest).sqrMagnitude <= Kind.fireRange * Kind.fireRange;
        }

        bool TryStartRanged(float gap)
        {
            if (!InRangedAttackBand(gap) || !DuelSenses.ClearSight(Chest, player.Chest)) return false;

            bool guided = Kind.hovers ? lastSkill == Skill.Shot : Kind.usesMissiles;
            shotDirection = (player.Chest - rangedMount.position).normalized;
            StartSkill(guided ? Skill.Missile : Skill.Shot);
            return true;
        }

        void UpdateRangedWindup(float dt, Vector3 dir)
        {
            const float ShotCommit = 0.18f;
            bool guided = skill == Skill.Missile;
            bool targetAlive = player != null && !player.Dead;
            if (targetAlive && (guided || stateT < stateDur - ShotCommit))
            {
                Face(dir, 10f, dt);
                shotDirection = (player.Chest - rangedMount.position).normalized;
            }
            if (shotDirection != Vector3.zero) rangedMount.rotation = Quaternion.LookRotation(shotDirection);
            if (stateT < stateDur) return;

            // 溜めの途中に物陰へ隠れたら発射しない。壁にめり込んだ発射口からも撃たない。
            bool clear = targetAlive && DuelSenses.ClearSight(Chest, Muzzle) && DuelSenses.ClearSight(Muzzle, player.Chest);
            if (clear && !game.stage.Cleared)
            {
                float damage = Kind.hovers ? (guided ? 9f : 6f) : (guided ? 12f : 7f) * Kind.damageMul;
                game.LaunchEnemyRound(player, Muzzle, shotDirection, Kind.fireRange + 28f, damage, guided);
            }
            rangedReadyAt = Time.time + (clear ? (Kind.hovers ? 3f : guided ? 5f : 4f) : 1.2f);
            Enter(State.Recover, 0.55f);
        }
    }
}
