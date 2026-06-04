using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// =====================================================================
// PrizeManager — wrapper genérico do ApiController focado em UI de prêmio.
// Funciona com dois fluxos:
//
//   A) "Cena de prêmio dedicada" (ex.: Lux/Bolhas)
//      No GameOver do seu jogo:
//          PrizeManager.PrepareForScore(score, duracaoSegundos);
//          SceneManager.LoadScene("PrizeScene");
//      Na PrizeScene tenha um GameObject com PrizeManager.autoAwardOnStart=true.
//      Ele consome os statics, chama CompleteRound, renderiza nome/imagem.
//
//   B) "Tela única" (ex.: Natura Jackpot)
//      No Start do seu jogo:
//          PrizeManager.Instance.FetchChance(c => winChance = Mathf.RoundToInt(c.chance));
//      Quando o jogador ganhar:
//          PrizeManager.Instance.AwardPrize(discreteOutcome: 1);
//      Eventos OnPrizeAwarded / OnNoPrize disparam pra você atualizar UI extra.
//
// Para SCORE_ENDLESS use PrepareForScore(score, duration).
// Para JACKPOT_ROSACEA/ROLETA, passe discreteOutcome no AwardPrize.
// =====================================================================

public class PrizeManager : MonoBehaviour
{
    public enum GameTypeOption { SCORE_ENDLESS, JACKPOT_ROSACEA, ROLETA }

    public static PrizeManager Instance { get; private set; }

    [Header("Configuração da rodada")]
    public GameTypeOption gameType = GameTypeOption.SCORE_ENDLESS;
    [Tooltip("Duração default em segundos se nada for passado via PrepareFor*")]
    public int defaultDurationSec = 30;
    [Tooltip("Ao iniciar a cena já dispara AwardPrize com os statics preparados (fluxo A).")]
    public bool autoAwardOnStart = false;

    [Tooltip("Faz GET /round/preview em background no Start. Necessário pra resolver offline (popula cache).")]
    public bool autoFetchPreviewOnStart = true;

    [Header("UI (opcional)")]
    public Image prizeImage;
    public TextMeshProUGUI prizeNameText;
    public Sprite fallbackSprite;
    [Tooltip("Painel mostrado quando não há prêmio (TIER_NO_STOCK, period cap, etc.)")]
    public GameObject noPrizePanel;
    [Tooltip("Se preenchido e não houver prêmio, carrega essa cena.")]
    public string noPrizeSceneName;

    [Header("Eventos")]
    public GiftEvent OnPrizeAwarded;
    public NoPrizeEvent OnNoPrize;
    public ChanceEvent OnChanceLoaded;

    // ----- Statics pra repassar contexto entre cenas (fluxo A) -----
    public static float LastGameDurationSeconds = 0f;
    public static int? LastScore = null;
    public static int? LastDiscreteOutcome = null;

    // ----- Cache da última chance buscada (fluxo B) -----
    public static ChanceData CurrentChance;
    public static Gift LastAwardedGift;

    void Awake()
    {
        // Singleton "soft" — sobrescreve em cada cena (não usa DontDestroyOnLoad
        // porque cada cena pode ter UI própria pro prêmio).
        Instance = this;

        // UnityEvents só são inicializados automaticamente quando vêm do Inspector.
        // Como o componente pode ser adicionado em runtime (AddComponent), garantimos aqui.
        if (OnPrizeAwarded == null) OnPrizeAwarded = new GiftEvent();
        if (OnNoPrize == null)     OnNoPrize     = new NoPrizeEvent();
        if (OnChanceLoaded == null) OnChanceLoaded = new ChanceEvent();
    }

    void Start()
    {
        if (autoFetchPreviewOnStart)
            FetchPreview();

        if (autoAwardOnStart)
            AwardPrize(LastScore, LastDiscreteOutcome);
    }

    // ----------- Helpers estáticos (preparação pré-cena) -----------
    public static void PrepareForScore(int score, float durationSeconds)
    {
        LastScore = score;
        LastDiscreteOutcome = null;
        LastGameDurationSeconds = durationSeconds;
    }

    public static void PrepareForJackpot(int discreteOutcome, float durationSeconds)
    {
        LastDiscreteOutcome = discreteOutcome;
        LastScore = null;
        LastGameDurationSeconds = durationSeconds;
    }

    public static void ResetPrepared()
    {
        LastScore = null;
        LastDiscreteOutcome = null;
        LastGameDurationSeconds = 0f;
    }

    // ----------- API pública -----------

    /// Busca a chance da API e atualiza CurrentChance + dispara OnChanceLoaded.
    /// Use no Start de jogos baseados em probabilidade.
    public void FetchChance(Action<ChanceData> done = null)
    {
        if (!EnsureApi()) { done?.Invoke(null); return; }
        StartCoroutine(FetchChanceCoroutine(done));
    }

    /// Busca preview do próximo prêmio (sem consumir estoque). Útil pra UI
    /// que mostra o "alvo" durante o jogo, ou pra pré-cachear imagem.
    public void FetchPreview(Action<PreviewData> done = null)
    {
        if (!EnsureApi()) { done?.Invoke(null); return; }
        StartCoroutine(ApiController.Instance.GetPreview(gameType.ToString(), p =>
        {
            if (p?.preview != null) Debug.Log($"[PrizeManager] Preview: {p.preview.candidateGiftName} ({p.preview.chancePct}%)");
            done?.Invoke(p);
        }));
    }

