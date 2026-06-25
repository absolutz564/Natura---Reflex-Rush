using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// =====================================================================
// ApiController — integração genérica com a API v2 de gerenciamento de brindes.
// Padrão Singleton MonoBehaviour. Use ApiController.Instance em qualquer jogo.
//
// Fluxo típico:
//   1) Start do jogo: aguardar Instance.LoadEvent (carrega eventId/activationId).
//   2) Jogo de chance (Jackpot/Roleta): Instance.GetChance pra usar % na lógica.
//   3) Jogo por score (Endless): Instance.GetPreview pra mostrar candidato/banda.
//   4) Fim da rodada: Instance.CompleteRound → retorna o Gift sorteado.
//   5) Renderizar prêmio: Instance.GetGiftImage(gift, sprite => ...).
//
// Offline:
//   - Health/ping a cada 15s define IsOnline.
//   - Se offline na hora do CompleteRound, usa o último preview cacheado do
//     gameType pra escolher o brinde local, enfileira o round e envia em batch
//     quando voltar online (/round/complete/batch).
// =====================================================================

[Serializable]
public class ApiConfig
{
    public string unityKey;
    public string baseUrl;
}

public static class ApiConfigLoader
{
    public static ApiConfig Load()
    {
        string rootPath = Directory.GetParent(Application.dataPath).FullName;
        string path = Path.Combine(rootPath, "config", "totem-config.json");

        if (!File.Exists(path))
        {
            Debug.LogError($"[ApiConfig] Arquivo não encontrado: {path}");
            return null;
        }

        try
        {
            return JsonUtility.FromJson<ApiConfig>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ApiConfig] Erro ao parsear: {e.Message}");
            return null;
        }
    }
}

// ---------- Modelos da API v2 ----------
// Envelope padrão: { success, message, code, data }. Como JsonUtility não
// lida bem com genéricos em runtime, criamos um envelope concreto por endpoint.

[Serializable] public class ApiEnvelopeBase { public bool success; public string message; public string code; }

[Serializable] public class HealthData {
    public string status;
    public string eventId;
    public string activationId;
    public string ts;
    public string gameType;       // gameType configurado pra essa ativação
    public int gameDurationSec;   // duração da rodada (usado em SCORE_ENDLESS)
}
[Serializable] public class HealthEnvelope : ApiEnvelopeBase { public HealthData data; }

[Serializable] public class ChanceData { public float chance; public int periodCeiling; public int dispensedInPeriod; }
[Serializable] public class ChanceEnvelope : ApiEnvelopeBase { public ChanceData data; }

[Serializable]
public class Gift
{
    public string id;
    public string name;
    public string category;
    public string imageUrl; // pode não vir da API; verificar com backend
}

[Serializable] public class ScoreRange { public int min; public int max; }
[Serializable] public class PeriodInfo { public bool blocked; public string motivo; }

[Serializable]
public class PreviewCandidate
{
    public string candidateGiftId;
    public string candidateGiftName;
    public float chancePct;
    public string reason;
}

[Serializable]
public class TierCalibration
{
    public ScoreRange scoreRange;
    public int discreteValue;
}

[Serializable]
public class PoolGift
{
    public string giftId;
    public string giftName;
    public int configuredWeight;
    public int remainingStock;      // estoque disponível — base pra premiação offline
    public int effectiveWeight;
    public float chancePctInTier;
}

[Serializable]
public class PreviewTier
{
    public string tierId;
    public string tierLabel;
    public int sortOrder;
    public string outcomeType;
    public TierCalibration calibration;
    public bool isTargetThisRound;
    public bool eligible;
    public float selectionChancePct;
    public int poolTotalWeight;
    public List<PoolGift> poolGifts;
}

[Serializable]
public class PreviewData
{
    public string gameType;
    public string targetTierId;
    public string targetTierLabel;
    public ScoreRange scoreRange;
    public int discreteValue;
    public PreviewCandidate preview;
    public PeriodInfo period;
    public string calibrationSessionId; // sessão de calibração — devolvida no complete
    public List<PreviewTier> tiers;     // faixas com estoque por brinde (controle offline)
}
[Serializable] public class PreviewEnvelope : ApiEnvelopeBase { public PreviewData data; }

[Serializable]
public class ResolvedFrom
{
    public int score;
    public int discreteOutcome;
    public ScoreRange band;
}

[Serializable]
public class CompleteData
{
    public string tierId;
    public string tierLabel;
    public Gift gift;
    public ResolvedFrom resolvedFrom;
    public int stockRemainingForGift;
    public bool resolvedOffline; // true quando o cliente resolveu local (não vem da API)
    public string code;          // OK, TIER_NO_STOCK, PERIOD_CAP_REACHED, ERROR, etc.
    public string message;       // mensagem do servidor (quando houver)
}
[Serializable] public class CompleteEnvelope : ApiEnvelopeBase { public CompleteData data; }

[Serializable] public class BatchResult { public string clientRoundId; public bool success; public string code; public string message; public CompleteData data; }
[Serializable] public class BatchData { public int processed; public int succeeded; public int failed; public List<BatchResult> results; }
[Serializable] public class BatchEnvelope : ApiEnvelopeBase { public BatchData data; }

// ---------- Round enfileirado pra envio offline ----------
[Serializable]
public class PendingRound
{
    public string clientRoundId;
    public string gameType;
    public int score;
    public int discreteOutcome;
    public bool hasScore;
    public bool hasDiscrete;
    public string calibrationSessionId; // sessão do preview, devolvida no complete
    public RoundMetadata metadata;
}

[Serializable] public class RoundMetadata { public int durationSec; public string resolvedOfflineAt; }

[Serializable] public class PendingRoundsWrapper { public List<PendingRound> rounds; }

[Serializable] public class BatchPayload { public List<PendingRound> rounds; }

[Serializable]
public class ImageCacheMeta
{
    public string url;
    public string etag;
    public string lastModified;
    public long checkedAtUnix;
}
[Serializable] public class ImageCacheMetaWrapper { public List<ImageCacheMeta> entries; }

// Hints aprendidos de rodadas online — usados pra resolver offline pelo score real.
[Serializable]
public class OfflineGiftHint
{
    public string gameType;
    public int scoreMin;
    public int scoreMax;
    public string tierId;
    public string tierLabel;
    public Gift gift;
}
[Serializable] public class OfflineGiftHintsWrapper { public List<OfflineGiftHint> entries; }

