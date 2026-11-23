using System.Collections.Generic;
using HuntProto;
using UnityEngine;

namespace Prototype
{
    public partial class DuelStage
    {
        const float GrenadeRange = 38f, GrenadeFlight = 0.64f, GrenadeFuse = 0.85f;
        const float GrenadeRadius = 14f, GrenadeDamage = 18f, GrenadeStagger = 12f;
        const float AnchorRadius = 14f, AnchorDuration = 10f, AnchorHeight = 3f;
        const float BeaconRange = 35f, BeaconHeight = 7f, BeaconDuration = 4f;

        sealed class CombatItem
        {
            public DuelItem Kind;
            public Transform Visual;
            public float StartedAt, EndsAt;
            public HashSet<DuelEnemy> BeaconTargets;
            public Vector3 Origin, Destination;
            public bool Landed;
        }

        readonly List<CombatItem> combatItems = new();

        internal void ThrowGrenade()
        {
            Vector3 feet = player.transform.position;
            Vector3 destination = feet + player.transform.forward * 26f;
            var enemy = player.Target;
            if (enemy != null && !enemy.Dead)
            {
                Vector3 offset = enemy.transform.position - feet;
                destination = offset.magnitude <= GrenadeRange
                    ? enemy.transform.position
                    : feet + Vector3.ClampMagnitude(new Vector3(offset.x, 0f, offset.z), GrenadeRange);
            }
            if (Physics.Raycast(destination + Vector3.up * 3f, Vector3.down, out var floor, 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                destination.y = floor.point.y;

            var grenade = CreateCombatItem(DuelItem.Grenade, player.Chest, GrenadeFuse);
            grenade.Origin = player.Chest;
            grenade.Destination = destination + Vector3.up * 0.45f;
            Color tint = DuelItems.Get(grenade.Kind).Tint;
            ItemShape(grenade.Visual, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 1.35f, tint);
            ItemShape(grenade.Visual, PrimitiveType.Cube, Vector3.up * 0.62f,
                new Vector3(0.52f, 0.4f, 0.52f), new Color(0.22f, 0.23f, 0.28f));
        }

        HashSet<DuelEnemy> FindBeaconTargets(Vector3 floor)
        {
            var targets = new HashSet<DuelEnemy>();
            foreach (var enemy in game.Enemies)
            {
                if (enemy == null || enemy.Dead || enemy.Invulnerable || enemy.IsBoss || !enemy.Engaged ||
                    enemy.Kind.hovers || enemy.Airborne || enemy.Flinching || TryGetBeacon(enemy, out _)) continue;
                Vector3 offset = enemy.transform.position - floor;
                if (Mathf.Abs(offset.y) > BeaconHeight || offset.sqrMagnitude > BeaconRange * BeaconRange ||
                    !DuelSenses.ClearSight(enemy.Chest, floor + Vector3.up * 1.5f)) continue;
                targets.Add(enemy);
            }
            return targets;
        }

        internal void PlaceCombatItem(DuelItem kind, RaycastHit floor)
        {
            bool anchor = kind == DuelItem.MagneticAnchor;
            var targets = anchor ? null : FindBeaconTargets(floor.point);
            var item = CreateCombatItem(kind, floor.point, anchor ? AnchorDuration : BeaconDuration);
            item.BeaconTargets = targets;
            var tint = DuelItems.Get(kind).Tint;
            ItemShape(item.Visual, PrimitiveType.Cylinder, Vector3.up * 0.23f, new Vector3(2.7f, 0.23f, 2.7f), tint);
            ItemShape(item.Visual, PrimitiveType.Cube, Vector3.up * (anchor ? 0.8f : 1.5f),
                new Vector3(0.8f, anchor ? 1.2f : 2.5f, 0.8f), tint);
            if (anchor)
            {
                DrawItemRing(item.Visual, AnchorRadius, tint);
            }
            else
            {
                DrawItemRing(item.Visual, 3f, tint);
                ItemShape(item.Visual, PrimitiveType.Sphere, Vector3.up * 3f, Vector3.one * 1.1f, tint);
            }
        }

        CombatItem CreateCombatItem(DuelItem kind, Vector3 position, float duration)
        {
            var visual = new GameObject(DuelItems.Get(kind).Name).transform;
            visual.SetParent(transform, false);
            visual.position = position;
            var item = new CombatItem
            {
                Kind = kind, Visual = visual, StartedAt = Time.time, EndsAt = Time.time + duration,
            };
            combatItems.Add(item);
            return item;
        }

        internal float AnchorMovementMultiplier(DuelEnemy enemy)
        {
            if (enemy.Kind.hovers || enemy.Airborne || enemy.Invulnerable) return 1f;
            foreach (var item in combatItems)
            {
                if (item.Kind != DuelItem.MagneticAnchor || Time.time >= item.EndsAt) continue;
                Vector3 floor = item.Visual.position;
                Vector3 offset = enemy.transform.position - floor;
                if (Mathf.Abs(offset.y) <= AnchorHeight && offset.x * offset.x + offset.z * offset.z <= AnchorRadius * AnchorRadius &&
                    DuelSenses.ClearSight(floor + Vector3.up * 0.5f, enemy.Chest))
                    return enemy.IsBoss ? 0.8f : 0.45f;
            }
            return 1f;
        }

        internal bool TryGetBeacon(DuelEnemy enemy, out Vector3 position)
        {
            foreach (var item in combatItems)
            {
                if (item.Kind != DuelItem.DecoyBeacon || !item.BeaconTargets.Contains(enemy) || Time.time >= item.EndsAt) continue;
                position = item.Visual.position;
                return true;
            }
            position = default;
            return false;
        }

        void TickCombatItems()
        {
            float now = Time.time;
            for (int i = combatItems.Count - 1; i >= 0; --i)
            {
                var item = combatItems[i];
                bool grenade = item.Kind == DuelItem.Grenade;
                if (grenade && !item.Landed) AdvanceGrenade(item, now);
                if (now < item.EndsAt) continue;
                combatItems.RemoveAt(i);
                Vector3 position = item.Visual.position;
                Destroy(item.Visual.gameObject);
                if (grenade) ExplodeGrenade(position);
                if (Cleared) return; // 爆発でボスを倒すと残る設置物も破棄される。
            }
        }

        static void AdvanceGrenade(CombatItem grenade, float now)
        {
            float t = Mathf.Clamp01((now - grenade.StartedAt) / GrenadeFlight);
            Vector3 next = Vector3.Lerp(grenade.Origin, grenade.Destination, t) + Vector3.up * (28f * t * (1f - t));
            if (Physics.Linecast(grenade.Visual.position, next, out var hit,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                grenade.Visual.position = hit.point + hit.normal * 0.15f;
                grenade.Landed = true;
                return;
            }
            grenade.Visual.position = next;
            grenade.Landed = t >= 1f;
        }

        void ExplodeGrenade(Vector3 position)
        {
            foreach (var enemy in game.Enemies)
            {
                if (enemy == null || enemy.Dead || enemy.Invulnerable ||
                    (enemy.Chest - position).sqrMagnitude > GrenadeRadius * GrenadeRadius ||
                    !DuelSenses.ClearSight(position, enemy.Chest)) continue;
                enemy.TakeRangedHit(GrenadeDamage, GrenadeStagger, Vector3.zero, 0f);
                if (Cleared) break;
            }
            var tint = DuelItems.Get(DuelItem.Grenade).Tint;
            ProtoFx.Blast(position, GrenadeRadius, tint, 0.32f);
            DuelSpark.Burst(position, Vector3.up, tint, 24, 1.4f);
        }

        void ClearCombatItems()
        {
            foreach (var item in combatItems) Destroy(item.Visual.gameObject);
            combatItems.Clear();
        }

        static void ItemShape(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color tint)
        {
            var shape = GameObject.CreatePrimitive(type);
            DestroyImmediate(shape.GetComponent<Collider>());
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = position;
            shape.transform.localScale = scale;
            var renderer = shape.GetComponent<Renderer>();
            renderer.sharedMaterial = ProtoFx.Unlit(tint);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void DrawItemRing(Transform parent, float radius, Color tint)
        {
            const int segments = 48;
            var ring = parent.gameObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = segments;
            ring.widthMultiplier = 0.4f;
            ring.sharedMaterial = ProtoFx.Unlit(tint);
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < segments; ++i)
            {
                float angle = i * (2f * Mathf.PI / segments);
                // 円周を床面に沿わせ、段差や斜面で円が地面に埋まるのを抑える。
                Vector3 offset = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Physics.Raycast(parent.position + offset + Vector3.up * 2.5f, Vector3.down,
                    out var hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    offset.y = hit.point.y - parent.position.y;
                ring.SetPosition(i, offset + Vector3.up * 0.18f);
            }
        }
    }
}
