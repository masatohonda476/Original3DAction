using UnityEngine;

public class LockOnSystem : MonoBehaviour
{
    private Transform target;

    void Start()
    {
        
    }


    void Update()
    {
        if (GameInput.Instance.LockOnPressed())
        {
            Debug.Log("Lock On!");
        }
    }
}
