using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// 宝箱。近づくと開き、武装(品質は高め)か回復アイテムが飛び出す。中身は開けたときに決まる。
    /// 見た目は仮の箱(後で差し替える前提)。
    /// </summary>
    public class ProtoChest : MonoBehaviour
    {
        [Tooltip("武装が入っている確率(残りは回復アイテム)")]
        public float weaponChance = 0.6f;
        public float openRadius = 9f;

        Transform lid;
        bool opened;

        static readonly Color Body = new(0.45f, 0.3f, 0.15f), Trim = new(0.95f, 0.75f, 0.2f), Spent = new(0.25f, 0.22f, 0.2f);

        public static ProtoChest Create(Vector3 groundPoint, Transform parent)
        {
            var go = new GameObject("Chest");
            go.transform.SetParent(parent, true);
            go.transform.position = groundPoint;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var c = go.AddComponent<ProtoChest>();
            c.Build();
            return c;
        }

        void Build()
        {
            Part(new Vector3(0f, 1.2f, 0f), new Vector3(4.5f, 2.4f, 3f), Body);
            Part(new Vector3(0f, 1.2f, 0f), new Vector3(4.6f, 0.4f, 3.1f), Trim);
            lid = new GameObject("Lid").transform;
            lid.SetParent(transform, false);
            lid.localPosition = new Vector3(0f, 2.4f, -1.5f);   // 後ろの辺を軸に開く
            var top = Part(new Vector3(0f, 0.5f, 1.5f), new Vector3(4.6f, 1f, 3.1f), Trim);
            top.SetParent(lid, false);
            top.localPosition = new Vector3(0f, 0.5f, 1.5f);
        }

        Transform Part(Vector3 localPos, Vector3 size, Color c)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(b.GetComponent<Collider>());   // 通り抜けられる(地形扱いにしない)
            b.transform.SetParent(transform, false);
            b.transform.localPosition = localPos;
            b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(c);
            return b.transform;
        }

        void Update()
        {
            if (opened)
            {
                lid.localRotation = Quaternion.Slerp(lid.localRotation, Quaternion.Euler(-110f, 0f, 0f), 1f - Mathf.Exp(-8f * Time.deltaTime));
                return;
            }
            var p = ProtoPlayer.I;
            if (p == null || p.IsDead) return;
            Vector3 d = p.transform.position - transform.position;
            float dy = d.y;
            d.y = 0f;
            if (d.magnitude > openRadius + p.Radius || Mathf.Abs(dy) > 10f) return;
            Open();
        }

        void Open()
        {
            opened = true;
            Vector3 top = transform.position + Vector3.up * 3f;
            Vector3 kick = transform.forward * 4f;
            var game = ProtoGame.I;
            if (Random.value < weaponChance && game != null && game.partPool.Length > 0)
            {
                // 宝箱の武装は品質が高め(粗悪10% / 標準40% / 上物35% / 逸品15%)
                var def = game.MakeLoot(game.partPool[Random.Range(0, game.partPool.Length)], PartDefinition.RollGrade(10f, 40f, 35f, 15f), 2);
                ProtoLoot.Drop(def, top, kick);
                game.Log($"宝箱を開けた:{def.displayName}");
            }
            else
            {
                ProtoLoot.DropHeal(top, kick);
                game?.Log("宝箱を開けた:回復アイテム");
            }
            ProtoFx.Blast(top, 5f, Trim, 0.35f);
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (r.transform != lid.GetChild(0)) r.sharedMaterial = ProtoFx.Lit(Spent);
        }
    }
}
