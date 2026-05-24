using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Quiz : MonoBehaviour
{
    public int Index = 0;
    public List<GameObject> Questions = new List<GameObject>();
    public Image FillBar;
    public float ValueToFill = 0.1f;
    public List<int> ProfileCounter = new List<int>();

    public GameObject EndGameObject;
    public Text ProfileText;
    void Start()
    {
        PopulateProfileLength();

        ValueToFill = 1.0f / Questions.Count;
    }

    void PopulateProfileLength()
    {
        for (int q = 0; q < 6; q++)
        {
            ProfileCounter.Add(0);
        }
    }
    void Update()
    {
        
    }

    public int GetEndIndex(List<int> lista)
    {
        int indiceMaiorValor = 0;
        int maiorValor = lista[0];
        for (int i = 1; i < lista.Count; i++)
        {
            if (lista[i] > maiorValor)
            {
                maiorValor = lista[i];
                indiceMaiorValor = i;
            }
        }
        return indiceMaiorValor;
    }

    public void NextQuestion(int profileId)
    {
        ProfileCounter[profileId]++;
        Index++;
        foreach (GameObject question in Questions)
        {
            question.SetActive(false);
        }

        if (Questions.Count > Index)
        {
            Debug.Log("ainda tem index");
            Questions[Index].SetActive(true);
        }
        else
        {
            Debug.Log("acabou lista, Indice mais votado foi " + GetEndIndex(ProfileCounter) + "    que é " + ConvertProfileByIndex(GetEndIndex(ProfileCounter)));
            EndGameObject.SetActive(true);
            ProfileText.text = ConvertProfileByIndex(GetEndIndex(ProfileCounter));
        }

        FillBar.fillAmount += ValueToFill;
    }

    public string ConvertProfileByIndex(int index)
    {
        if (index == 0)
        {
            return "Os que se cuidam!";
        }

        if (index ==1)
        {
            return "Boca sempre limpa!";
        }
        if (index == 2)
        {
            return "Os que cuidam da família!";
        }
        if (index == 3)
        {
            return "Big parceiro Condor, podemos te ajudar com tudo!";
        }
        if (index == 4)
        {
            return "Maquiadora";
        }
        return "Profissional";
    }

    public void PlayAgain()
    {
        SceneManager.LoadScene("Quiz");
    }

    public void GoToMemoryGame()
    {
        SceneManager.LoadScene("JogoDaMemoria");
    }

    public void GoToMenu()
    {
        SceneManager.LoadScene("MenuScene");
    }
}
