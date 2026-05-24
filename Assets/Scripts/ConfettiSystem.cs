using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ConfettiSystem : MonoBehaviour
{
    public int confettiCount = 80;
    public float fallSpeedMin = 280f;
    public float fallSpeedMax = 560f;

    private struct PieceData
    {
        public RectTransform rt;
        public float speed;
        public float wobbleOffset;
        public float rotSpeed;
    }

    private List<PieceData> pieces = new List<PieceData>();
    private bool active = false;
    private RectTransform canvasRect;

    private Color[] colors = {
        new Color(0.95f, 0.18f, 0.18f),  // vermelho
        new Color(0.20f, 0.50f, 1.00f),  // azul
        new Color(0.10f, 0.78f, 0.30f),  // verde
        new Color(1.00f, 0.85f, 0.00f),  // amarelo
        new Color(1.00f, 0.48f, 0.00f),  // laranja
        new Color(0.78f, 0.20f, 0.80f),  // roxo
    };

    void Awake()
    {
        Canvas c = GetComponentInParent<Canvas>();
        if (c) canvasRect = c.GetComponent<RectTransform>();
    }

    void Start() {
        PlayConfetti();
    }

    public void PlayConfetti()
    {
        StopAllCoroutines();
        ClearPieces();
        active = true;
        SpawnPieces();
        StartCoroutine(AnimateLoop());
    }

    public void StopConfetti()
    {
        active = false;
        StopAllCoroutines();
        ClearPieces();
    }

    private void SpawnPieces()
    {
        float canvasW = canvasRect != null ? canvasRect.rect.width  : 1080f;
        float canvasH = canvasRect != null ? canvasRect.rect.height : 1920f;

        for (int i = 0; i < confettiCount; i++)
        {
            GameObject go = new GameObject($"C_{i}");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            Image img = go.AddComponent<Image>();
            img.color = colors[i % colors.Length];
            img.raycastTarget = false;

            // 30% pontos circulares, 70% retângulos achatados (como no exemplo)
            bool isDot = Random.value < 0.30f;
            if (isDot)
            {
                float d = Random.Range(7f, 13f);
                rt.sizeDelta = new Vector2(d, d);
            }
            else
            {
                float w = Random.Range(14f, 24f);
                float h = Random.Range(5f, 11f);
                rt.sizeDelta = new Vector2(w, h);
            }

            rt.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

            // Distribui verticalmente para não aparecerem todos juntos
            float startY = Random.Range(-canvasH * 0.3f, canvasH * 0.6f);
            rt.anchoredPosition = new Vector2(
                Random.Range(-canvasW / 2f, canvasW / 2f),
                startY
            );

            pieces.Add(new PieceData
            {
                rt           = rt,
                speed        = Random.Range(fallSpeedMin, fallSpeedMax),
                wobbleOffset = Random.Range(0f, Mathf.PI * 2f),
                rotSpeed     = Random.Range(80f, 220f) * (Random.value < 0.5f ? 1f : -1f),
            });
        }
    }

    private IEnumerator AnimateLoop()
    {
        float elapsed = 0f;
        float canvasW = canvasRect != null ? canvasRect.rect.width  : 1080f;
        float canvasH = canvasRect != null ? canvasRect.rect.height : 1920f;

        while (active)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.rt == null) continue;

                Vector2 pos = p.rt.anchoredPosition;
                pos.y -= p.speed * dt;
                pos.x += Mathf.Sin(elapsed * 1.8f + p.wobbleOffset) * 22f * dt;
                p.rt.anchoredPosition = pos;
                p.rt.Rotate(0f, 0f, p.rotSpeed * dt);

                // Quando sai pela base, reaparece no topo com X aleatório
                if (pos.y < -canvasH / 2f - 40f)
                {
                    pieces[i] = new PieceData
                    {
                        rt           = p.rt,
                        speed        = p.speed,
                        wobbleOffset = Random.Range(0f, Mathf.PI * 2f),
                        rotSpeed     = p.rotSpeed,
                    };
                    p.rt.anchoredPosition = new Vector2(
                        Random.Range(-canvasW / 2f, canvasW / 2f),
                        canvasH / 2f + Random.Range(0f, 60f)
                    );
                }
            }

            yield return null;
        }
    }

    private void ClearPieces()
    {
        foreach (var p in pieces)
            if (p.rt != null) Destroy(p.rt.gameObject);
        pieces.Clear();
    }
}
