using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TotemApiUI : MonoBehaviour
{
    public TotemApiClient api;
    public TMP_InputField eventInput;
    public TMP_InputField batchInput;
    public TMP_Text output;

    public void Start()
    {
        Log("Sistema pronto.");
        Btn_GetStock();
    }

    private void Log(string msg)
    {
        //output.text = msg;
        Debug.Log(msg);
    }

    public void Btn_GetStock()
    {
        StartCoroutine(api.GetStock(Log));
    }

    public void Btn_PostEvent()
    {
        StartCoroutine(api.PostEvent(eventInput.text, Log));
    }

    public void Btn_PostBatch()
    {
        StartCoroutine(api.PostBatch(batchInput.text, Log));
    }

    public void Btn_Ping()
    {
        StartCoroutine(api.Ping(Log));
    }
}
