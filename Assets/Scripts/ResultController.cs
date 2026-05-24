using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ResultController : MonoBehaviour
{
    public QuizController quizController;

    public int CurrentIndex;
    public int CurrentSpriteIndex;
    public List<string> Tips;
    public List<Sprite> Icons;
    public List<Sprite> ResultIcons;

    public Text DescText;
    public Text TipText;
    public Image Icon;
    public Image ClothImage;
    public Image ResultBackImage;
    [System.Serializable]
    public class serializableClass
    {
        public List<string> skippedText;
    }
    public List<serializableClass> SelectedIndex = new List<serializableClass>();

    [System.Serializable]
    public class serializableClassSprite
    {
        public List<Sprite> skippedSprites;
    }
    public List<serializableClassSprite> SelectedSpriteIndex = new List<serializableClassSprite>();

    public void SetResult()
    {
        ResultBackImage.sprite = ResultIcons[quizController.GetBestScoreIndex()];
        ResultBackImage.SetNativeSize();
    }

    private void Start()
    {
        DescText.text = SelectedIndex[quizController.GetBestScoreIndex()].skippedText[CurrentIndex];
        Debug.Log(SelectedIndex[quizController.GetBestScoreIndex()].skippedText[CurrentIndex]);

        ClothImage.sprite = SelectedSpriteIndex[quizController.GetBestScoreIndex()].skippedSprites[CurrentSpriteIndex];
        ClothImage.SetNativeSize();
        Icon.sprite = Icons[quizController.GetBestScoreIndex()];
    }


    void SkipSprite()
    {
        CurrentSpriteIndex++;
        if (CurrentSpriteIndex > SelectedSpriteIndex[quizController.GetBestScoreIndex()].skippedSprites.Count - 1)
        {
            CurrentSpriteIndex = 0;
        }
        ClothImage.sprite = SelectedSpriteIndex[quizController.GetBestScoreIndex()].skippedSprites[CurrentSpriteIndex];
        ClothImage.SetNativeSize();
    }

    public void Skip(bool isNext)
    {
        SkipSprite();
        if (isNext)
        {
            CurrentIndex++;
            if (CurrentIndex > SelectedIndex[quizController.GetBestScoreIndex()].skippedText.Count -1)
            {
                CurrentIndex = 0;
            }
            else if(CurrentIndex > SelectedIndex[quizController.GetBestScoreIndex()].skippedText.Count - 2)
            {
                TipText.transform.parent.gameObject.SetActive(true);
                TipText.text = Tips[quizController.GetBestScoreIndex()];
            }
            DescText.text = SelectedIndex[quizController.GetBestScoreIndex()].skippedText[CurrentIndex];
        }
        else
        {
            CurrentIndex--;
            if (CurrentIndex < 0)
            {
                CurrentIndex = SelectedIndex[quizController.GetBestScoreIndex()].skippedText.Count - 1;
            }
                DescText.text = SelectedIndex[quizController.GetBestScoreIndex()].skippedText[CurrentIndex];
        }
    }
}
