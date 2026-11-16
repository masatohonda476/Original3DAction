using UnityEngine;

namespace HuntProto
{
    /// <summary>
    /// リグ付き機体(Blenderの メカ1v3_リグ.blend から書き出し)。
    /// アニメーションは再生時間をコードから直接指定する(歩きは踏み込み、斬撃は構え・振りのタイミングに同期させるため)。
    /// 部位の取付位置(ボーンに追従)と剣もここで持つ。プレハブはビルダーが作る。
    /// </summary>
    public class MechRig : MonoBehaviour
    {
        public const string Idle = "Idle", Walk = "Walk", Attack = "Attack", Aim = "Aim", Dash = "Dash";
        /// <summary>左手で振る斬撃(Attack を左右反転。区切りのフレームは同じ)。</summary>
        public const string AttackL = "AttackL";
        /// <summary>走り(20フレームで2歩、1歩5.57m)。</summary>
        public const string Run = "Run";

        // 斬撃クリップの区切り(秒)。Blender側の 16 / 21 / 40 フレーム(30fps)
        public const float AttackRaised = 16f / 30f;
        public const float AttackStruck = 21f / 30f;
        public const float AttackEnd = 40f / 30f;

        public Animation anim;
        public SkinnedMeshRenderer body;
        public Transform mountRightArm, mountLeftArm, mountBack, mountShoulderR, mountShoulderL;
        [Tooltip("右手の剣の握り点。ローカル Y 方向に拡大すると、握りはそのままで剣が長くなる")]
        public Transform sword;
        [Tooltip("左手の剣の握り点(左手に腕刃を付けたときだけ出す)")]
        public Transform swordL;

        [Header("確認用:ここにクリップ名を入れると、そのポーズで固定される")]
        public string debugClip;
        public float debugTime;

        MaterialPropertyBlock mpb;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (anim == null) return;
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState s in anim)
            {
                s.speed = 0f;
                s.weight = s.name == Idle ? 1f : 0f;
                s.enabled = true;
            }
        }

        public bool Has(string clip) => anim != null && anim[clip] != null;
        public float Length(string clip) => anim != null && anim[clip] != null ? anim[clip].length : 1f;

        /// <summary>このフレームに見せたいクリップと再生位置(秒)。他のクリップからは blendSpeed で切り替わる。</summary>
        public void Pose(string clip, float time, float blendSpeed = 10f)
        {
            if (!string.IsNullOrEmpty(debugClip)) { clip = debugClip; time = debugTime; blendSpeed = 1000f; }
            if (anim == null || anim[clip] == null) return;
            float dt = Time.deltaTime;
            float sum = 0f;
            foreach (AnimationState s in anim)
            {
                float target = s.name == clip ? 1f : 0f;
                s.weight = Mathf.MoveTowards(s.weight, target, blendSpeed * dt);
                sum += s.weight;
            }
            if (sum > 0.0001f)
                foreach (AnimationState s in anim) s.weight /= sum;
            var st = anim[clip];
            st.time = st.wrapMode == WrapMode.Loop ? Mathf.Repeat(time, st.length) : Mathf.Clamp(time, 0f, st.length);
        }

        /// <summary>部位を付ける親と、そこからの位置。同じ枠の2個目以降は少しずつずらす。</summary>
        public Transform Mount(MountSlot slot, int index, out Vector3 localPos)
        {
            switch (slot)
            {
                case MountSlot.RightArm:
                    localPos = new Vector3(0f, 1.1f * index, -0.6f * index);
                    return mountRightArm;
                case MountSlot.LeftArm:
                    localPos = new Vector3(0f, 1.1f * index, -0.6f * index);
                    return mountLeftArm;
                case MountSlot.Back:
                    float side = index == 0 ? 0f : (index % 2 == 1 ? 1f : -1f) * 1.4f * ((index + 1) / 2);
                    localPos = new Vector3(side, 0f, 0f);
                    return mountBack;
                default:
                    localPos = new Vector3(0f, 1.2f * (index / 2), 0f);
                    return index % 2 == 0 ? mountShoulderR : mountShoulderL;
            }
        }

        public void SetTint(Color? c)
        {
            if (body == null) return;
            if (c.HasValue)
            {
                mpb.SetColor("_BaseColor", c.Value);
                body.SetPropertyBlock(mpb);
            }
            else body.SetPropertyBlock(null);
        }
    }
}
