using UnityEngine;

public class PlayerStatus : MonoBehaviour
{
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
            Debug.Log("Player HP:" + HP);
        }
    }

    void Start()
    {
        HP = 100;
        Debug.Log("Player HP:" + HP);
    }
}