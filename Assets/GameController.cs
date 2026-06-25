using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public ItemState[] buttons;
    public float gameTime = 60f;
    public int pointsPerHit = 10;
    public int penaltyPerMiss = 20;
    public int maxMisses = 3;

    public int score = 0;
    public int misses = 0;
    public bool gameEnded = false;
    public Coroutine gameCoroutine;
    public Coroutine buttonCoroutine;
    public Coroutine waitCoroutine;
    public float velocity = 0.4f;
    private WaitForSeconds buttonActiveWait;
    public int CurrentTime = 60;
    public Sprite MissingSpriteRed;
    public Sprite MissingSpriteBlue;
    public GameObject GameOverObject;
    public GameObject TryAgainObject;
    public GameObject StartObject;
    public GameObject WinnerObject;
    public GameObject EndObject;
    public Image TimerImage;
    public GameObject Ballsobjects;
    public TextMeshProUGUI CountText;
    public GameObject AllHideObjects;
    public Image Life;
    public Sprite[] LifeSprites;
    public TextMeshProUGUI PointsText;
    public TextMeshProUGUI PointsWinnerText;
    public TextMeshProUGUI PointsGameOverText;

    public Image fill1;
    public Image fill2;

    public TMP_InputField inputFieldName;
    public Button button;

    public TextMeshProUGUI SpeedText;
    public TextMeshProUGUI MinScoreText;

    public Image imageToFade;
    public float fadeDuration = 2.0f;
    public float waitTime = 8.0f;
    public GameObject ButtonNextRanking;

    public bool canFade = true;
    public int minScore = 80;

    private float roundStartTime;

    public void ResetFadeState()
    {
        if (canFade)
        {
            Color newColor = imageToFade.color;
            newColor.a = 0f;
            imageToFade.color = newColor;
            imageToFade.gameObject.SetActive(false);
            ButtonNextRanking.SetActive(true);
        }
    }

    IEnumerator FadeObject()
    {
        while (true)
        {
            if (canFade)
            {
                ButtonNextRanking.SetActive(false);
                imageToFade.gameObject.SetActive(true);
                float timer = 0f;
                while (timer < fadeDuration)
                {
                    timer += Time.deltaTime;
                    float alpha = timer / fadeDuration;
                    Color newColor = imageToFade.color;
                    newColor.a = alpha;
                    imageToFade.color = newColor;
                    yield return null;
                }

                yield return new WaitForSeconds(waitTime - (fadeDuration * 2));

                timer = 0f;
                while (timer < fadeDuration)
                {
                    timer += Time.deltaTime;
                    float alpha = 1f - (timer / fadeDuration);
                    Color newColor = imageToFade.color;
                    newColor.a = alpha;
                    imageToFade.color = newColor;
                    yield return null;
                }

                imageToFade.gameObject.SetActive(false);
                yield return new WaitForSeconds(3f);
            }
            else
            {
                yield return null;
            }
        }
    }

    public void SetSpeed(bool increase)
    {
        if (increase)
        {
            velocity += 0.1f;
        }
        else
        {
            if (velocity > 0.2f)
            {
                velocity -= 0.1f;
            }
        }

        velocity = (float)Math.Round(velocity, 1);
        SetGameSpeed(velocity);
    }

    public void SetMinScore(bool increase)
    {
        if (increase)
        {
            minScore += 10;
        }
        else
        {
            minScore -= 10;
        }

        SetGameMinScore(minScore);
    }

    IEnumerator WaitToStartFade()
    {
        yield return new WaitForSeconds(1);
        if (ApiController.Instance != null && ApiController.Instance.CurrentGameDurationSec > 0)
            gameTime = ApiController.Instance.CurrentGameDurationSec;
        else
            gameTime = 30;
        Debug.Log($"[GameController] gameTime resolvido: {gameTime}s");
        StartCoroutine(FadeObject());
    }

    private void Start()
    {
        if (PlayerPrefs.HasKey("GameSpeed"))
        {
            velocity = PlayerPrefs.GetFloat("GameSpeed");
            Debug.Log("encontrou vel: " + velocity);
        }

        LoadGameMinScore();
        LoadUpdatedConfig();
    }

    public void LoadUpdatedConfig() {
        StartCoroutine(WaitToStartFade());
    }

    public void SetGameSpeed(float newSpeed)
    {
        velocity = newSpeed;
        PlayerPrefs.SetFloat("GameSpeed", velocity);
        PlayerPrefs.Save();
    }

    void LoadGameMinScore()
    {
        if (PlayerPrefs.HasKey("GameScore"))
        {
            minScore = PlayerPrefs.GetInt("GameScore");
        }
        else
        {
            SetGameMinScore(minScore);
        }
    }

    public void SetGameMinScore(int newScore)
    {
        minScore = newScore;
        PlayerPrefs.SetInt("GameScore", minScore);
        PlayerPrefs.Save();
    }

    void Update()
    {
        bool isInputFieldEmpty = string.IsNullOrEmpty(inputFieldName.text);
        button.interactable = !isInputFieldEmpty;

        SpeedText.text = velocity.ToString();
        MinScoreText.text = minScore.ToString();
    }

    void StartGame()
    {
        misses = 0;
        Life.sprite = LifeSprites[misses];
        gameEnded = false;
        roundStartTime = Time.time;
        DisableAllButtons();
        gameCoroutine = StartCoroutine(GameLoop());
        buttonCoroutine = StartCoroutine(ActivateRandomButton());
    }

    public void ReloadScene()
    {
        SceneManager.LoadScene(0);
    }

    public void ShowEndGame()
    {
        EndObject.SetActive(true);
    }

    public void StartCountdown()
    {
        canFade = false;
        score = 0;
        PointsText.text = score.ToString();
        buttonActiveWait = new WaitForSeconds(velocity);
        fill1.fillAmount = 1f;
        fill2.fillAmount = 1f;
        StartObject.SetActive(false);
        GameOverObject.SetActive(false);
        WinnerObject.SetActive(false);
        TryAgainObject.SetActive(false);
        TimerImage.gameObject.SetActive(true);
        CurrentTime = 60;

        StartCoroutine(Countdown());
    }

    IEnumerator Countdown()
    {
        CountText.text = "5";
        yield return new WaitForSeconds(1);
        CountText.text = "4";
        yield return new WaitForSeconds(1);
        CountText.text = "3";
        yield return new WaitForSeconds(1);
        CountText.text = "2";
        yield return new WaitForSeconds(1);
        CountText.text = "1";
        yield return new WaitForSeconds(1);
        CountText.text = "";
        AllHideObjects.SetActive(true);
        StartGame();
        TimerImage.gameObject.SetActive(false);
        Ballsobjects.gameObject.SetActive(true);
    }

    IEnumerator GameLoop()
    {
        StartCoroutine(DecreaseFillOverTime());
        StartCoroutine(PlayTimer());
        yield return new WaitForSeconds(gameTime);

        EndRound();
    }

    void EndRound()
    {
        StopAllCoroutines();
        gameEnded = true;
        DisableAllButtons();

        if (PrizeManager.Instance == null)
        {
            Debug.LogError("[GameController] PrizeManager.Instance ausente — fallback pra gate local.");
            if (score >= minScore) ShowWinner(); else ShowGameOver();
            return;
        }

        // Servidor decide se ganhou prêmio. Cliente só reporta o score real.
        PrizeManager.Instance.OnPrizeReady.RemoveAllListeners();
        PrizeManager.Instance.OnNoPrize.RemoveAllListeners();
        // Vitória: abre a tela só quando a imagem do prêmio já carregou (ou caiu no fallback).
        PrizeManager.Instance.OnPrizeReady.AddListener(_ => ShowWinner());
        // Derrota: não depende de imagem, abre imediatamente.
        PrizeManager.Instance.OnNoPrize.AddListener(reason =>
        {
            Debug.Log($"[GameController] Sem prêmio. Motivo: {reason}");
            ShowGameOver();
        });

        PrizeManager.LastGameDurationSeconds = Time.time - roundStartTime;
        int scoreToSend = Mathf.Max(0, score);
        PrizeManager.Instance.AwardPrize(score: scoreToSend);
    }

    void ShowWinner()
    {
        if (PointsWinnerText != null)
        {
            PointsWinnerText.text = score.ToString();
        }

        WinnerObject.SetActive(true);
        Debug.Log($"Vencedor com {score} pontos");
    }

    void ShowGameOver()
    {
        if (PointsGameOverText != null)
        {
            PointsGameOverText.text = score.ToString();
        }

        GameOverObject.SetActive(true);
        Debug.Log($"Game Over com {score} pontos");
    }

    IEnumerator DecreaseFillOverTime()
    {
        float timer = 0f;

        while (timer < gameTime)
        {
            float fillAmount = 1f - (timer / gameTime);
            fill1.fillAmount = fillAmount;
            fill2.fillAmount = fillAmount;

            timer += Time.deltaTime;
            yield return null;
        }

        fill1.fillAmount = 0f;
        fill2.fillAmount = 0f;
    }

    IEnumerator PlayTimer()
    {
        while (CurrentTime > 0 && !gameEnded)
        {
            yield return new WaitForSeconds(1f);
            CurrentTime--;
        }
    }

    IEnumerator ActivateRandomButton()
    {
        DisableAllButtons();
        int randomIndex = UnityEngine.Random.Range(0, buttons.Length);
        buttons[randomIndex].EnableItem();
        yield return buttonActiveWait;
        if (!gameEnded)
        {
            buttons[randomIndex].DisableItem();
            buttonCoroutine = StartCoroutine(ActivateRandomButton());
        }
    }

    public void OnButtonClick(int buttonIndex)
    {
        if (buttonCoroutine != null)
        {
            StopCoroutine(buttonCoroutine);
        }
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
        }
        buttonCoroutine = null;
        waitCoroutine = StartCoroutine(WaitToActiveRandomButton());
        if (!gameEnded && buttons[buttonIndex].Enabled)
        {
            score += pointsPerHit;
            PointsText.text = score.ToString();
            buttons[buttonIndex].StartParticle();
            buttons[buttonIndex].DisableItem();
        }
        else if (!gameEnded && !buttons[buttonIndex].Enabled)
        {
            MissedButton();
            Debug.Log(buttons[buttonIndex].ItemImage.sprite.ToString());
            if (buttons[buttonIndex].ItemImage.sprite.name == "Red")
            {
                buttons[buttonIndex].ItemImage.sprite = MissingSpriteRed;
            }
            else
            {
                buttons[buttonIndex].ItemImage.sprite = MissingSpriteBlue;
            }

            StartCoroutine(WaitToResetMissing(buttons[buttonIndex]));
        }
    }

    IEnumerator WaitToActiveRandomButton()
    {
        yield return new WaitForSeconds(0.2f);
        buttonCoroutine = StartCoroutine(ActivateRandomButton());
    }

    IEnumerator WaitToResetMissing(ItemState item)
    {
        yield return new WaitForSeconds(0.7f);
        item.ItemImage.sprite = item.DisabledSprite;
    }

    void MissedButton()
    {
        misses++;
        int spriteIndex = Mathf.Clamp(misses, 0, LifeSprites.Length - 1);
        Life.sprite = LifeSprites[spriteIndex];
        if (score - penaltyPerMiss >= 0) {
            score -= penaltyPerMiss;
        } else {
            score = 0;
        }
        PointsText.text = score.ToString();
        if (misses >= maxMisses)
        {
            EndRound();
            return;
        }
    }

    void DisableAllButtons()
    {
        foreach (ItemState button in buttons)
        {
            button.DisableItem();
        }
    }
}
