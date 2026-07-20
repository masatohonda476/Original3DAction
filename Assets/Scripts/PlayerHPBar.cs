using UnityEngine;
using UnityEngine.UI;

public class PlayerHPBar : MonoBehaviour
{
    [SerializeField] private PlayerStatus playerStatus;
    [SerializeField] private Slider slider;
    void Start()
    {
        slider.maxValue = playerStatus.MaxHP;
        slider.value = playerStatus.CurrentHP;
    }

    void Update()
    {
        slider.value = playerStatus.CurrentHP;
    }
}
