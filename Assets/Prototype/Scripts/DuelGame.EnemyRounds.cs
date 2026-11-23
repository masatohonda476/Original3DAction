using System.Collections;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelGame
    {
        internal static readonly Color EnemyShotColor = new(1f, 0.40f, 0.1f);
        internal static readonly Color EnemyMissileColor = new(1f, 0.17f, 0.43f);
        int enemyRoundVersion;

        internal void CancelEnemyRounds() => enemyRoundVersion++;

        internal void LaunchEnemyRound(DuelPlayer target, Vector3 origin, Vector3 direction, float range, float damage, bool guided) =>
            StartCoroutine(FlyEnemyRound(target, origin, direction, range, damage, guided, enemyRoundVersion));

        IEnumerator FlyEnemyRound(DuelPlayer target, Vector3 position, Vector3 direction, float remaining, float damage, bool guided, int version)
        {
            float speed = guided ? 36f : 95f;
            float trail = guided ? 0.48f : 0.19f;
            Color color = guided ? EnemyMissileColor : EnemyShotColor;
            var round = DuelArms.Projectile(position, color, guided ? 0.9f : 0.38f, trail, guided ? 1.8f : 1.3f);
            round.name = guided ? "敵の誘導弾" : "敵の直進弾";
            round.SetParent(transform, true);
            round.rotation = Quaternion.LookRotation(direction);
            Destroy(round.gameObject, remaining / speed + trail + 0.1f);
            DuelSpark.Burst(position, direction * 3f, color, 3, 0.45f);

            var guidance = new DuelMissileGuidance(guided ? target : null);
            var body = target.GetComponent<CharacterController>();
            while (remaining > 0f)
            {
                yield return null;
                if (round == null) yield break;
                if (version != enemyRoundVersion || target == null || target.Dead)
                {
                    Destroy(round.gameObject);
                    yield break;
                }
                float dt = Time.deltaTime;
                if (dt <= 0f) continue;

                if (guided) direction = guidance.Steer(position, direction, 80f * Mathf.Deg2Rad * dt);
                float step = Mathf.Min(remaining, speed * dt);
                var ray = new Ray(position, direction);
                bool wall = Physics.Raycast(ray, out var wallHit, step, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float reach = wall ? wallHit.distance : step;
                if (body.Raycast(ray, out var hit, reach))
                {
                    target.ReceiveHit(new DuelHit
                    {
                        damage = damage, stagger = guided ? 11f : 5f, hurtTime = 0.35f,
                        knock = guided ? 12f : 5f, from = position,
                    });
                    DuelSpark.Burst(hit.point, -direction, color, guided ? 8 : 3, 0.65f);
                    DuelArms.EndProjectile(round, hit.point, trail);
                    yield break;
                }
                if (wall)
                {
                    DuelSpark.Burst(wallHit.point, wallHit.normal, color, guided ? 8 : 3, 0.65f);
                    DuelArms.EndProjectile(round, wallHit.point, trail);
                    yield break;
                }

                position += direction * step;
                remaining -= step;
                round.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            }
            DuelArms.EndProjectile(round, position, trail);
        }
    }
}
