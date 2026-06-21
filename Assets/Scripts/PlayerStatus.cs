using UnityEngine;

public class PlayerStatus : MonoBehaviour
{
    public int maxHP = 100;
    public int HP = 100;

    public void TakeDamage(int damage)
    {
        HP -= damage;
        if (HP <= 0)
        {
            HP = 0;
            Debug.Log("You Died");
        }
        else
        {
            Debug.Log($"Player HP: {HP}/{maxHP}");
        }
    }

    void Start()
    {
        HP = maxHP;
        Debug.Log($"Player HP: {HP}/{maxHP}");
    }
}