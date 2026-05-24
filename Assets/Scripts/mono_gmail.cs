using UnityEngine;
using System.Collections;
using System;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public class mono_gmail : MonoBehaviour
{
    public string Receiver;

    public string Body;
    public string Style;
    public string urlPdf;
    public QuizController quizController;
    public string UserName;
    private void Awake()
    {
        urlPdf = quizController.Urls[quizController.SelectedIndex];
        Body = quizController.YourStyleDesc.text;

        Style = quizController.YourStyleTitle.text;
        Receiver = LoginController.Instance.Email;
        UserName = LoginController.Instance.Name;
    }
    private void Start()
    {
        Invoke("Main", 2f);
    }

    void Main()
    {
        MailMessage mail = new MailMessage();

        mail.From = new MailAddress("fashion4likes@gmail.com");
        mail.To.Add(Receiver);
        mail.Subject ="Olá " + UserName + " Seu estilo é " + Style;
        mail.Body = Body + "\n \n \n \n \n " + urlPdf;

        SmtpClient smtpServer = new SmtpClient("smtp.gmail.com");
        smtpServer.Port = 587;
        smtpServer.Credentials = new System.Net.NetworkCredential("fashion4likes@gmail.com", "112233DILIS112233") as ICredentialsByHost; smtpServer.EnableSsl = true;
        ServicePointManager.ServerCertificateValidationCallback = delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        };

        mail.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;
        smtpServer.Send(mail);
        Debug.Log("success");

    }
}