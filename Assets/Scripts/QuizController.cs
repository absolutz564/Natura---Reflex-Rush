using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class QuizController : MonoBehaviour
{
    public int SelectedIndex;

    public int CurrentIndex;
    public List<string> Urls;
    public List<int> Scores = new List<int>();
    public List<string> Titles;
    public List<string> TextsQuest1;
    public List<string> TextsQuest2;
    public List<string> TextsQuest3;
    public List<string> TextsQuest4;
    public List<string> TextsQuest5;
    public List<string> TextsQuest6;
    public List<string> TextsQuest7;
    public List<string> TextsQuest8;

    public Text TitleText;
    public List<Text> QuestionTexts;

    public Text YourStyleTitle;
    public Text YourStyleDesc;

    public GameObject Result;

    public List<Transform> ButtonsTransform;

    public Text CounterText;
    public Button BtnNext;

    int Index;
    public List<Sprite> QrCodes;
    public Image QRCodeImage;

    public Text QrCodeText;
    public Color DefaultTextColor;
    public Image FillBar;

    public Survey SurveyData;
    public void SetQRCodeByIndex()
    {
        if (QrCodeText != null)
        {
            QrCodeText.text = YourStyleTitle.text;
        }

        QRCodeImage.sprite = QrCodes[Index];
    }
    public void GoToLogin()
    {
        SceneManager.LoadScene("Main");
    }

    void ResetScores()
    {
        for (int i1 = 0; i1 < Scores.Count; i1++)
        {
            Scores[i1] = 0;
        }
    }

    public void ResetQuiz()
    {
        ResetScores();
        Result.SetActive(false);
        CurrentIndex = 0;
        UpdateQuest();
    }

    private void Start()
    {
        UpdateQuest();
    }
    List<string> GetQuestionByIndex(int index)
    {
        if (index == 0)
        {
            return TextsQuest1;
        }
        else if (index == 1)
        {
            return TextsQuest2;
        }
        else if (index == 2)
        {
            return TextsQuest3;
        }
        else if (index == 3)
        {
            return TextsQuest4;
        }
        else if (index == 4)
        {
            return TextsQuest5;
        }
        else if (index == 5)
        {
            return TextsQuest6;
        }
        else if (index == 6)
        {
            return TextsQuest7;
        }
        else if (index == 7)
        {
            return TextsQuest8;
        }
        else
        {
            return null;
        }

    }
    public void UpdateQuest()
    {
        CounterText.text = "(" + (CurrentIndex+1) + "/8)";
        if (CurrentIndex < Titles.Count)
        {
            TitleText.text = Titles[CurrentIndex];
            int i = 0;
            foreach (Text txt in QuestionTexts)
            {

                txt.text = GetQuestionByIndex(CurrentIndex)[i];
                i++;
            }
        }
        else
        {
            SetStyleByBestScoreIndex(GetBestScoreIndex());
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            GoToLogin();
        }
    }

    public void NextQuestion()
    {
        ResetCheckbox();
        Scores[SelectedIndex] += 1;
        CurrentIndex++;
        FillBar.fillAmount += 0.125f;
        UpdateQuest();
    }

    void ResetCheckbox()
    {
        foreach(Transform t in ButtonsTransform)
        {
            t.GetComponentInChildren<Toggle>().isOn = false;
            t.GetComponentInChildren<Text>().color = DefaultTextColor;
        }
    }
    public void SetScoreByIndex(int index)
    {
        BtnNext.interactable = true;
        ResetCheckbox();
        SelectedIndex = index;
        ButtonsTransform[index].GetComponentInChildren<Toggle>().isOn = true;
        ButtonsTransform[index].GetComponentInChildren<Text>().color = Color.yellow;
    }

    public int GetBestScoreIndex()
    {
        int value = 0;
        Index = 0;
        for (int i = 0; i < Scores.Count; i++)
        {
            if (Scores[i] > value)
            {
                value = Scores[i];
                Index = i;
            }
        }
        Debug.Log("O indice mais escolhido foi " + Index);
        return Index;
    }
    void SetStyleByBestScoreIndex(int index)
    {
        SurveyData.OnSendData();
        Result.SetActive(true);
        if (index == 0)
        {
            YourStyleTitle.text = "Clássico";
            YourStyleDesc.text = "Seu estilo é clássico! Este estilo inclui itens que todo mundo deveria ter no guarda-roupa. Investe em qualidade e peças que nunca saem de moda. Você certamente não costuma usar cores mais fortes, com transparência ou decotes – mas tem convicção que, na moda, “menos é mais”. \nDica: Procure inserir peças mais ousadas e coloridas para dar uma equilibrada.";
            Debug.Log("Clássico (Maioria das Respostas: Letra A)");
        }
        else if (index == 1)
        {
            YourStyleTitle.text = "Elegante";
            YourStyleDesc.text = "Uau! Seu estilo é elegante! Você provavelmente se preocupa bastante com a sua aparência, não é? Prioriza roupas duráveis, materiais de qualidade refinada, mas, principalmente, está sempre de olho nas tendências. O estilo elegante é utilizado por pessoas de opinião forte, seguras, sofisticadas, exigentes e muito respeitadas. \nDica: Busque relaxar um pouco e mixar seu estilo com alguma peça mais despojada, para ter mais conforto no dia-a - dia.";
            Debug.Log("Elegante(Maioria das Respostas: Letra B)");
        }
        else if (index == 2)
        {
            YourStyleTitle.text = "Moderno";
            YourStyleDesc.text = "Seu estilo é o moderno! Também conhecido como dramático urbano, significa que você é uma pessoa muito ligada à vida urbana e isso se reflete nos seus looks. Certamente, não faltam peças na cor preta e com estampas geométricas em seu guarda-roupa. As peças-chave deste estilo são jeans destroyed, blusas e casacos volumosos e/ou com gola alta e jaqueta de couro. Peças estruturadas e de design marcante, coturno, plataforma e outros sapatos pesados também fazem parte de seus looks. \n Dica: Que tal mixar seu estilo com alguma peça mais leve e diferente, como os estilos romântico e sexy? Assim, você pode conseguir um resultado muito legal em proporções e cores.";
            Debug.Log("Moderno (Maioria das Respostas: Letra C)");
        }
        else if (index == 3)
        {
            YourStyleTitle.text = "Esportivo ou Natural";
            YourStyleDesc.text = "Olha, o seu estilo é o casual/esportivo! Você certamente preza pela praticidade e põe seu conforto em primeiro lugar. Você transmite uma imagem leve, alegre e despretensiosa. Algumas peças-chaves do seu estilo são o jeans, a malha, as mochilas, as rasteirinhas e sandálias anabella. E claro que não pode faltar aquela jeans com um tênis confortável, claro. Para você, o básico sempre funciona. \nDica: Dá para manter o conforto e a discrição com um pouco de cor.Aposte em alguns acessórios coloridos, assim você conseguirá dar uma variada nos looks.";
            Debug.Log("Esportivo ou Natural (Maioria das Respostas: Letra D)");
        }
        else if (index == 4)
        {
            YourStyleTitle.text = "Sexy";
            YourStyleDesc.text = "Uau! O seu estilo é ousadia e poder… Ops! Quis dizer:  SEXY! Você provavelmente é uma pessoa determinada, atraente, dominadora e se sente confortável com seu próprio corpo, por isso, busca peças que valorizam e evidenciam o seu corpo. Peças com decote, transparência, recortes e com um bom caimento não podem faltar no seu closet. Inclusive, o animal print é uma estampa que não sai de lá, não é? Ousadia e poder são seus sobrenomes! \nDica: Esse é um estilo que merece atenção para manter o equilíbrio.Então, escolha o que vai ser o destaque da vez.Um decote mais cavado pode harmonizar muito bem com uma calça mais larguinha.Se você é homem, um shortinho mais curto vai melhor com uma blusa mais folgada.";
            Debug.Log("Sexy (Maioria das Respostas: Letra E)");
        }
        else if (index == 5)
        {
            YourStyleTitle.text = "Romântico";
            YourStyleDesc.text = "Awm! O seu estilo é o romântico. *-* Você deve ter uma preferência por roupas e acessórios fofos e delicados, tons suaves e tecidos leves, não é? Suas roupas e acessórios transmitem a sua personalidade romântica, tímida, delicada, gentil e refinada. Provavelmente você é alguém que ama e suspira assistindo a filmes de comédia romântica ou lendo romances. Sobre moda, uma coisa é certa: seus looks são repletos de sentimentos. \nDica: Busque dar uma quebrada quando o look estiver muito tom pastel, equilibrando com algumas cores mais sóbrias e / ou peças mais sexys.";
            Debug.Log("Romântico (Maioria das Respostas: Letra F)");
        }
        else if (index == 6)
        {
            YourStyleTitle.text = "Criativo";
            YourStyleDesc.text = "Seu estilo é criativo! Você busca utilizar a roupa como forma de expressão e isso é o máximo! Você usa a moda a seu favor e pode transitar por mais de um estilo. Você não apenas segue as tendências, porque você lança sua própria trend com personalidade e de acordo com seu humor. Possui uma personalidade inovadora, criativa, artística, divertida, exótica e confiante. \nDica: Esteja atento às ocasiões que você vai e quais são as tendências do momento, assim, você com certeza vai poder usar sua criatividade com mais segurança e, assim, não se sentir deslocado em alguns momentos.";
            Debug.Log("Criativo (Maioria das Respostas: Letra G)");
        }

    }
}