// =====================================================================
public class ApiController : MonoBehaviour
{
    public static ApiController Instance { get; private set; }

    [Header("Configuração")]
    public bool dontDestroyOnLoad = true;
    [Tooltip("Intervalo entre pings de health pra detectar reconexão (segundos)")]
    public float pingIntervalSec = 15f;

    [Header("Estado (read-only)")]
    public bool IsOnline = false;
    public string EventId;
    public string ActivationId;
    public string CurrentGameType;
    public int CurrentGameDurationSec;
    public ChanceData LastChance;

    [Header("Debug")]
    [Tooltip("Quando true, todas as requisições falham imediatamente (simula sem internet).")]
    public bool DebugForceOffline = false;

    [Header("Cache de imagens")]
    [Tooltip("Segundos durante os quais o cache é servido sem revalidar com o servidor. Default: 1h.")]
    public float imageCacheTtlSec = 3600f;

    [Header("Logs")]
    [Tooltip("Máximo de caracteres do corpo HTTP impresso no log. Acima disso, corta com '…'. 0 = sem limite.")]
    public int logBodyMaxChars = 4000;

    // Atalho seguro: 0 (ou negativo) no Inspector significa "sem limite".
    private int LogBodyMaxChars => logBodyMaxChars > 0 ? logBodyMaxChars : int.MaxValue;

    // Último preview por gameType — usado pra resolver offline e pra UI.
    private readonly Dictionary<string, PreviewData> previewByGameType = new Dictionary<string, PreviewData>();

    // Fila de rounds que ainda não foram confirmados pelo servidor.
    private List<PendingRound> pendingRounds = new List<PendingRound>();

    // Evita que o ping loop e o pre-sync do preview disparem o batch ao mesmo tempo.
    private bool isFlushing;

    private const string PENDING_FILE = "pending_rounds.json";
    private const string LAST_PREVIEWS_FILE = "last_previews.json";
    private const string LAST_HEALTH_FILE = "last_health.json";
    private const string IMG_META_FILE = "image_cache_meta.json";
    private const string OFFLINE_HINTS_FILE = "offline_gift_hints.json";
    private const string GIFT_IMG_MAP_FILE = "gift_image_map.json";

    // Caches de imagem (memória + metadata persistida em disco)
    private readonly Dictionary<string, Sprite> imageMemCache = new Dictionary<string, Sprite>();
    private readonly Dictionary<string, ImageCacheMeta> imageMetaCache = new Dictionary<string, ImageCacheMeta>();

    // Histórico de gifts vistos em rodadas online — chave: gameType + tierId
    private List<OfflineGiftHint> giftHints = new List<OfflineGiftHint>();

    // Mapa giftId → imageUrl, aprendido de rodadas online. Usado pra recuperar a imagem
    // (já cacheada em disco pela URL) na premiação offline, onde os poolGifts não trazem URL.
    private readonly Dictionary<string, string> giftImageUrlById = new Dictionary<string, string>();

    private ApiConfig config;

    // ----------- Lifecycle -----------
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        config = ApiConfigLoader.Load();
        if (config == null)
        {
            TryLaunchConfigurator();
        }

        // Invalida caches de premiação de uma ativação anterior (unityKey diferente)
        // ANTES de carregá-los, pra nunca premiar com produto de outro evento/dia.
        EnforceActivationScope();

