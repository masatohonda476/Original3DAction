using UnityEngine;

public class LockOnSystem : MonoBehaviour
{
    private Transform target;
    public Transform Target => target;

    void Start()
    {
        
    }


    void Update()
    {
        if (GameInput.Instance.LockOnPressed())
        {
            if (target == null)
            {
                FindNearestEnemy();
            }
            else
            {
                target = null;
            }
        }
    }

    void FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies.Length > 0)
        {
            target = enemies[0].transform;
        }
    }
}
