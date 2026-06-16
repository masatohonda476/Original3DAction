using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    private NavMeshAgent agent;
    private PlayerStatus playerStatus;
    private float attackRange = 3f;
    private float attackCooldown = 1f;
    private float attackTimer = 0f;

    private enum EnemyState
    {
        Chase,
        Windup,
        Attack,
        Cooldown,
    }

    private EnemyState state = EnemyState.Chase;
    private float windupDuration = 0.5f;
    private float windupTimer = 0f;
    private float attackDuration = 0.3f;
    private float attackStateTimer;

    [SerializeField] private GameObject target;
    [SerializeField] private Transform weapon;


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

        if (state == EnemyState.Chase)
        {
            if (distance > attackRange)
            {
                agent.destination = target.transform.position;
            }
            else
            {
                agent.ResetPath();
            }
        }

        attackTimer -= Time.deltaTime;


        if (distance <= attackRange && attackTimer <= 0f && state == EnemyState.Chase)
        {
            state = EnemyState.Windup;
            windupTimer = windupDuration;
        }

        if (state == EnemyState.Windup)
        {
            windupTimer -= Time.deltaTime;

            agent.ResetPath();

            weapon.localRotation = Quaternion.RotateTowards
            (
                weapon.localRotation,
                Quaternion.Euler(-90f, 0f, 0f),
                180f * Time.deltaTime
            );

            if (windupTimer <= 0f)
            {
                state = EnemyState.Attack;
                attackStateTimer = attackDuration;
            }

            return;
        }

        if (state == EnemyState.Attack)
        {
            attackStateTimer -= Time.deltaTime;

            weapon.localRotation = Quaternion.RotateTowards
            (
                weapon.localRotation,
                Quaternion.Euler(90f, 0f, 0f),
                720f * Time.deltaTime
            );

            if (attackStateTimer <= 0f)
            {
                playerStatus.TakeDamage(10);
                attackTimer = attackCooldown;
                state = EnemyState.Cooldown;
            }

            return;
        }

        if (state == EnemyState.Cooldown)
        {
            if (attackTimer <= 0f)
            {
                weapon.localRotation = Quaternion.identity;
                state = EnemyState.Chase;
            }

            return;
        }
    }
}