        LoadPendingRounds();
        LoadCachedPreviews();
        LoadCachedHealth();
        LoadImageMetaCache();
        LoadGiftHints();
        LoadGiftImageMap();
    }

    private const string ACTIVATION_MARKER_FILE = "offline_activation.json";

    // Escopa os caches offline de premiação à ativação atual (unityKey). Se o unityKey mudou
    // desde a última execução — ou se não há marcador (instalação legada) — apaga os caches
    // aprendidos (hints, previews, mapa de imagem) pra não usar dados de outra ativação.
    // Não toca em pending_rounds (não perde rodadas a sincronizar).
    private void EnforceActivationScope()
    {
        if (config == null || string.IsNullOrEmpty(config.unityKey)) return;

        string current = HashKey(config.unityKey);
        string markerPath = GetLocalPath(ACTIVATION_MARKER_FILE);

        string previous = null;
        try { if (File.Exists(markerPath)) previous = File.ReadAllText(markerPath).Trim(); }
        catch (Exception e) { Debug.LogWarning($"[Activation] ler marcador: {e.Message}"); }

        if (previous == current) return; // mesma ativação — mantém os caches

        Debug.LogWarning("[Activation] unityKey novo/alterado — descartando caches offline de premiação da ativação anterior.");
        SafeDelete(GetLocalPath(OFFLINE_HINTS_FILE));
        SafeDelete(GetLocalPath(LAST_PREVIEWS_FILE));
        SafeDelete(GetLocalPath(GIFT_IMG_MAP_FILE));

        try { File.WriteAllText(markerPath, current); }
        catch (Exception e) { Debug.LogError($"[Activation] gravar marcador: {e.Message}"); }
    }

    private static string HashKey(string s)
    {
        using (var sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) { Debug.LogWarning($"[Activation] apagar {path}: {e.Message}"); }
    }

    void Start()
    {
        StartCoroutine(LoadEvent(_ => { }));
        StartCoroutine(PeriodicPingAndSync());
    }

    // ----------- API pública -----------

    /// Carrega dados do evento (eventId/activationId). Chamar no início da aplicação.
    public IEnumerator LoadEvent(Action<HealthData> callback)
    {
        yield return SendRequest("/v2/unity/health", "GET", null, raw =>
        {
            HealthData data = null;
            if (!string.IsNullOrEmpty(raw) && !raw.StartsWith("ERRO"))
            {
                try
                {
                    var env = JsonUtility.FromJson<HealthEnvelope>(raw);
                    if (env != null && env.success && env.data != null)
                    {
                        data = env.data;
                        EventId = data.eventId;
                        ActivationId = data.activationId;
                        if (!string.IsNullOrEmpty(data.gameType)) CurrentGameType = data.gameType;
                        if (data.gameDurationSec > 0) CurrentGameDurationSec = data.gameDurationSec;
                        IsOnline = true;
                        SaveCachedHealth(raw);
                    }
                }
                catch (Exception e) { Debug.LogError($"[Health] parse: {e.Message}"); }
            }
            callback?.Invoke(data);
        });
    }

    /// GET /v2/unity/chance — usar antes de iniciar um jogo baseado em chance.
    public IEnumerator GetChance(Action<ChanceData> callback)
    {
        yield return SendRequest("/v2/unity/chance", "GET", null, raw =>
        {
            ChanceData data = null;
            if (!string.IsNullOrEmpty(raw) && !raw.StartsWith("ERRO"))
            {
                try
                {
                    var env = JsonUtility.FromJson<ChanceEnvelope>(raw);
                    if (env != null && env.data != null)
                    {
                        data = env.data;
                        LastChance = data;
                    }
                }
                catch (Exception e) { Debug.LogError($"[Chance] parse: {e.Message}"); }
            }
            callback?.Invoke(data);
        });
    }

    /// Retorna o último preview cacheado pra um gameType (ou null se não houver).
    /// Usado pelo gerenciador pra decidir o outcome/estoque offline.
    public PreviewData GetCachedPreview(string gameType)
    {
        return previewByGameType.TryGetValue(gameType, out var p) ? p : null;
    }

    /// GET /v2/unity/round/preview?gameType=... — preview sem consumir estoque.
    /// O resultado é cacheado por gameType pra uso offline.
    public IEnumerator GetPreview(string gameType, Action<PreviewData> callback)
    {
        // Opção 1 (à prova de estoque inflado): se há rodadas offline pendentes, sincroniza o
        // batch ANTES de pedir o preview. Assim o servidor já contabilizou as premiações offline
        // e devolve o estoque correto (senão o preview sobrescreveria os decrementos locais com
        // um estoque maior do que o real). Roda só quando há pendências (caso raro).
        if (pendingRounds.Count > 0)
        {
            yield return PingHealth();           // confirma conectividade (rápido quando online)
            if (IsOnline)
            {
                Debug.Log($"[Preview] {pendingRounds.Count} rodada(s) offline pendente(s) — sincronizando batch antes do preview.");
                yield return FlushPendingBatch();
            }
        }

        string ep = $"/v2/unity/round/preview?gameType={UnityWebRequest.EscapeURL(gameType)}";
        yield return SendRequest(ep, "GET", null, raw =>
        {
            PreviewData data = null;
            if (!string.IsNullOrEmpty(raw) && !raw.StartsWith("ERRO"))
            {
                try
                {
                    var env = JsonUtility.FromJson<PreviewEnvelope>(raw);
                    if (env != null && env.data != null)
                    {
                        data = env.data;
                        previewByGameType[gameType] = data;
                        SaveCachedPreviews();
                    }
                }
                catch (Exception e) { Debug.LogError($"[Preview] parse: {e.Message}"); }
            }
            // fallback offline: devolve o preview cacheado se a chamada falhou
            if (data == null && previewByGameType.TryGetValue(gameType, out var cached))
                data = cached;
            callback?.Invoke(data);
        });
    }

    /// POST /v2/unity/round/complete — registra a rodada e retorna o brinde sorteado.
    /// Offline: resolve usando o preview cacheado e enfileira pra envio em batch.
    public IEnumerator CompleteRound(string gameType, int? score, int? discreteOutcome, int durationSec, Action<CompleteData> callback)
    {
        string clientRoundId = Guid.NewGuid().ToString();
        var pending = new PendingRound
        {
            clientRoundId = clientRoundId,
            gameType = gameType,
            score = score ?? 0,
            discreteOutcome = discreteOutcome ?? 0,
            hasScore = score.HasValue,
            hasDiscrete = discreteOutcome.HasValue,
            calibrationSessionId = previewByGameType.TryGetValue(gameType, out var pvSession)
                ? pvSession?.calibrationSessionId : null,
            metadata = new RoundMetadata { durationSec = durationSec }
        };

        if (IsOnline)
        {
            string body = BuildCompleteBody(pending);
            string capturedRaw = null;
            yield return SendRequest("/v2/unity/round/complete", "POST", body, raw => capturedRaw = raw);

            // ParseCompleteResponse agora retorna CompleteData mesmo em failure lógica
            // (TIER_NO_STOCK, PERIOD_CAP_REACHED, ...), preenchendo code/message.
            // Só cai pra offline se houve falha de rede (raw começa com "ERRO").
            bool networkFailed = string.IsNullOrEmpty(capturedRaw) || capturedRaw.StartsWith("ERRO");
            if (!networkFailed)
            {
                CompleteData data = ParseCompleteResponse(capturedRaw, gameType);
                callback?.Invoke(data);
                yield break;
            }
            Debug.LogWarning("[CompleteRound] Falha de rede, resolvendo offline.");
        }

        // ---- Offline ----
        pending.metadata.resolvedOfflineAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        pendingRounds.Add(pending);
        SavePendingRounds();

        CompleteData local = BuildOfflineResolution(gameType, score, discreteOutcome);
        callback?.Invoke(local);
    }

    /// Cache de imagens em 3 camadas:
    ///   1) Memória (Sprite reutilizado durante a sessão)
    ///   2) Disco com TTL — não bate na rede dentro do TTL
    ///   3) Revalidação condicional (ETag/Last-Modified): 304 mantém cache, 200 troca
    /// Em falha de rede, serve cache stale se existir.
    public IEnumerator GetGiftImage(Gift gift, Action<Sprite> callback)
    {
        if (gift == null || string.IsNullOrEmpty(gift.imageUrl)) { callback?.Invoke(null); yield break; }

        string url = gift.imageUrl;

        // 1) memória
        if (imageMemCache.TryGetValue(url, out var memSprite) && memSprite != null)
        {
            callback?.Invoke(memSprite);
            yield break;
        }

        string localPath = GetLocalImagePath(url);
        bool hasLocal = File.Exists(localPath);
        imageMetaCache.TryGetValue(url, out var meta);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 2) cache fresco dentro do TTL — serve direto, sem rede
        if (hasLocal && meta != null && (now - meta.checkedAtUnix) < imageCacheTtlSec)
        {
            var sp = LoadSpriteFromDisk(localPath);
            if (sp != null)
            {
                imageMemCache[url] = sp;
                callback?.Invoke(sp);
                yield break;
            }
        }

        // 3) request — usa UnityWebRequest.Get (mais permissivo que GetTexture com URLs exóticas).
        //    O decode é feito manualmente via Texture2D.LoadImage abaixo, evitando "Data Processing Error".
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.downloadHandler = new DownloadHandlerBuffer();
            if (hasLocal && meta != null)
            {
                if (!string.IsNullOrEmpty(meta.etag))         www.SetRequestHeader("If-None-Match", meta.etag);
                if (!string.IsNullOrEmpty(meta.lastModified)) www.SetRequestHeader("If-Modified-Since", meta.lastModified);
            }
            www.timeout = 10;
            yield return www.SendWebRequest();

            // 304 Not Modified — servidor confirmou que o cache local serve
            if (www.responseCode == 304 && hasLocal)
            {
                Debug.Log($"[ImgCache] 304 — mantém cache: {url}");
                TouchMeta(url, meta);
                var sp = LoadSpriteFromDisk(localPath);
                if (sp != null) { imageMemCache[url] = sp; callback?.Invoke(sp); yield break; }
            }

            // Erro de rede — degrada gracefully pro cache stale
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ImgCache] FAIL ({www.responseCode} {www.error}) — usando cache stale se houver: {url}");
                if (hasLocal)
                {
                    var sp = LoadSpriteFromDisk(localPath);
                    if (sp != null) { imageMemCache[url] = sp; callback?.Invoke(sp); yield break; }
                }
                callback?.Invoke(null);
                yield break;
            }

            // 200 OK — tenta decodificar manualmente
            byte[] bytes = www.downloadHandler.data;
            Texture2D tex = new Texture2D(2, 2);
            if (bytes == null || bytes.Length == 0 || !tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[ImgCache] decode falhou ({bytes?.Length ?? 0} bytes) — usando cache stale se houver: {url}");
                if (hasLocal)
                {
                    var sp = LoadSpriteFromDisk(localPath);
                    if (sp != null) { imageMemCache[url] = sp; callback?.Invoke(sp); yield break; }
                }
                callback?.Invoke(null);
                yield break;
            }

            try { File.WriteAllBytes(localPath, bytes); }
            catch (Exception e) { Debug.LogError($"[ImgCache] write: {e.Message}"); }

            string etag = www.GetResponseHeader("ETag");
            string lastMod = www.GetResponseHeader("Last-Modified");
            SaveMeta(url, etag, lastMod);
            Debug.Log($"[ImgCache] 200 — baixou nova ({bytes.Length} bytes, etag={etag ?? "?"}): {url}");

            var newSprite = TextureToSprite(tex);
            imageMemCache[url] = newSprite;
            callback?.Invoke(newSprite);
        }
    }

    /// Limpa o cache de Sprites em memória (não apaga disco). Útil entre cenas pesadas.
    public void ClearImageMemoryCache()
    {
        imageMemCache.Clear();
    }

    /// Apaga uma imagem específica do cache (memória + disco + metadata).
    public void InvalidateImage(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        imageMemCache.Remove(url);
        imageMetaCache.Remove(url);
        try { File.Delete(GetLocalImagePath(url)); } catch { }
        PersistImageMeta();
    }

    private void SaveMeta(string url, string etag, string lastModified)
    {
        imageMetaCache[url] = new ImageCacheMeta
        {
            url = url,
            etag = etag,
            lastModified = lastModified,
            checkedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        PersistImageMeta();
    }

    private void TouchMeta(string url, ImageCacheMeta meta)
    {
        meta.checkedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        imageMetaCache[url] = meta;
        PersistImageMeta();
    }

    private void PersistImageMeta()
    {
        try
        {
            var w = new ImageCacheMetaWrapper { entries = imageMetaCache.Values.ToList() };
            File.WriteAllText(GetLocalPath(IMG_META_FILE), JsonUtility.ToJson(w));
        }
        catch (Exception e) { Debug.LogError($"[ImgCache] save meta: {e.Message}"); }
    }

    private void LoadImageMetaCache()
    {
        string path = GetLocalPath(IMG_META_FILE);
        if (!File.Exists(path)) return;
        try
        {
            var w = JsonUtility.FromJson<ImageCacheMetaWrapper>(File.ReadAllText(path));
            if (w?.entries != null)
                foreach (var e in w.entries) imageMetaCache[e.url] = e;
            Debug.Log($"[ImgCache] metadata carregada: {imageMetaCache.Count} entradas.");
        }
        catch (Exception e) { Debug.LogError($"[ImgCache] load meta: {e.Message}"); }
    }

    public int PendingRoundsCount => pendingRounds.Count;
    public bool HasConfig => config != null;
    public string ConfiguredBaseUrl => config?.baseUrl;

    /// Dispara manualmente o flush dos rounds pendentes (útil pra testes).
    public IEnumerator FlushPending() => FlushPendingBatch();

    // ----------- Resolução offline -----------
    private CompleteData BuildOfflineResolution(string gameType, int? score, int? discreteOutcome)
    {
        // Jogos discretos (JACKPOT/ROLETA): resolve pelas FAIXAS do preview cacheado,
        // respeitando estoque. Prefere o tier pedido; se sem estoque, cai pro discreteValue 0
        // e depois pros demais. Nunca premia brinde sem estoque.
        if (discreteOutcome.HasValue && !score.HasValue)
        {
            previewByGameType.TryGetValue(gameType, out var pv);
            if (pv?.tiers != null && pv.tiers.Count > 0)
                return BuildOfflineFromTiers(gameType, pv, discreteOutcome.Value);
        }

        // Jogos por SCORE (SCORE_ENDLESS): SEM internet premia sempre pela faixa mais baixa
        // (como se fosse pontuação 0), respeitando estoque. Se não houver pool/estoque utilizável
        // no preview, retorna null e cai pro hint/preview aprendido online (abaixo).
        if (score.HasValue)
        {
            previewByGameType.TryGetValue(gameType, out var pvScore);
            if (pvScore?.tiers != null && pvScore.tiers.Count > 0)
            {
                var byScore = BuildOfflineFromTiersByScore(gameType, pvScore, score.Value);
                if (byScore != null) return byScore;
            }
        }

        // Fallback pra SCORE quando o preview não trouxe pool utilizável (poolGifts vazios):
        // como offline é SEMPRE a faixa base (consolação), resolvemos pelo hint da banda MAIS BAIXA
        // (pontuação mínima), aprendido de uma rodada online — NÃO pelo score real do jogador.
        if (score.HasValue)
        {
            int baseScore = LowestBandMin(gameType);   // ex.: 0 (Faixa 1)

            var hint = giftHints.FirstOrDefault(h =>
                h.gameType == gameType &&
                baseScore >= h.scoreMin &&
                baseScore <= h.scoreMax &&
                h.gift != null);

            if (hint != null)
            {
                Debug.Log($"[Offline] SCORE consolação via hint da faixa base (score real={score} ignorado): " +
                          $"tier={hint.tierLabel} gift={hint.gift.name} band=[{hint.scoreMin},{hint.scoreMax}]");
                return new CompleteData
                {
                    tierId = hint.tierId,
                    tierLabel = hint.tierLabel,
                    gift = hint.gift,
                    resolvedFrom = new ResolvedFrom
                    {
                        score = score.Value,
                        discreteOutcome = discreteOutcome ?? 0,
                        band = new ScoreRange { min = hint.scoreMin, max = hint.scoreMax }
                    },
                    stockRemainingForGift = -1,
                    resolvedOffline = true,
                    code = "OFFLINE_OK_HINT"
                };
            }
        }

        // 2ª escolha: preview cacheado (genérico do gameType).
        PreviewData preview;
        if (!previewByGameType.TryGetValue(gameType, out preview) || preview?.preview == null)
        {
            Debug.LogWarning($"[Offline] Sem hint nem preview pra gameType={gameType}. Sem prêmio offline.");
            return new CompleteData { gift = null, resolvedOffline = true, code = "OFFLINE_NO_PREVIEW" };
        }

        Debug.Log($"[Offline] Sem hint pro score={score}, usando preview cacheado: {preview.preview.candidateGiftName}");
        return new CompleteData
        {
            tierId = preview.targetTierId,
            tierLabel = preview.targetTierLabel,
            gift = new Gift
            {
                id = preview.preview.candidateGiftId,
                name = preview.preview.candidateGiftName,
                imageUrl = GetKnownImageUrl(preview.preview.candidateGiftId)
            },
            resolvedFrom = new ResolvedFrom
            {
                score = score ?? 0,
                discreteOutcome = discreteOutcome ?? 0,
                band = preview.scoreRange
            },
            stockRemainingForGift = -1,
            resolvedOffline = true,
            code = "OFFLINE_OK_PREVIEW"
        };
    }

    // ===== Premiação offline pelas FAIXAS do preview, respeitando estoque =====
    // Comum aos dois tipos de jogo; só muda a forma de escolher a faixa-alvo.

    // DISCRETO (JACKPOT/ROLETA): tier pedido → discreteValue 0 → demais (por sortOrder).
    private CompleteData BuildOfflineFromTiers(string gameType, PreviewData pv, int requestedDiscrete)
    {
        // Monta a ordem de preferência sem repetir discreteValue.
        var order = new List<int>();
        void AddDv(int dv) { if (!order.Contains(dv)) order.Add(dv); }

        AddDv(requestedDiscrete);   // 1º: a faixa que o gerenciador decidiu
        AddDv(0);                   // 2º: regra do negócio — offline sempre tende ao discreteValue 0
        foreach (var t in pv.tiers.OrderBy(t => t.sortOrder))
            if (t.calibration != null) AddDv(t.calibration.discreteValue);

        foreach (int dv in order)
        {
            PreviewTier tier = FindTierByDiscrete(pv, dv);
            PoolGift pick = PickInStockGift(tier);
            if (pick == null) continue;

            var resolved = new ResolvedFrom { score = 0, discreteOutcome = dv, band = null };
            return AwardOfflineFromTier(tier, pick, resolved, fallback: dv != requestedDiscrete, ctx: $"dv={dv}");
        }

        Debug.LogWarning("[Offline] Nenhuma faixa com estoque disponível. Sem prêmio.");
        return new CompleteData { gift = null, resolvedOffline = true, code = "OFFLINE_NO_STOCK" };
    }

    // SCORE (SCORE_ENDLESS): SEM internet, premia SEMPRE pela FAIXA MAIS BAIXA (sortOrder 0,
    // como se fosse pontuação 0), independente do score real do jogador. Se a faixa mais baixa
    // estiver sem estoque, sobe pra próxima até achar disponível (não há nada abaixo da base).
    // Retorna null se nenhuma faixa tiver estoque (ex.: poolGifts vazios) → o chamador cai pro
    // hint/preview aprendido online.
    private CompleteData BuildOfflineFromTiersByScore(string gameType, PreviewData pv, int score)
    {
        // Faixas ordenadas por sortOrder ASC (0 = base/mais baixa).
        var sorted = pv.tiers.Where(t => t.calibration != null).OrderBy(t => t.sortOrder).ToList();
        if (sorted.Count == 0) return null;

        // Começa na faixa mais baixa e sobe até encontrar estoque.
        for (int i = 0; i < sorted.Count; i++)
        {
            PreviewTier tier = sorted[i];
            PoolGift pick = PickInStockGift(tier);
            if (pick == null) continue;

            var band = tier.calibration.scoreRange;
            var resolved = new ResolvedFrom { score = score, discreteOutcome = 0, band = band };
            return AwardOfflineFromTier(tier, pick, resolved, fallback: i != 0, ctx: $"faixa-base (score real={score} ignorado)");
        }

        // Nenhuma faixa com estoque utilizável (ex.: poolGifts vazios no preview).
        Debug.LogWarning("[Offline] SCORE: nenhuma faixa com estoque no preview — caindo pro hint/preview.");
        return null;
    }

    // Consome estoque, persiste, resolve a imagem e monta o CompleteData. Comum aos dois tipos.
    private CompleteData AwardOfflineFromTier(PreviewTier tier, PoolGift pick, ResolvedFrom resolved, bool fallback, string ctx)
    {
        // Consome 1 do estoque local e persiste, pra não premiar além do disponível
        // em rodadas offline subsequentes (até um novo preview online sobrescrever).
        pick.remainingStock = Mathf.Max(0, pick.remainingStock - 1);
        SaveCachedPreviews();

        // Recupera a imageUrl aprendida de uma rodada online anterior (poolGifts não trazem URL).
        // Se nunca saiu online, fica null → a UI usa o fallbackSprite.
        string imageUrl = GetKnownImageUrl(pick.giftId);

        Debug.Log($"[Offline] Premiando via estoque: tier={tier.tierLabel} ({ctx}{(fallback ? ", FALLBACK" : "")}) " +
                  $"gift={pick.giftName} estoqueRestante={pick.remainingStock} img={(string.IsNullOrEmpty(imageUrl) ? "—" : "cache")}");

        return new CompleteData
        {
            tierId = tier.tierId,
            tierLabel = tier.tierLabel,
            gift = new Gift { id = pick.giftId, name = pick.giftName, imageUrl = imageUrl },
            resolvedFrom = resolved,
            stockRemainingForGift = pick.remainingStock,
            resolvedOffline = true,
            code = fallback ? "OFFLINE_OK_STOCK_FALLBACK" : "OFFLINE_OK_STOCK"
        };
    }

    private static PreviewTier FindTierByDiscrete(PreviewData pv, int discreteValue)
    {
        if (pv?.tiers == null) return null;
        return pv.tiers.FirstOrDefault(t => t.calibration != null && t.calibration.discreteValue == discreteValue);
    }

    // Pontuação mínima da faixa base (menor sortOrder) do preview cacheado. 0 se não houver.
    // Usado pra resolver SCORE offline sempre como "faixa de consolação".
    private int LowestBandMin(string gameType)
    {
        if (previewByGameType.TryGetValue(gameType, out var pv) && pv?.tiers != null)
        {
            var lowest = pv.tiers
                .Where(t => t.calibration != null && t.calibration.scoreRange != null)
                .OrderBy(t => t.sortOrder)
                .FirstOrDefault();
            if (lowest != null) return lowest.calibration.scoreRange.min;
        }
        return 0;
    }

    // Sorteia um brinde COM estoque dentro da faixa, ponderado por effectiveWeight.
    // Retorna null se a faixa não existe ou está sem estoque.
    private static PoolGift PickInStockGift(PreviewTier tier)
    {
        if (tier?.poolGifts == null) return null;

        var inStock = tier.poolGifts.Where(g => g != null && g.remainingStock > 0).ToList();
        if (inStock.Count == 0) return null;

        // Peso: effectiveWeight (cai pro configuredWeight, e por fim 1).
        long total = 0;
        foreach (var g in inStock) total += Mathf.Max(1, g.effectiveWeight > 0 ? g.effectiveWeight : g.configuredWeight);

        long r = (long)(UnityEngine.Random.value * total);
        foreach (var g in inStock)
        {
            r -= Mathf.Max(1, g.effectiveWeight > 0 ? g.effectiveWeight : g.configuredWeight);
            if (r < 0) return g;
        }
        return inStock[inStock.Count - 1];
    }

    private void RememberGiftHint(string gameType, CompleteData data)
    {
        if (data?.gift == null || string.IsNullOrEmpty(data.tierId)) return;
        if (data.resolvedFrom?.band == null) return;

        // Substitui hint do mesmo tier (mantém só o mais recente por tier)
        giftHints.RemoveAll(h => h.gameType == gameType && h.tierId == data.tierId);
        giftHints.Add(new OfflineGiftHint
        {
            gameType = gameType,
            scoreMin = data.resolvedFrom.band.min,
            scoreMax = data.resolvedFrom.band.max,
            tierId = data.tierId,
            tierLabel = data.tierLabel,
            gift = data.gift
        });
        SaveGiftHints();
    }

    private void SaveGiftHints()
    {
        try
        {
            string json = JsonUtility.ToJson(new OfflineGiftHintsWrapper { entries = giftHints });
            File.WriteAllText(GetLocalPath(OFFLINE_HINTS_FILE), json);
        }
        catch (Exception e) { Debug.LogError($"[Offline] save hints: {e.Message}"); }
    }

    private void LoadGiftHints()
    {
        string path = GetLocalPath(OFFLINE_HINTS_FILE);
        if (!File.Exists(path)) return;
        try
        {
            var w = JsonUtility.FromJson<OfflineGiftHintsWrapper>(File.ReadAllText(path));
            giftHints = w?.entries ?? new List<OfflineGiftHint>();
            Debug.Log($"[Offline] hints carregados: {giftHints.Count}");
        }
        catch (Exception e) { Debug.LogError($"[Offline] load hints: {e.Message}"); giftHints = new List<OfflineGiftHint>(); }
    }

    // ----------- Mapa giftId → imageUrl (pra imagem offline) -----------

    // Memoriza a URL da imagem de um brinde premiado online. A imagem em si já é
    // baixada/cacheada em disco pelo GetGiftImage; aqui guardamos só a chave (URL) por giftId.
    private void RememberGiftImage(Gift gift)
    {
        if (gift == null || string.IsNullOrEmpty(gift.id) || string.IsNullOrEmpty(gift.imageUrl)) return;

        if (giftImageUrlById.TryGetValue(gift.id, out var existing) && existing == gift.imageUrl)
            return; // já conhecido, evita reescrever o arquivo

        giftImageUrlById[gift.id] = gift.imageUrl;
        SaveGiftImageMap();
    }

    // Retorna a imageUrl conhecida pra um giftId (ou null se nunca vista online).
    private string GetKnownImageUrl(string giftId)
    {
        if (string.IsNullOrEmpty(giftId)) return null;
        return giftImageUrlById.TryGetValue(giftId, out var url) ? url : null;
    }

    [Serializable] private class GiftImageEntry { public string giftId; public string imageUrl; }
    [Serializable] private class GiftImageMapWrapper { public List<GiftImageEntry> entries; }

    private void SaveGiftImageMap()
    {
        try
        {
            var w = new GiftImageMapWrapper
            {
                entries = giftImageUrlById.Select(kv => new GiftImageEntry { giftId = kv.Key, imageUrl = kv.Value }).ToList()
            };
            File.WriteAllText(GetLocalPath(GIFT_IMG_MAP_FILE), JsonUtility.ToJson(w));
        }
        catch (Exception e) { Debug.LogError($"[GiftImgMap] save: {e.Message}"); }
    }

    private void LoadGiftImageMap()
    {
        string path = GetLocalPath(GIFT_IMG_MAP_FILE);
        if (!File.Exists(path)) return;
        try
        {
            var w = JsonUtility.FromJson<GiftImageMapWrapper>(File.ReadAllText(path));
            if (w?.entries != null)
                foreach (var e in w.entries)
                    if (!string.IsNullOrEmpty(e.giftId) && !string.IsNullOrEmpty(e.imageUrl))
                        giftImageUrlById[e.giftId] = e.imageUrl;
            Debug.Log($"[GiftImgMap] carregados: {giftImageUrlById.Count}");
        }
        catch (Exception e) { Debug.LogError($"[GiftImgMap] load: {e.Message}"); }
    }

    // ----------- Ping periódico + sync -----------
    private IEnumerator PeriodicPingAndSync()
    {
        // primeira espera curta pra dar tempo do LoadEvent inicial
        yield return new WaitForSeconds(pingIntervalSec);

        while (true)
        {
            bool wasOffline = !IsOnline;
            yield return PingHealth();

            if (IsOnline && (wasOffline || pendingRounds.Count > 0))
            {
                yield return FlushPendingBatch();
            }

            yield return new WaitForSeconds(pingIntervalSec);
        }
    }

    private IEnumerator PingHealth()
    {
        yield return SendRequest("/v2/unity/health", "GET", null, raw =>
        {
            if (!string.IsNullOrEmpty(raw) && !raw.StartsWith("ERRO"))
            {
                try
                {
                    var env = JsonUtility.FromJson<HealthEnvelope>(raw);
                    IsOnline = env != null && env.success;
                    if (IsOnline && env.data != null)
                    {
                        EventId = env.data.eventId;
                        ActivationId = env.data.activationId;
                        if (!string.IsNullOrEmpty(env.data.gameType)) CurrentGameType = env.data.gameType;
                        if (env.data.gameDurationSec > 0) CurrentGameDurationSec = env.data.gameDurationSec;
                    }
                }
                catch { IsOnline = false; }
            }
            else
            {
                IsOnline = false;
            }
        });
    }

    private IEnumerator FlushPendingBatch()
    {
        if (isFlushing || pendingRounds.Count == 0) yield break;
        isFlushing = true;

        // API aceita até 100 por request. Vou enviar em chunks de 50 por segurança.
        const int CHUNK = 50;
        var snapshot = new List<PendingRound>(pendingRounds);

        for (int i = 0; i < snapshot.Count; i += CHUNK)
        {
            var chunk = snapshot.GetRange(i, Math.Min(CHUNK, snapshot.Count - i));
            string body = BuildBatchBody(chunk);
            string raw = null;
            yield return SendRequest("/v2/unity/round/complete/batch", "POST", body, r => raw = r);

            if (string.IsNullOrEmpty(raw) || raw.StartsWith("ERRO"))
            {
                Debug.LogWarning("[Batch] Falha — mantendo pendentes.");
                isFlushing = false;
                yield break; // tenta no próximo ciclo
            }

            BatchEnvelope env = null;
            try { env = JsonUtility.FromJson<BatchEnvelope>(raw); }
            catch (Exception e) { Debug.LogError($"[Batch] parse: {e.Message}"); }

            if (env?.data?.results != null)
            {
                // Remove apenas os que o servidor confirmou (success=true OU code definitivo
                // tipo TIER_NO_STOCK — não dá pra reentregar, então também tira).
                var confirmedIds = new HashSet<string>(
                    env.data.results
                        .Where(r => r.success || IsTerminalFailureCode(r.code))
                        .Select(r => r.clientRoundId));

                pendingRounds.RemoveAll(p => confirmedIds.Contains(p.clientRoundId));
                SavePendingRounds();
                Debug.Log($"[Batch] {confirmedIds.Count} confirmados. Pendentes restantes: {pendingRounds.Count}");
            }
        }

        isFlushing = false;
    }

    private bool IsTerminalFailureCode(string code)
    {
        // códigos que não fazem sentido reenviar
        return code == "TIER_NO_STOCK" || code == "EVENT_INACTIVE" || code == "DUPLICATE_ROUND";
    }

    // ----------- HTTP -----------
    private IEnumerator SendRequest(string endpoint, string method, string jsonBody, Action<string> callback)
    {
        if (config == null)
        {
            callback?.Invoke("ERRO: config ausente");
            yield break;
        }

        if (DebugForceOffline)
        {
            IsOnline = false;
            callback?.Invoke("ERRO: DebugForceOffline ativo");
            yield break;
        }

        string url = $"{config.baseUrl.TrimEnd('/')}{endpoint}";
        UnityWebRequest req;

        if (method == "GET")
        {
            req = UnityWebRequest.Get(url);
        }
        else
        {
            req = new UnityWebRequest(url, method);
            byte[] raw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
        }
        req.SetRequestHeader("Authorization", $"Bearer {config.unityKey}");
        req.timeout = 10;

        if (method != "GET" && !string.IsNullOrEmpty(jsonBody))
        {
            string reqPreview = jsonBody.Length > LogBodyMaxChars ? jsonBody.Substring(0, LogBodyMaxChars) + "…" : jsonBody;
            Debug.Log($"[HTTP] → {method} {endpoint} body={reqPreview}");
        }
        else
        {
            Debug.Log($"[HTTP] → {method} {endpoint}");
        }
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            IsOnline = false;
            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            Debug.LogWarning($"[HTTP] ← {method} {endpoint} FAIL status={req.responseCode} result={req.result} error={req.error} body={body}");
            callback?.Invoke($"ERRO: {req.result} {req.error} {body}");
            yield break;
        }

        IsOnline = true;
        string respBody = req.downloadHandler.text ?? "";
        string preview = respBody.Length > LogBodyMaxChars ? respBody.Substring(0, LogBodyMaxChars) + "…" : respBody;
        Debug.Log($"[HTTP] ← {method} {endpoint} {req.responseCode} body={preview}");
        callback?.Invoke(respBody);
    }

    private CompleteData ParseCompleteResponse(string raw, string gameType)
    {
        if (string.IsNullOrEmpty(raw) || raw.StartsWith("ERRO"))
            return new CompleteData { code = "NETWORK_ERROR", message = raw };

        try
        {
            var env = JsonUtility.FromJson<CompleteEnvelope>(raw);
            if (env == null)
                return new CompleteData { code = "PARSE_ERROR" };

            // Sucesso real (servidor entregou um brinde)
            if (env.success && env.data != null && env.data.gift != null)
            {
                env.data.code = string.IsNullOrEmpty(env.code) ? "OK" : env.code;
                env.data.message = env.message;
                RememberGiftHint(gameType, env.data);
                RememberGiftImage(env.data.gift);
                return env.data;
            }

            // Servidor respondeu mas sem prêmio (TIER_NO_STOCK, PERIOD_CAP_REACHED, etc.)
            Debug.LogWarning($"[Complete] sem prêmio: code={env.code} msg={env.message}");
            return new CompleteData { code = env.code ?? "NO_PRIZE", message = env.message };
        }
        catch (Exception e)
        {
            Debug.LogError($"[Complete] parse: {e.Message}");
            return new CompleteData { code = "PARSE_ERROR", message = e.Message };
        }
    }

    // ----------- Builders de body -----------
    private string BuildCompleteBody(PendingRound p)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"gameType\":\"{Escape(p.gameType)}\",");
        sb.Append($"\"clientRoundId\":\"{Escape(p.clientRoundId)}\"");
        if (p.hasScore) sb.Append($",\"score\":{p.score}");
        if (p.hasDiscrete) sb.Append($",\"discreteOutcome\":{p.discreteOutcome}");
        if (!string.IsNullOrEmpty(p.calibrationSessionId))
            sb.Append($",\"calibrationSessionId\":\"{Escape(p.calibrationSessionId)}\"");
        sb.Append($",\"metadata\":{{\"durationSec\":{p.metadata.durationSec}}}");
        sb.Append("}");
        return sb.ToString();
    }

    private string BuildBatchBody(List<PendingRound> rounds)
    {
        var sb = new StringBuilder();
        sb.Append("{\"rounds\":[");
        for (int i = 0; i < rounds.Count; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append(BuildCompleteBody(rounds[i]));
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ----------- Persistência local -----------
    private string GetLocalPath(string fileName) => Path.Combine(Application.persistentDataPath, fileName);

    private void SavePendingRounds()
    {
        try
        {
            string json = JsonUtility.ToJson(new PendingRoundsWrapper { rounds = pendingRounds });
            File.WriteAllText(GetLocalPath(PENDING_FILE), json);
        }
        catch (Exception e) { Debug.LogError($"[Pending] save: {e.Message}"); }
    }

    private void LoadPendingRounds()
    {
        string path = GetLocalPath(PENDING_FILE);
        if (!File.Exists(path)) { pendingRounds = new List<PendingRound>(); return; }
        try
        {
            var w = JsonUtility.FromJson<PendingRoundsWrapper>(File.ReadAllText(path));
            pendingRounds = w?.rounds ?? new List<PendingRound>();
            Debug.Log($"[Pending] carregados: {pendingRounds.Count}");
        }
        catch (Exception e) { Debug.LogError($"[Pending] load: {e.Message}"); pendingRounds = new List<PendingRound>(); }
    }

    [Serializable] private class PreviewCacheEntry { public string gameType; public PreviewData data; }
    [Serializable] private class PreviewCacheWrapper { public List<PreviewCacheEntry> entries; }

    private void SaveCachedPreviews()
    {
        try
        {
            var w = new PreviewCacheWrapper
            {
                entries = previewByGameType.Select(kv => new PreviewCacheEntry { gameType = kv.Key, data = kv.Value }).ToList()
            };
            File.WriteAllText(GetLocalPath(LAST_PREVIEWS_FILE), JsonUtility.ToJson(w));
        }
        catch (Exception e) { Debug.LogError($"[PreviewCache] save: {e.Message}"); }
    }

    private void LoadCachedPreviews()
    {
        string path = GetLocalPath(LAST_PREVIEWS_FILE);
        if (!File.Exists(path)) return;
        try
        {
            var w = JsonUtility.FromJson<PreviewCacheWrapper>(File.ReadAllText(path));
            if (w?.entries != null)
                foreach (var e in w.entries) previewByGameType[e.gameType] = e.data;
        }
        catch (Exception e) { Debug.LogError($"[PreviewCache] load: {e.Message}"); }
    }

    private void SaveCachedHealth(string raw)
    {
        try { File.WriteAllText(GetLocalPath(LAST_HEALTH_FILE), raw); }
        catch (Exception e) { Debug.LogError($"[Health] cache save: {e.Message}"); }
    }

    private void LoadCachedHealth()
    {
        string path = GetLocalPath(LAST_HEALTH_FILE);
        if (!File.Exists(path)) return;
        try
        {
            var env = JsonUtility.FromJson<HealthEnvelope>(File.ReadAllText(path));
            if (env?.data != null)
            {
                EventId = env.data.eventId;
                ActivationId = env.data.activationId;
                if (!string.IsNullOrEmpty(env.data.gameType)) CurrentGameType = env.data.gameType;
                if (env.data.gameDurationSec > 0) CurrentGameDurationSec = env.data.gameDurationSec;
            }
        }
        catch { }
    }

    // ----------- Cache de imagem -----------
    public string GetLocalImagePath(string imageUrl)
    {
        using (var sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(imageUrl));
            string hex = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
            return Path.Combine(Application.persistentDataPath, hex + ".png");
        }
    }

    private Sprite LoadSpriteFromDisk(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(data)) return TextureToSprite(tex);
        }
        catch (Exception e) { Debug.LogError($"[Sprite] {e.Message}"); }
        return null;
    }

    private Sprite TextureToSprite(Texture2D tex) =>
        Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);

    // ----------- Fallback do configurador (mantém compatibilidade com a v1) -----------
    private void TryLaunchConfigurator()
    {
        string rootPath = Directory.GetParent(Application.dataPath).FullName;
        string bat = Path.Combine(rootPath, "run_with_config.bat");
        if (File.Exists(bat))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = bat, UseShellExecute = true });
            }
            catch (Exception e) { Debug.LogError($"[Config] launcher: {e.Message}"); }
            Application.Quit();
        }
        else
        {
            Debug.LogError("[Config] totem-config.json ausente e run_with_config.bat não encontrado.");
        }
    }
}
