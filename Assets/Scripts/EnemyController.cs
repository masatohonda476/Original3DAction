using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    private NavMeshAgent agent;
    private PlayerStatus playerStatus;
    private float attackRange = 2f;
    private float stopMargin = 0.3f;
    private float attackCooldown = 1f;
    private float attackTimer = 0f;
    private Quaternion attackRotation;

    private enum EnemyState
    {
        Chase,
        Windup,
        Attack,
        Cooldown,
    }

    private EnemyState state = EnemyState.Chase;
    private float windupDuration = 0.3f;
    private float windupTimer = 0f;
    private float attackDuration = 0.2f;
    private float attackStateTimer;
    private EnemyWeaponHitbox weaponHitbox;
    [SerializeField] private int attackDamage = 10;

    [SerializeField] private GameObject target;
    [SerializeField] private Transform weapon;

    public bool IsAttacking => state == EnemyState.Attack;
    public int AttackDamage => attackDamage;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = attackRange;
        weaponHitbox = weapon.GetComponentInChildren<EnemyWeaponHitbox>();
        if (target != null)
        {
            playerStatus = target.GetComponent<PlayerStatus>();
        }
    }

    void Update()
    {
        if (target == null) return;

//プレイヤーとの距離を計算
        float distance = Vector3.Distance(
            transform.position,
            target.transform.position
        );

//Chase状態の処理
        if (state == EnemyState.Chase)
        {
            if (agent.enabled)
            {
                agent.isStopped = false;
            }
            if (distance > attackRange + stopMargin)
            {
                agent.destination = target.transform.position;
            }
            else
            {
                agent.isStopped = true;

                Vector3 direction = target.transform.position - transform.position;
                direction.y = 0f;

                if (direction != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }
        }

//攻撃のクールダウンタイマーを更新
        attackTimer -= Time.deltaTime;

//攻撃判定開始
        if (distance <= attackRange && attackTimer <= 0f && state == EnemyState.Chase)
        {
            agent.velocity = Vector3.zero;
            agent.ResetPath();
            agent.enabled = false;

            state = EnemyState.Windup;
            windupTimer = windupDuration;
            attackRotation = transform.rotation;
        }

//Windup状態の処理
        if (state == EnemyState.Windup)
        {
            windupTimer -= Time.deltaTime;

            transform.rotation = attackRotation;

            weapon.localRotation = Quaternion.RotateTowards
            (
                weapon.localRotation,
                Quaternion.Euler(-90f, 0f, 0f),
                180f * Time.deltaTime
            );

            if (windupTimer <= 0f)
            {
                weaponHitbox.BeginAttack();
                state = EnemyState.Attack;
                attackStateTimer = attackDuration;
            }

            return;
        }

//Attack状態の処理
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
                attackTimer = attackCooldown;
                state = EnemyState.Cooldown;
            }
        }

//Cooldown状態の処理
        if (state == EnemyState.Cooldown)
        {
            if (attackTimer <= 0f)
            {
                weapon.localRotation = Quaternion.identity;
                agent.enabled = true;
                state = EnemyState.Chase;
            }

            return;
        }
    }
}
