using UnityEngine;

public class PlayerWeaponHitbox : MonoBehaviour
{
    private bool hasHit;
    [SerializeField] private PlayerAttack playerAttack;
    private void OnTriggerEnter(Collider other)
    {
        //攻撃中以外はダメージなし
        if (!playerAttack.IsAttacking) return;
        if (hasHit) return;
        //敵か判定
        EnemyStatus enemyStatus = other.GetComponent<EnemyStatus>();
        if (enemyStatus != null)
        {
            enemyStatus.TakeDamage(playerAttack.AttackDamage);
            hasHit = true;
        }
    }

    public void BeginAttack()
    {
        hasHit = false;
    }
}
