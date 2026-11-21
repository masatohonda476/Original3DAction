using UnityEngine;

namespace Prototype
{
    /// <summary>落ちる武装のレア度の割合(敵の種類ごとに持つ。倒した所の高さは見ない)。</summary>
    [System.Serializable]
    public class DuelRarityWeights
    {
        [Tooltip("コモン・レア・エピックの重み")] public float common = 1f, rare, epic;

        public DuelRarityWeights() { }
        public DuelRarityWeights(float common, float rare, float epic)
        {
            this.common = common;
            this.rare = rare;
            this.epic = epic;
        }

        /// <summary>重みに応じてレア度を1つ選ぶ(負の重みは0として扱う)。</summary>
        public int RollRarity()
        {
            float c = Mathf.Max(0f, common), r = Mathf.Max(0f, rare), e = Mathf.Max(0f, epic);
            float sum = c + r + e;
            if (sum <= 0f) return 0;
            float pick = Random.value * sum;
            if (pick < c) return 0;
            return pick < c + r ? 1 : 2;
        }
    }
}
