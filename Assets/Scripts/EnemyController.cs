using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    private NavMeshAgent agent;
    public GameObject target;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
    }

     void Update()
    {
        if (target == null) return;
        agent.destination = target.transform.position;
    }
}
