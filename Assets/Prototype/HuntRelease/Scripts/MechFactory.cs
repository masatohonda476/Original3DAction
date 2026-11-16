using UnityEngine;

namespace HuntProto
{
    /// <summary>機体と部位の見た目を組み立てる。見た目の差し替えはここと PartDefinition.visualPrefab だけで済むようにしている。</summary>
    public static class MechFactory
    {
        public static MeshRenderer CreateMechVisual(Transform parent, Mesh mesh, Material[] materials)
        {
            var go = new GameObject("Visual");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            return mr;
        }

        /// <summary>機体を作る。リグ付きプレハブがあればそれを、なければ固定メッシュを使う。</summary>
        public static MechRig CreateMech(ProtoGame g, Transform parent, out Renderer body)
        {
            if (g.mechPrefab != null)
            {
                var go = Object.Instantiate(g.mechPrefab, parent, false);
                go.name = "Visual";
                var rig = go.GetComponent<MechRig>();
                body = rig.body;
                return rig;
            }
            body = CreateMechVisual(parent, g.mechMesh, g.mechMaterials);
            return null;
        }

        /// <summary>部位の見た目を作る。withCollider なら剣で狙える当たり判定(トリガー)を付ける。</summary>
        public static GameObject CreatePartVisual(PartDefinition def, Transform parent, Vector3 localPos, bool withCollider)
        {
            var root = new GameObject(def.displayName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            if (def.visualPrefab != null)
            {
                var inst = Object.Instantiate(def.visualPrefab, root.transform, false);
                // 自作モデルも品質の色で塗る(色を残したい場合は PartDefinition 側で調整する)
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", def.GradeColor);
                foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
            }
            else
            {
                var prim = GameObject.CreatePrimitive(def.placeholderShape);
                Object.DestroyImmediate(prim.GetComponent<Collider>());
                prim.transform.SetParent(root.transform, false);
                prim.transform.localScale = def.placeholderScale;
                prim.transform.localRotation = Quaternion.Euler(def.placeholderEuler);
                prim.GetComponent<Renderer>().sharedMaterial = ProtoFx.Lit(def.GradeColor);
            }

            if (withCollider)
            {
                // 形そのものに合わせた当たり判定(取付先が回転していてもずれない)
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<BoxCollider>().isTrigger = true;
            }
            return root;
        }
    }
}
