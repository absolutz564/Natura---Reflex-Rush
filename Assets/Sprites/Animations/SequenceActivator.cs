using System.Collections;
using UnityEngine;

public class SequenceActivator : MonoBehaviour
{
    [Header("Objetos que vão aparecer em sequência")]
    public GameObject[] objects;

    [Header("Tempo entre cada objeto")]
    public float delayBetweenObjects = 0.5f;

    [Header("Desativar todos no Start")]
    public bool disableOnStart = true;

    void Start()
    {
        if (disableOnStart)
        {
            foreach (GameObject obj in objects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }

        StartCoroutine(ActivateSequence());
    }

    IEnumerator ActivateSequence()
    {
        foreach (GameObject obj in objects)
        {
            if (obj != null)
            {
                obj.SetActive(true);
                yield return new WaitForSeconds(delayBetweenObjects);
            }
        }
    }
}