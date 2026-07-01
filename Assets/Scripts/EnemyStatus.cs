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
        hp -= damage;

        Debug.Log($"Enemy HP: {hp}");
    }
}
