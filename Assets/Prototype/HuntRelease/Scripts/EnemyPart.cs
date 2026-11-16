using System.Collections;
using UnityEngine;

namespace HuntProto
{
    /// <summary>敵に付いている部位。剣で削り切ると剥がれ、剥ぎ取り可能なら自機へ飛んでくる。</summary>
    public class EnemyPart : MonoBehaviour
    {
        public PartDefinition Def { get; private set; }
        public ProtoEnemy Owner { get; private set; }
        public float MaxHP { get; private set; }
        public float HP { get; private set; }
        public bool Severed { get; private set; }
        public bool Harvestable { get; private set; }
        /// <summary>ドロップ制での部位破壊:付いたまま(能力も残る)、倒したときに落としやすくなる。</summary>
        public bool Broken { get; private set; }
        Color baseColor;

        Renderer[] rends;
        MaterialPropertyBlock mpb;
        float flashT;
        bool flashing;

        public void Init(ProtoEnemy owner, PartDefinition def, bool harvestable, float hpMul)
        {
            Owner = owner;
            Def = def;
            Harvestable = harvestable;
            MaxHP = HP = def.partHP * hpMul;
            rends = GetComponentsInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            baseColor = def.GradeColor;
        }

        public void TakeHit(float dmg)
        {
            if (Severed || Broken) return;
            HP -= dmg;
            flashT = 0.1f;
            if (HP > 0f) return;
            if (ProtoGame.I != null && ProtoGame.I.lootRule == LootRule.Drop) MarkBroken();
            else Sever();
        }

        /// <summary>ドロップ制:壊れた印(暗くなる)を付けるだけ。外れないし、敵の能力も残る。</summary>
        void MarkBroken()
        {
            Broken = true;
            HP = 0f;
            baseColor = Def.GradeColor * 0.45f;
            baseColor.a = 1f;
            ProtoFx.Blast(transform.position, 3f * transform.lossyScale.x, new Color(1f, 0.6f, 0.2f), 0.25f);
            ProtoGame.I.Log($"部位破壊: {Def.displayName}(倒すと追加で落とすことがある)");
        }

        void Update()
        {
            if (flashT > 0f)
            {
                flashT -= Time.unscaledDeltaTime;
                mpb.SetColor("_BaseColor", Color.white);
                foreach (var r in rends) r.SetPropertyBlock(mpb);
                flashing = true;
            }
            else if (flashing)
            {
                mpb.SetColor("_BaseColor", baseColor);   // 品質の色(壊れていれば暗く)に戻す
                foreach (var r in rends) r.SetPropertyBlock(mpb);
                flashing = false;
            }
        }

        void Detach()
        {
            Severed = true;
            transform.SetParent(null, true);
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
        }

        void Sever()
        {
            Detach();
            ProtoFx.Blast(transform.position, 3f * transform.lossyScale.x, new Color(1f, 0.6f, 0.2f), 0.25f);
            ProtoCamera.Shake(0.6f);
            if (Harvestable && ProtoPlayer.I != null)
            {
                ProtoGame.I.Log($"剥ぎ取り: {Def.displayName}");
                StartCoroutine(FlyToPlayer());
            }
            else
            {
                ProtoGame.I.Log($"部位破壊: {Def.displayName}");
                StartCoroutine(Fall());
            }
        }

        /// <summary>本体が先に倒れた時。剥ぎ取れずに失われる。</summary>
        public void Break()
        {
            if (Severed) return;
            Detach();
            StartCoroutine(Fall());
        }

        IEnumerator FlyToPlayer()
        {
            Vector3 start = transform.position;
            const float dur = 0.7f;
            float t = 0f;
            while (t < dur && ProtoPlayer.I != null)
            {
                t += Time.deltaTime;
                float k = t / dur;
                Vector3 target = ProtoPlayer.I.ChestPosition;
                transform.position = Vector3.Lerp(start, target, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 10f;
                transform.Rotate(400f * Time.deltaTime, 250f * Time.deltaTime, 0f);
                yield return null;
            }
            if (ProtoPlayer.I != null) ProtoPlayer.I.Acquire(Def);
            Destroy(gameObject);
        }

        IEnumerator Fall()
        {
            float v = 0f;
            float t = 0f;
            while (t < 2f)
            {
                t += Time.deltaTime;
                v += 25f * Time.deltaTime;
                Vector3 p = transform.position;
                p.y = Mathf.Max(0.5f, p.y - v * Time.deltaTime);
                transform.position = p;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
