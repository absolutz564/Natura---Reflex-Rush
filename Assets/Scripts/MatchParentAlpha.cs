using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MatchParentAlpha : MonoBehaviour
{
    public Graphic parentGraphic;

    void Update()
    {
        if (parentGraphic != null)
        {
            if (parentGraphic.gameObject.activeSelf)
            {
                UpdateAlpha(parentGraphic.color.a);
            }
        }
    }

    void UpdateAlpha(float alpha)
    {
        if (TryGetComponent(out Image image))
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }
        else if (TryGetComponent(out TextMeshProUGUI textMeshPro))
        {
            Color color = textMeshPro.color;
            color.a = alpha;
            textMeshPro.color = color;
        }
    }
}
