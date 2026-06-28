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


        [Header("Timing")]
        [SerializeField] private float windupTime = 0.2f;
        [SerializeField] private float attackTime = 0.2f;
        [SerializeField] private float recoveryTime = 03f;


        private AttackState state = PlayerAttack.AttackState.Idle;
        private float stateTimer;


        private void Update()
    {
        switch(state)
        {
            case AttackState.Idle:
                if (GameInput.Instance.LightAttackPressed())
                {
                    state = AttackState.Windup;
                    stateTimer = windupTime;
                }
                break;

            case AttackState.Windup:
                RotateWeapon(-90f, windupTime);
                UpdateTimer(AttackState.Attack, attackTime);
                break;

            case AttackState.Attack:
                RotateWeapon(90f, attackTime);
                UpdateTimer(AttackState.Recovery, recoveryTime);
                break;

            case AttackState.Recovery:
                weaponPivot.localRotation = Quaternion.RotateTowards(
                    weaponPivot.localRotation,
                    Quaternion.identity,
                    720f * Time.deltaTime);

                UpdateTimer(AttackState.Idle, 0f);
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

    private void UpdateTimer(AttackState nextState, float nextDuration)
    {
        stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f)
        {
            state = nextState;
            stateTimer = nextDuration;
        }
    }
}