using System.Collections;
using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelArms
    {
        const float FlareLife = 2.5f, FlareRange = 45f;
        static readonly Color FlareColor = new(1f, 0.44f, 0.23f);
        readonly List<Transform> activeFlares = new();

        void DeployFlare(int slot)
        {
            float side = slot % Sides == 0 ? -1f : 1f;
            var origin = subVisuals[slot].position;
            var velocity = transform.TransformDirection(new Vector3(side * 15f, 9f, -12f));
            var flare = Projectile(origin, FlareColor, 0.85f, 0.3f);
            flare.name = "フレア";
            flare.SetParent(game.transform, true);
            activeFlares.RemoveAll(f => f == null);
            activeFlares.Add(flare);
            DuelSpark.Burst(origin, velocity, FlareColor, 6, 0.7f);
            StartCoroutine(MoveFlare(flare, velocity));
        }

        IEnumerator MoveFlare(Transform flare, Vector3 velocity)
        {
            // コルーチンが途中で止まっても、デコイを残さない。
            Destroy(flare.gameObject, FlareLife);
            while (flare != null)
            {
                yield return null;
                if (flare == null) break;
                float dt = Time.deltaTime;
                if (dt <= 0f) continue;
                velocity += Vector3.down * (8f * dt);
                Vector3 step = velocity * dt;
                if (Physics.Raycast(flare.position, step.normalized, out var hit, step.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    flare.position = hit.point + hit.normal * 0.1f;
                    break;
                }
                flare.position += step;
            }
        }

        internal bool TryGetFlareTarget(Vector3 missilePosition, out Transform target)
        {
            target = null;
            float nearest = FlareRange * FlareRange;
            for (int i = activeFlares.Count - 1; i >= 0; i--)
            {
                var flare = activeFlares[i];
                if (flare == null)
                {
                    activeFlares.RemoveAt(i);
                    continue;
                }
                float distance = (flare.position - missilePosition).sqrMagnitude;
                if (distance >= nearest) continue;
                target = flare;
                nearest = distance;
            }
            return target != null;
        }

        void ClearFlares()
        {
            foreach (var flare in activeFlares)
                if (flare != null) Destroy(flare.gameObject);
            activeFlares.Clear();
        }
    }

    /// <summary>誘導弾ごとに追尾先を保持する。フレアにそらされた弾は自機を再捕捉しない。</summary>
    internal struct DuelMissileGuidance
    {
        readonly DuelFighter target;
        Transform decoy;
        bool diverted;

        public DuelMissileGuidance(DuelFighter target)
        {
            this.target = target;
            decoy = null;
            diverted = false;
        }

        public Vector3 Steer(Vector3 position, Vector3 direction, float maxTurn)
        {
            if (!diverted && target is DuelPlayer player && player != null && !player.Dead && player.Arms != null)
                diverted = player.Arms.TryGetFlareTarget(position, out decoy);
            if (diverted)
            {
                if (decoy == null) return direction;
                Vector3 offset = decoy.position - position;
                if (offset.sqrMagnitude <= 1f)
                {
                    decoy = null;
                    return direction;
                }
                return Vector3.RotateTowards(direction, offset.normalized, maxTurn, 0f);
            }
            return target != null && !target.Dead
                ? Vector3.RotateTowards(direction, (target.Chest - position).normalized, maxTurn, 0f)
                : direction;
        }
    }
}
