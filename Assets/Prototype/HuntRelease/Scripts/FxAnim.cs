using UnityEngine;

namespace HuntProto
{
    /// <summary>拡大して消えるだけの仮エフェクト。ヒットストップ中も進むよう unscaled 時間で動く。</summary>
    public class FxAnim : MonoBehaviour
    {
        Vector3 from, to;
        float life, t;

        public void Setup(Vector3 fromScale, Vector3 toScale, float lifeTime)
        {
            from = fromScale;
            to = toScale;
            life = Mathf.Max(0.01f, lifeTime);
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float k = t / life;
            transform.localScale = Vector3.Lerp(from, to, 1f - (1f - k) * (1f - k));
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
