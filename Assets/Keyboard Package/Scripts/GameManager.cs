using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public TMP_InputField textBox;
    //[SerializeField] TextMeshProUGUI printBox;
    public GameObject Keyboard;

    private void Start()
    {
        Instance = this;
        //printBox.text = "";
        textBox.text = "";
    }

    public void SetTextbox(TMP_InputField current)
    {
        textBox = current;
        Keyboard.SetActive(true);
    }
    public void DeleteLetter()
    {
        if(textBox.text.Length != 0) {
            textBox.text = textBox.text.Remove(textBox.text.Length - 1, 1);
        }
    }

    public void AddLetter(string letter)
    {
        textBox.text = textBox.text + letter;
    }

    public void SubmitWord()
    {
        //printBox.text = textBox.text;
        //textBox.text = "";
        Debug.Log("Text submitted successfully!");
        Keyboard.SetActive(false);
    }
}
