using UnityEngine;

namespace MeleeDuel
{
    /// <summary>
    /// ステージの区画の間の扉。閉じている間は通れない(見えない壁で上も塞ぐので、飛んでも越えられない)。
    /// 開くと床へ沈み、閉じると床からせり上がる。
    /// </summary>
    public class DuelDoor : MonoBehaviour
    {
        [Tooltip("開くときに沈む速さ(m/s)")]
        public float openSpeed = 12f;
        [Tooltip("閉じるときにせり上がる速さ(m/s)")]
        public float closeSpeed = 28f;

        public bool IsOpen { get; private set; }

        Collider[] blockers;
        float closedY, openY;

        /// <summary>閉じた状態で用意する。height は見える扉の高さ。</summary>
        public void Init(float height)
        {
            closedY = transform.position.y;
            openY = closedY - height - 0.5f;
            blockers = GetComponentsInChildren<Collider>();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            foreach (var c in blockers) c.enabled = false;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            foreach (var c in blockers) c.enabled = true;
        }

        void Update()
        {
            Vector3 p = transform.position;
            float target = IsOpen ? openY : closedY;
            if (Mathf.Approximately(p.y, target)) return;
            p.y = Mathf.MoveTowards(p.y, target, (IsOpen ? openSpeed : closeSpeed) * Time.deltaTime);
            transform.position = p;
        }
    }
}
