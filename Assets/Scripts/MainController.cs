using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainController : MonoBehaviour
{
    public Text EasyWinnerText;
    public float speed = 50f; // velocidade do movimento do texto
    public RectTransform EasyTextTransform;
    public float CanvasWidth;

    private int currentIndex = 0;

    public List<Text> UsernamesTexts = new List<Text>();
    public List<Text> TimersTexts = new List<Text>();

    public Animator Anim;

    public void StartAnim()
    {
        if (Anim)
        {
            Anim.SetTrigger("StartAnim");
        }
    }

    // Start is called before the first frame update
    void Start()
    {
#if UNITY_STANDALONE
        Screen.SetResolution(534, 960, true);
        Screen.fullScreen = true;
#endif
        EasyTextTransform = EasyWinnerText.GetComponent<RectTransform>();
        CanvasWidth = GameObject.Find("Canvas").GetComponent<RectTransform>().rect.width * 2;

        SetRanking();
    }

    // Update is called once per frame
    void Update()
    {
        //float newX = EasyTextTransform.anchoredPosition.x - speed * Time.deltaTime;

        //if (newX < -CanvasWidth / 2f)
        //{
        //    newX = CanvasWidth / 2f;

        //}

        //EasyTextTransform.anchoredPosition = new Vector2(newX, EasyTextTransform.anchoredPosition.y);
    }

    void SetRanking()
    {
        for (int i = 0; i <= 3; i++)
        {
            currentIndex = i;
            string scorekey = GetKeyByLevel();
            if (PlayerPrefs.HasKey(scorekey))
            {
                string namekey = scorekey.Replace("score", "username");


                string username = PlayerPrefs.GetString(namekey);
                float score = PlayerPrefs.GetInt(scorekey);
                string levelname = GetLevel(scorekey);

                string mensagem = string.Format("{0} conseguiu o nível {2} em {1} segundos.", username, score, levelname);

                EasyWinnerText.text = mensagem;
                Debug.Log(mensagem);

                UsernamesTexts[currentIndex].text = username;
                TimersTexts[currentIndex].text = ConvertToTimeFormat((int) score);

            }
        }
    }

    public string ConvertToTimeFormat(int value)
    {
        int minutes = value / 60;
        int seconds = value % 60;
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }


    string GetKeyByLevel()
    {
        if (currentIndex == 0)
        {
            return "score_easy";
        }
        if (currentIndex == 1)
        {
            return "score_medium";
        }
        if (currentIndex == 2)
        {
            return "score_hard";
        }

        return "score_expert";
    }

    string GetLevel(string id)
    {
        if (id == "score_easy")
        {
            return "Fácil";
        }
        if (id == "score_medium")
        {
            return "Médio";
        }
        if (id == "score_hard")
        {
            return "Difícil";
        }

        return "Expert";
    }

    public void GoToRegister(int selectedGame)
    {
        if (selectedGame == 0)
        {
            PlayerPrefs.SetInt("SelectedGame", 0);
        }
        else
        {
            PlayerPrefs.SetInt("SelectedGame", 1);
        }
        SceneManager.LoadScene("Register");
    }

    public void GoToGame()
    {
        PlayerPrefs.SetInt("SelectedGame", 1);
        SceneManager.LoadScene("JogoDaMemoria");
    }
}
