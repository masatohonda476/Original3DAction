using UnityEngine;

public class PlayerAttack : MonoBehaviour
{

    private enum AttackState
    {
        Idle,
        Windup,
        Attack,
        Recovery
    }

        [Header("References")]
        [SerializeField] private Transform weaponPivot;
        [SerializeField] private PlayerWeaponHitbox weaponHitbox;


        [Header("Timing")]
        [SerializeField] private float windupDuration = 0.2f;
        private float windupTimer;
        [SerializeField] private float attackDuration = 0.2f;
        private float attackTimer;
        [SerializeField] private float recoveryDuration = 0.3f;
        private float recoveryTimer;

        private AttackState state = PlayerAttack.AttackState.Idle;
        [Header("Attack")]
        [SerializeField] private int attackDamage = 10;
        public bool IsAttacking => state == AttackState.Attack;
        public int AttackDamage => attackDamage;
        private void Update()
    {
        //攻撃モーション
        switch(state)
        {
            case AttackState.Idle:
                if (GameInput.Instance.LightAttackPressed())
                {
                    state = AttackState.Windup;
                    windupTimer = windupDuration;
                }
                break;

            case AttackState.Windup:
                windupTimer -= Time.deltaTime;
                RotateWeapon(-90f, windupDuration);
                if (windupTimer <= 0f)
                {
                    weaponHitbox.BeginAttack();
                    state = AttackState.Attack;
                    attackTimer = attackDuration;
                }
                break;

            case AttackState.Attack:
                attackTimer -= Time.deltaTime;
                RotateWeapon(90f, attackDuration);
                if (attackTimer <= 0f)
                {
                    state = AttackState.Recovery;
                    recoveryTimer = recoveryDuration;
                }
                break;

            case AttackState.Recovery:
                recoveryTimer -= Time.deltaTime;
                weaponPivot.localRotation = Quaternion.RotateTowards(
                    weaponPivot.localRotation,
                    Quaternion.identity,
                    720f * Time.deltaTime);

                if (recoveryTimer <= 0f)
                {
                    weaponPivot.localRotation = Quaternion.identity;
                    state = AttackState.Idle;
                }
                break;
        }
    }

    private void RotateWeapon(float targetX, float duration)
    {
        Quaternion targetRotation = Quaternion.Euler(targetX, 0f, 0f);
        float speed = 180f / duration;

        weaponPivot.localRotation = Quaternion.RotateTowards(
            weaponPivot.localRotation,
            targetRotation,
            speed * Time.deltaTime);
    }
}