using UnityEngine;
using System.Collections;

public class PulseEffect : MonoBehaviour
{
    [Header("Pulse")]
    public float minScale = 0.95f;
    public float maxScale = 1.05f;
    public float speed = 1.2f;

    [Header("Animator")]
    public Animator animatorToDisable;

    [Tooltip("Tempo para desativar o Animator")]
    public float disableAnimatorAfter = 2f;

    private Vector3 baseScale;
    private float timer;

    private void Awake()
    {
        baseScale = transform.localScale;

        // Caso não arraste no Inspector
        if (animatorToDisable == null)
            animatorToDisable = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        transform.localScale = baseScale;
        timer = 0f;

        if (animatorToDisable != null)
        {
            animatorToDisable.enabled = true;

            StartCoroutine(DisableAnimator());
        }
    }

    private void Update()
    {
        timer += Time.deltaTime;

        float t = (Mathf.Sin(timer * speed * Mathf.PI * 2f) + 1f) / 2f;
        float s = Mathf.Lerp(minScale, maxScale, t);

        transform.localScale = baseScale * s;
    }

    private IEnumerator DisableAnimator()
    {
        yield return new WaitForSeconds(disableAnimatorAfter);

        if (animatorToDisable != null)
        {
            animatorToDisable.enabled = false;
        }
    }

    private void OnDisable()
    {
        transform.localScale = baseScale;
    }
}