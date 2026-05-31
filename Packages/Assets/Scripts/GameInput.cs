using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)] // GameInputを他のスクリプトより先に実行
public class GameInput : MonoBehaviour
{
    public static GameInput Instance { get; private set; }
    private InputSystem_Actions inputActions;

    public Vector2 Move => inputActions.Player.Move.ReadValue<Vector2>();
    public Vector2 Look => inputActions.Player.Look.ReadValue<Vector2>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        inputActions = new InputSystem_Actions();
    }

    void OnEnable()
    {
        inputActions.Player.Enable();
    }

    void OnDisable()
    {
        inputActions.Player.Disable();
    }

    void OnDestroy()
    {
        inputActions.Dispose();
    }
}
