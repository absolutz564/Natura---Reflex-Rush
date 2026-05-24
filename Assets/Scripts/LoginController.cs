using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;

public class LoginController : MonoBehaviour
{
    public string Name;
    public string Email;
    public string Fone;

    public GameObject LoginObject;
    public GameObject QuizObject;

    public InputField NameInput;
    public InputField EmailInput;
    public InputField FoneInput;


    public Button StartButton;

    public static LoginController Instance;

    private void Awake()
    {
        Instance = this;
        //Set screen size for Standalone

    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            // Normalize the domain
            email = Regex.Replace(email, @"(@)(.+)$", DomainMapper,
                                  RegexOptions.None, TimeSpan.FromMilliseconds(200));

            // Examines the domain part of the email and normalizes it.
            string DomainMapper(Match match)
            {
                // Use IdnMapping class to convert Unicode domain names.
                var idn = new IdnMapping();

                // Pull out and process domain name (throws ArgumentException on invalid)
                string domainName = idn.GetAscii(match.Groups[2].Value);

                return match.Groups[1].Value + domainName;
            }
        }
        catch (RegexMatchTimeoutException e)
        {
            return false;
        }
        catch (ArgumentException e)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public void GoToGame()
    {
        Name = NameInput.text;
        Email = EmailInput.text;
        Fone = FoneInput.text;
        SaveUser(Email, Name, Fone);
        if (PlayerPrefs.GetInt("SelectedGame") == 0)
        {
            SceneManager.LoadScene("Quiz");
        }
        else
        {
            SceneManager.LoadScene("JogoDaMemoria");
        }
    }

    public void SaveUser(string email, string name, string fone)
    {
        Usuario novoUsuario = new Usuario();
        novoUsuario.email = email;
        novoUsuario.nome = name;
        novoUsuario.telefone = fone;

        string limitedName = novoUsuario.nome.Substring(0, Mathf.Min(novoUsuario.nome.Length, 16));
        PlayerPrefs.SetString("CurrentUser", limitedName);
        string path = Application.dataPath + "/usuarios.json";

        if (!File.Exists(path))
        {
            List<Usuario> listaUsuarios = new List<Usuario>();
            listaUsuarios.Add(novoUsuario);

            string json = JsonConvert.SerializeObject(listaUsuarios, Formatting.Indented);

            File.WriteAllText(path, json);
        }
        else
        {
            string conteudoArquivo = File.ReadAllText(path);
            List<Usuario> listaUsuarios = JsonConvert.DeserializeObject<List<Usuario>>(conteudoArquivo);

            listaUsuarios.Add(novoUsuario);

            string json = JsonConvert.SerializeObject(listaUsuarios, Formatting.Indented);

            File.WriteAllText(path, json);
        }
    }
    public void OnInputChange()
    {
        if(NameInput.text != "" && EmailInput.text != "" && IsValidEmail(EmailInput.text))
        {
            StartButton.interactable = true;
        }
        else
        {
            StartButton.interactable = false;
        }
    }

}

[System.Serializable]
public class Usuario
{
    public string email;
    public string nome;
    public string telefone;
}
