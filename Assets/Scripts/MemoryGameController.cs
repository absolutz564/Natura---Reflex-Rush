using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MemoryGameController : MonoBehaviour
{
    public GameObject buttonPrefab; // Referência ao prefab do botão
    public int gridSizeX = 4; // Número de colunas da grade
    public int gridSizeY = 4; // Número de linhas da grade
    public Transform gridTransform;
    public List<GameObject> ButtonsList = new List<GameObject>();

    public List<Sprite> MemorySprites = new List<Sprite>();

    public ElementController card1; //Primeira carta escolhida no turno
    public ElementController card2; //Segunda carta escolhida no turno

    public int ObjectsFlipped = 0;

    private int lines; //linhas da matriz
    private int columns; // colunas da matriz
    int score; //pontuacao do jogador
    public float gameTime; //tempo para fim de jogo
    public float startGameTime; //tempo total do jogo
    int timeToStart; // Tempo antes de começar o jogo
    public bool gameStarted = false; //Verifica se o jogo começou
    bool pause = false; //Verifica se o jogo está pausado

    public static MemoryGameController Instance;

    public bool CanClick = true;

    public int Difficulty = 0;

    public List<Button> DifficultyButtons = new List<Button>();

    public GameObject PopupDifficulty;

    public GridLayoutGroup GameGrid;

    public float PlayedTime;

    public Text TimerText;

    public Text MatchFindedText;

    public int MatchFinded = 0;

    public GameObject EndGameObject;
    public Text GameOverText;
    public GameObject RankingObject;
    public Text EndGameTimerText;
    public Text EndGameNameText;

    public Text NameText;
    public Text CounterText;
    public Text AlertText;

    void Awake()
    {
        NameText.text = PlayerPrefs.GetString("CurrentUser");
        Instance = this;
        MatchFindedText.text = "x" + AddLeadingZeroIfNeeded(MatchFinded);
    }

    public void SendMessage(string message, bool animated)
    {
        CancelInvoke("DisableMessage");
        if (message.Length > 3)
        {
            AlertText.fontSize = 80;
        }
        AlertText.gameObject.SetActive(true);

        AlertText.text = message;
        if (animated)
        {
            AlertText.GetComponent<Animator>().SetTrigger("Animation");
        }
        Invoke("DisableMessage", 2);
    }

    public void ShowAll()
    {
        foreach (GameObject bt in ButtonsList)
        {
            StartCoroutine(bt.GetComponent<ElementController>().GirarBotao(bt.GetComponent<ElementController>().versoImage));
        }
        StartCoroutine (WaitToReset());
    }
    
    IEnumerator WaitToReset()
    {
        yield return new WaitForSeconds(3);
        foreach (GameObject bt in ButtonsList)
        {
            bt.GetComponent<ElementController>().Reset();
        }

    }
    void DisableMessage()
    {
        AlertText.gameObject.SetActive(false);
    }
    public IEnumerator StartCountdown()
    {
        AlertText.gameObject.SetActive(false);
        ShowAll();
        CounterText.gameObject.SetActive(true);
        int timer = 3;
        while (timer > 0)
        {
            yield return new WaitForSeconds(1);
            timer--;
            CounterText.text = timer.ToString();
        }

        gameStarted = true;
        CounterText.gameObject.SetActive(false);
    }
    public string AddLeadingZeroIfNeeded(int inputInt)
    {
        string inputString = inputInt.ToString();
        if (inputString.Length == 1)
        {
            return "0" + inputString;
        }
        else
        {
            return inputString;
        }
    }

    public string ConvertToTimeFormat(int value)
    {
        int minutes = value / 60;
        int seconds = value % 60;
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    public void SelectButton(int index)
    {
        foreach (Button difficultyButton in DifficultyButtons)
        {
            difficultyButton.interactable = true;
        }

        Difficulty = index;
        DifficultyButtons[index].interactable = false;
    }
    public void InitConfig()
    {
        PopupDifficulty.SetActive(false);
        if (Difficulty == 0)
        {
            GameGrid.spacing = new Vector2(150, GameGrid.spacing.y); 
            gridSizeX = 2;
            gridSizeY = 4;
        } else if (Difficulty == 1)
        {
            GameGrid.spacing = new Vector2(130, GameGrid.spacing.y);
            gridSizeX = 3;
            gridSizeY = 4;
        } else if (Difficulty == 2)
        {
            GameGrid.spacing = new Vector2(130, 110);
            gridSizeX = 4;
            gridSizeY = 5;
        }
        else
        {
            GameGrid.spacing = new Vector2(110, 100);
            gridSizeX = 5;
            gridSizeY = 6;
        }
        CreateGrid();

        GameStart();
    }

    void Update()
    {
        if (gameStarted && !pause)
        {
            gameTime -= Time.deltaTime;
            PlayedTime += Time.deltaTime;
            //GUIHandleMemory.instance.UpdateGameTime(PlayedTime);

            if (gameTime <= 0)
            {
                //Caso o jogador não tenha pegado todas as cartas, fim de jogo
                GameOver();
            }

            TimerText.text = ConvertToTimeFormat((int) gameTime);
        }
    }

    void GameOver()// Finaliza o jogo - Derrota
    {
        //Para o jogo
        //resolve resultados
        int scoreTotal = (lines * columns) / 2;
        Debug.Log("encontrou apenas " + scoreTotal + "  pares");
        //GUIHandleMemory.instance.ShowLossPopup(score, scoreTotal);
        gameStarted = false;
        gameTime = 0;
        //Time.timeScale = 0;
        EndGameTimerText.text = ConvertToTimeFormat((int) PlayedTime);
        EndGameNameText.text = PlayerPrefs.GetString("CurrentUser");
        RankingObject.SetActive(false);
        GameOverText.gameObject.SetActive(true);
        EndGameObject.SetActive(true);
    }

    private void Start()
    {

    }

    public void InitGame()
    {
        StartCoroutine(StartCountdown());
        //gameStarted = true;
    }

    void GameStart() //Use para começar/recomeçar o jogo
    {
        //allCardsInGame = GameObject.FindGameObjectsWithTag("Card");      
        //DisableBarrier();
        score = 0;
        //GUIHandle.instance.UpdateScore(score);        
        gameTime = startGameTime + 1;
        pause = false;
        gameStarted = false;
    }

    public void ResetCanClick()
    {
        CanClick = false;
        StartCoroutine(ActiveCanClick());
    }

    private IEnumerator ActiveCanClick()
    {
        yield return new WaitForSeconds(0.3f);
        CanClick = true;
    }

    public void AddFlipped(ElementController pickedElement)
    {
        ObjectsFlipped++;
        if (ObjectsFlipped == 1)
        {
            card1 = pickedElement;
            Debug.Log("Você pegou a primeira carta " + card1.Id);
        }
        else if(ObjectsFlipped == 2)
        {
            card2 = pickedElement;
            ObjectsFlipped = 0;
            Debug.Log("Você pegou a segunda carta " + card2.Id);
            CheckMatch();
        }
    }

    void CheckMatch() //Use para verificar se as cartas deram match
    {
        //Verificar se código das cartas são iguais

        //Reseta cartas
        if (card1.Id == card2.Id)
        {
            SendMessage("+1", true);
            Debug.Log("Par encontrado");
            gameTime += 5f;
            score++;
            CheckScore();
            card1.Finded = true;
            card2.Finded = true;
            ResetCards();
            MatchFinded++;
            MatchFindedText.text = "x" + AddLeadingZeroIfNeeded(MatchFinded);

        }
        else
        {
            Debug.Log("Cartas diferentes");
            StartCoroutine(UnflipCards());
        }

    }

    IEnumerator UnflipCards()
    {
        yield return new WaitForSeconds(1);
        card1.Reset();
        card2.Reset();

        ResetCards();
    }

    IEnumerator WaitToEnd()
    {
        yield return new WaitForSeconds(1);
        EndGameObject.SetActive(true);

    }
    void CheckScore() //Verifica se o jogador conseguiu a pontuacao para vencer
    {
        int scoreTotal = (gridSizeX * gridSizeY) / 2;
        if (score >= scoreTotal)
        {
            EndGameTimerText.text = ConvertToTimeFormat((int)PlayedTime);
            EndGameNameText.text = PlayerPrefs.GetString("CurrentUser");
            StartCoroutine(WaitToEnd());
            //Venceu 
            Debug.Log("Você conseguiu o nível " + GetLevel() + " em " + GetSecconds() + " segundos.");
            gameStarted = false;

            string scorekey = GetKeyByLevel();
            string namekey = scorekey.Replace("score", "username");

            if (!PlayerPrefs.HasKey(scorekey))
            {
                PlayerPrefs.SetString(namekey, PlayerPrefs.GetString("CurrentUser"));
                PlayerPrefs.SetInt(scorekey, GetSecconds());
            }
            else if (PlayerPrefs.HasKey(scorekey))
            {
                float score = PlayerPrefs.GetInt(scorekey);
                if (GetSecconds() <= score)
                {
                    PlayerPrefs.SetString(namekey, PlayerPrefs.GetString("CurrentUser"));
                    PlayerPrefs.SetInt(scorekey, GetSecconds());
                }
            }
        }
    }

    string GetKeyByLevel()
    {
        if (Difficulty == 0)
        {
            return "score_easy";
        }
        if (Difficulty == 1)
        {
            return "score_medium";
        }
        if (Difficulty == 2)
        {
            return "score_hard";
        }

        return "score_expert";
    }

    int GetSecconds()
    {
        return (int)PlayedTime;
    }

    string GetLevel()
    {
        if (Difficulty == 0)
        {
            return "Fácil";
        }
        if (Difficulty == 1)
        {
            return "Médio";
        }
        if (Difficulty == 2)
        {
            return "Difícil";
        }

        return "Expert";
    }

    void ResetCards() //Use para resetar as cartas
    {

        card1 = null;
        card2 = null;
    }

    private void CreateGrid()
    {
        // Loop para criar cada botão
        for (int y = 0; y < gridSizeY; y++)
        {
            for (int x = 0; x < gridSizeX; x++)
            {
                // Cria uma nova instância do prefab do botão
                GameObject button = Instantiate(buttonPrefab);

                // Posiciona o botão na grade
                //button.transform.localPosition = new Vector3(x, y, 0);
                ButtonsList.Add(button);
            }
        }
        SetMemorySprites();
        MemoryGameController myClassInstance = GetComponent<MemoryGameController>();
        MemoryGameController.ShuffleListStatic(ButtonsList, myClassInstance);

        int i = 0;
        for (int y = 0; y < gridSizeY; y++)
        {
            for (int x = 0; x < gridSizeX; x++)
            {

                // Posiciona o botão na grade
                ButtonsList[i].transform.SetParent(gridTransform);
                //if (Application.isEditor)
                //{
                //    ButtonsList[i].transform.localScale = Vector3.one;
                //}

                ButtonsList[i].transform.localPosition = new Vector3(x, y, 0);
                i++;
            }
        }

    }



    public void ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Range(0, n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
    public static void ShuffleListStatic<T>(List<T> list, MemoryGameController myClassInstance)
    {
        myClassInstance.ShuffleList(list);
    }

    public void GoToQuiz()
    {
        SceneManager.LoadScene("Quiz");
    }

    public void GoToMenu()
    {
        SceneManager.LoadScene("MenuScene");
    }

    public void ReloadScene()
    {
        SceneManager.LoadScene("JogoDaMemoria");
    }
    public void SetMemorySprites()
    {
        int id = 0;
        int duplicatedId = 0;
        foreach (GameObject o in ButtonsList)
        {
            id++;
            ElementController element = o.GetComponent<ElementController>();
            element.Id = duplicatedId + "memory";
            element.versoImage = MemorySprites[duplicatedId];
            if (id == 2)
            {
                id = 0;
                duplicatedId++;
            }
        }
    }
}