using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;
    public bool IsCursorUnlocked { get; private set; }
    private bool isPaused;
    private bool hasLockedOnce = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        IsCursorUnlocked = false;
        Debug.Log($"LockCursor : {Cursor.lockState}, Visible={Cursor.visible}");
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsCursorUnlocked = true;
        Debug.Log($"UnlockCursor : {Cursor.lockState}, Visible={Cursor.visible}");
    }

    void Update()
    {
    #if UNITY_EDITOR
    //エディターでは最初のクリックでカーソルロック
        if (!hasLockedOnce)
        {
            LockCursor();
            hasLockedOnce = true;
            return;
        }
    #else
    //ビルド版では起動時に一度だけロック
        if (!hasLockedOnce)
        {
            LockCursor();
            hasLockedOnce = true;
            return;
        }
    #endif
    //一度ロックした後はEscだけで切り替える
        if (hasLockedOnce && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsCursorUnlocked)
            {
                LockCursor();
            }
            else
            {
                UnlockCursor();
            }
        }
    }
}
