using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    private NavMeshAgent agent;
    private PlayerStatus playerStatus;
    private float attackRange = 2f;
    private float attackCooldown = 1f;
    private float attackTimer = 0f;
    private bool isAttacking = false;
    [SerializeField] private GameObject target;


    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (target != null)
        {
            playerStatus = target.GetComponent<PlayerStatus>();
        }
    }

     void Update()
    {
        if (target == null) return;

        float distance = Vector3.Distance(
            transform.position,
            target.transform.position
        );

        if(distance > attackRange)
        {
        agent.destination = target.transform.position;
        }
        else
        {
            agent.ResetPath();
        }

        attackTimer -= Time.deltaTime;


        if (distance <= attackRange && attackTimer <= 0f)
        {
            playerStatus.TakeDamage(10);
            attackTimer = attackCooldown;
        }
    }
}
