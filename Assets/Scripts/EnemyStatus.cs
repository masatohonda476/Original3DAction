using UnityEngine;

public class EnemyStatus : MonoBehaviour
{
    [SerializeField] private int maxHP = 100;
    private int hp;
    void Start()
    {
        hp = maxHP;
    }

    public void TakeDamage(int damage)
    {
        if (hp > 0)
        {
            hp -= damage;
            if (hp <= 0)
            {
                hp = 0;
                Die();
                return;
            }
            Debug.Log($"Enemy HP: {hp}");
        }
    }

    void Die()
    {
        Debug.Log("Enemy destroyed!");
        Destroy(gameObject);
    }
}
