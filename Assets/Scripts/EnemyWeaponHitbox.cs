using UnityEngine;

public class EnemyWeaponHitbox : MonoBehaviour
{
    private bool hasHit;
    [SerializeField] private EnemyController enemy;
    private void OnTriggerEnter(Collider other)
    {
        //攻撃中以外はダメージなし
        if (!enemy.IsAttacking) return;
        if (hasHit) return;
        //プレイヤーか判定
        PlayerStatus player = other.GetComponent<PlayerStatus>();
        if (player != null)
        {
            player.TakeDamage(enemy.AttackDamage);
            hasHit = true;
        }
    }

    public void BeginAttack()
    {
        hasHit = false;
    }
}
