using UnityEngine;

namespace Prototype
{
    /// <summary>落ちる武装のレア度の割合(この高さ(m)から上で使う)。上ほど強い武装が落ちる。</summary>
    [System.Serializable]
    public class DuelDropBand
    {
        [Tooltip("この高さ(m)から上で使う")] public float fromY;
        [Tooltip("コモン・レア・エピックの重み")] public float common = 1f, rare, epic;

        public DuelDropBand() { }
        public DuelDropBand(float fromY, float common, float rare, float epic)
        {
            this.fromY = fromY;
            this.common = common;
            this.rare = rare;
            this.epic = epic;
        }

        /// <summary>重みに応じてレア度を1つ選ぶ。</summary>
        public int RollRarity()
        {
            float sum = Mathf.Max(0f, common) + Mathf.Max(0f, rare) + Mathf.Max(0f, epic);
            if (sum <= 0f) return 0;
            float r = Random.value * sum;
            if (r < common) return 0;
            return r < common + rare ? 1 : 2;
        }
    }
}
