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
                Debug.Log("ロックオン解除");
            }
        }
    }

    void FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        Debug.Log("敵の数:" + enemies.Length);
        if (enemies.Length > 0)
        {
            target = enemies[0].transform;
            Debug.Log("ロックオン:" + target.name);
        }
    }
}
