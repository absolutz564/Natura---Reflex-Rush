using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;


public class Survey : MonoBehaviour
{
    public string messageText;
    public string username;
    public string email;
    public int Timer;

    readonly string postURL = "https://fashion4likes.dilisgs.com.br/database/uwr_post.php";
    private float StartTime;

    public Text timerText;
    private void Start()
    {
        messageText = "Press buttons to interact with web server";

    }

    public void StartTimer()
    {
        StartTime = Time.time;
    }
    void Update()
    {
        float TimerControl = Time.time - StartTime;
        string mins = ((int)TimerControl / 60).ToString("00");
        string segs = (TimerControl % 60).ToString("00");
        string milisegs = ((TimerControl * 100) % 100).ToString("00");

        string TimerString = string.Format("{00}:{01}:{02}", mins, segs, milisegs);

        timerText.text = TimerString.ToString();
    }

    public void OnButtonGetScore()
    {
        messageText = "Downloading data...";
    }

    public void OnSendData()
    {
        username = LoginController.Instance.NameInput.text;
        email = LoginController.Instance.EmailInput.text;
        string text = "Username: " + username + "\nEmail: " + email + "\nTempo: " + timerText.text + "\n\n\n";
        //text = "20";
        if (text == string.Empty)
        {
            messageText = "Error: No to send.\nEnter a value.";
        }
        else
        {
            messageText = "Sending data...";
            StartCoroutine(SimplePostRequest(text));
        }
    }

    IEnumerator SimplePostRequest(string curScore)
    {
        List<IMultipartFormSection> wwwForm = new List<IMultipartFormSection>();
        wwwForm.Add(new MultipartFormDataSection("curData", curScore));

        UnityWebRequest www = UnityWebRequest.Post(postURL, wwwForm);

        yield return www.SendWebRequest();

        if (www.isNetworkError || www.isHttpError)
        {
            Debug.LogError(www.error);
        }

        else
        {
            messageText = www.downloadHandler.text;
        }
    }
}