    /// Chama CompleteRound e dispara UI/eventos. Use no fim da rodada.
    /// Para SCORE_ENDLESS passe score; para JACKPOT/ROLETA passe discreteOutcome.
    public void AwardPrize(int? score = null, int? discreteOutcome = null)
    {
        if (!EnsureApi()) return;
        StartCoroutine(AwardCoroutine(score, discreteOutcome));
    }

    /// Reinicia a cena atual (atalho usado por botões de UI).
    /// Qualificado com namespace completo pra evitar conflito com possíveis classes "SceneManager" do projeto.
    public void ReloadScene()
    {
        var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.LoadScene(s.buildIndex);
    }

    /// Carrega uma cena por nome (atalho pra botões).
    public void LoadScene(string sceneName)
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    /// Mostra ou esconde os GameObjects de prizeImage e prizeNameText.
    /// Útil quando win/loss compartilham o mesmo panel e só os elementos de prêmio
    /// devem desaparecer no caso de loss.
    public void SetPrizeUIVisible(bool visible)
    {
        if (prizeImage != null)    prizeImage.gameObject.SetActive(visible);
        if (prizeNameText != null) prizeNameText.gameObject.SetActive(visible);
    }

    // ----------- Internas -----------

    private IEnumerator FetchChanceCoroutine(Action<ChanceData> done)
    {
        yield return ApiController.Instance.GetChance(c =>
        {
            CurrentChance = c;
            if (c != null) Debug.Log($"[PrizeManager] Chance: {c.chance}% | cap: {c.dispensedInPeriod}/{c.periodCeiling}");
            OnChanceLoaded?.Invoke(c);
            done?.Invoke(c);
        });
    }

    private IEnumerator AwardCoroutine(int? score, int? discreteOutcome)
    {
        float duration = LastGameDurationSeconds > 0 ? LastGameDurationSeconds : defaultDurationSec;
        int durationSec = Mathf.Max(1, Mathf.RoundToInt(duration));

        yield return ApiController.Instance.CompleteRound(
            gameType.ToString(),
            score,
            discreteOutcome,
            durationSec,
            data =>
            {
                ResetPrepared();

                // Servidor decide: sem gift → não há prêmio (loss/no-stock/cap atingido/etc.).
                // O reason vem do `code` retornado pela API (TIER_NO_STOCK, PERIOD_CAP_REACHED, ...).
                if (data == null || data.gift == null || string.IsNullOrEmpty(data.gift.id))
                {
                    string reason = data?.code ?? "NO_GIFT";
                    if (!string.IsNullOrEmpty(data?.message)) reason += $" — {data.message}";
                    HandleNoPrize(reason);
                    return;
                }

                LastAwardedGift = data.gift;
                DisplayPrize(data.gift, data.resolvedOffline);
                OnPrizeAwarded?.Invoke(data.gift);
            }
        );
    }

    private void DisplayPrize(Gift gift, bool offline)
    {
        if (prizeNameText != null)
            prizeNameText.text = gift.name ?? "";

        if (prizeImage == null) return;

        // Sem URL na resposta — mantém fallback (alguns endpoints não devolvem imageUrl).
        if (string.IsNullOrEmpty(gift.imageUrl))
        {
            ApplyFallback();
            return;
        }

        StartCoroutine(ApiController.Instance.GetGiftImage(gift, sp =>
        {
            if (sp != null)
            {
                prizeImage.sprite = sp;
                prizeImage.enabled = true;
            }
            else ApplyFallback();
        }));

        if (offline) Debug.LogWarning("[PrizeManager] Prêmio resolvido offline — será confirmado quando voltar online.");
    }

    private void ApplyFallback()
    {
        if (prizeImage == null) return;
        prizeImage.enabled = true; // nunca desabilita o componente — fica visível mesmo sem sprite
        if (fallbackSprite != null) prizeImage.sprite = fallbackSprite;
        else Debug.LogWarning("[PrizeManager] Imagem do brinde não pôde ser carregada e fallbackSprite não está configurado no inspector.");
    }

    private void HandleNoPrize(string reason)
    {
        Debug.LogWarning($"[PrizeManager] Sem prêmio. Motivo: {reason}");
        OnNoPrize?.Invoke(reason);

        if (noPrizePanel != null) noPrizePanel.SetActive(true);
        if (!string.IsNullOrEmpty(noPrizeSceneName)) UnityEngine.SceneManagement.SceneManager.LoadScene(noPrizeSceneName);
    }

    private bool EnsureApi()
    {
        if (ApiController.Instance == null)
        {
            Debug.LogError("[PrizeManager] ApiController.Instance ausente. Coloque ApiController numa cena anterior.");
            return false;
        }
        return true;
    }
}

// UnityEvents customizados aparecem no Inspector quando declarados como classes nomeadas.
[Serializable] public class GiftEvent : UnityEvent<Gift> { }
[Serializable] public class NoPrizeEvent : UnityEvent<string> { }
[Serializable] public class ChanceEvent : UnityEvent<ChanceData> { }
