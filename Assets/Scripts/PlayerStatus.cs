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
            Die();
        }
        else
        {
            Debug.Log($"Player HP: {HP}/{maxHP}");
        }
    }

    void Die()
    {
        Debug.Log("You Died");

        GetComponent<PlayerController>().enabled = false;
        GetComponent<PlayerAttack>().enabled = false;

        GameManager.Instance.GameOver();
    }

    void Start()
    {
        HP = maxHP;
        Debug.Log($"Player HP: {HP}/{maxHP}");
    }
}