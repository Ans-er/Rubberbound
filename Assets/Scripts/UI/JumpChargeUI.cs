using UnityEngine;
using UnityEngine.UI;

public class JumpChargeUI : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private GameObject root;

    private void Awake()
    {
        if (root == null) root = gameObject;

        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
        }

        if (root != null)
        {
            root.SetActive(false);
        }
    }

    public void SetCharge01(float charge01, bool visible)
    {
        if (slider != null)
        {
            slider.value = Mathf.Clamp01(charge01);
        }

        if (root != null)
        {
            root.SetActive(visible);
        }
    }
}
