using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Para List.Any()
using System.Text;
using System.Security.Cryptography; // Necessário para PrizeManager usar SHA256
using UnityEngine;
using UnityEngine.Networking;

// A classe StockItem foi renomeada para TotemItem e movida para o escopo global no PrizeManager.cs
// Para evitar duplicação, ela será removida daqui.
// O TotemApiClient.cs usará a definição global de TotemItem.

[System.Serializable]
public class TotemConfig
{
    public string unityKey;
    public string deviceSecret;
}

public static class ConfigManager
{
    public static TotemConfig Load()
    {
        // Pega a pasta onde está o EXE
        string rootPath = Directory.GetParent(Application.dataPath).FullName;

        // Agora podemos usar uma pasta config na raiz
        string path = Path.Combine(rootPath, "config/totem-config.json");

        if (!File.Exists(path))
        {
            Debug.LogError("Arquivo de config não encontrado!");
            return null;
        }

        string json = File.ReadAllText(path);
        return JsonUtility.FromJson<TotemConfig>(json);
    }
}

public class TotemApiClient : MonoBehaviour
{
    private const string STOCK_FILE_NAME = "local_stock.json";
    private const string BATCH_FILE_NAME = "pending_batches.json";
    private const string EVENT_FILE_NAME = "pending_events.json";

    public string CurrentDifficulty { get; private set; } = "NORMAL"; // Default para evitar erro
    public string LastStockJson { get; private set; } = "{}"; // Armazena o último JSON de estoque
    private ApiTotemStockResponse lastStockResponse; // Armazena o objeto de resposta do último estoque
    public List<TotemItem> StockItems { get; private set; } = new List<TotemItem>();
    public ApiTotemStockResponse GetLastStockResponse() => lastStockResponse;
    public int GameTime { get; private set; } = 0;

    // Lista de batches pendentes (para salvar localmente)
    private List<BatchPayload> pendingBatches = new List<BatchPayload>();
    // NOVO: Lista de vitórias pendentes (para salvar localmente)
    private List<WinRecord> pendingWins = new List<WinRecord>();
    // Lista de eventos pendentes (para salvar localmente)
    private List<TrackingEvent> pendingEvents = new List<TrackingEvent>();

    // Classes de dados para o parsing da resposta da API GetStock
    [System.Serializable]
    public class ApiTotemConfig
    {
        public string difficulty; // EASY, NORMAL, HARD, EXTREME
        public int version;
        public int gameTime;
    }

    [System.Serializable]
    public class ApiTotemStockResponse
    {
        public string totemId;
        public ApiTotemConfig totemConfig;
        public List<TotemItem> items;
    }

    private const string BaseUrl = "https://api.gift-catcher.dilis.com.br/api";

    // Credenciais de teste
    //private const string UnityKey = "TOT-0DAD30F5-F5BC422B";
    //private const string DeviceSecret = "1836e27e6f24933fddb0c32f49ae5dc259b8de5d9ace0a4fbd9f8e233d112853";

    // -------------------------
    // Helper para requisições
    // -------------------------

    // Variável estática para o Singleton
    public static TotemApiClient Instance { get; private set; }
    public GameController gameController;
    void Awake()
    {
        // Implementação do Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        //DontDestroyOnLoad(gameObject);

        // Pega a pasta onde está o EXE
        string rootPath = Directory.GetParent(Application.dataPath).FullName;

        // Agora podemos usar uma pasta config na raiz
        string configPath = Path.Combine(rootPath, "config/totem-config.json");

        Debug.Log("Procurando config em: " + configPath);

        if (!File.Exists(configPath))
        {
            Debug.LogWarning("Arquivo de config não encontrado, abrindo configurador...");

            // BAT fica na mesma pasta do EXE
            string batFile = Path.Combine(rootPath, "run_with_config.bat");

            if (File.Exists(batFile))
            {
                var psi = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = batFile,
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(psi);
            }
            else
            {
                Debug.LogError("run_with_config.bat não encontrado!");
            }

            Application.Quit();
            return;
        }

        Debug.Log("Config encontrado!");
    }

    void Start()
    {
        // Carrega batches e eventos pendentes ao iniciar
        LoadPendingBatches();
        LoadPendingEvents();
        // NOVO: Carrega vitórias pendentes ao iniciar
        LoadPendingWins();

        // Inicia a rotina de ping periódico
        StartCoroutine(PeriodicPingAndSync());

        // O Ping inicial será feito dentro do LoadConfig para sincronização inicial
    }


    private IEnumerator SendRequest(string endpoint, string method, string jsonBody, System.Action<string> callback)
    {
        TotemConfig cfg = ConfigManager.Load();
        string url = $"{BaseUrl}{endpoint}";

        UnityWebRequest request;

        if (method == "GET")
        {
            request = UnityWebRequest.Get(url);
        }
        else
        {
            request = new UnityWebRequest(url, method);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
        }

        // headers obrigatórios
        request.SetRequestHeader("x-unity-key", cfg.unityKey);
        request.SetRequestHeader("x-device-secret", cfg.deviceSecret);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            callback?.Invoke($"ERRO: {request.result}\n{request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        string jsonResponse = request.downloadHandler.text;

        // 🔥🔥🔥 SOMENTE SE FOR /totem-sync/stock, FAZ PARSE E ATUALIZA GAME CONFIG E SALVA LOCALMENTE 🔥🔥🔥
        if (endpoint == "/totem-sync/stock")
        {
            try
            {
                ApiTotemStockResponse response = JsonUtility.FromJson<ApiTotemStockResponse>(jsonResponse);
                lastStockResponse = response; // Armazena o objeto

                if (response != null)
                {
                    CurrentDifficulty = response.totemConfig?.difficulty ?? "NORMAL";
                    GameTime = response.totemConfig?.gameTime ?? 30; // fallback

                    if (response.items != null)
                    {
                        StockItems = response.items;

                        foreach (var item in StockItems)
                        {
                            Debug.Log($"[STOCK] Item: {item.name} | Total: {item.totalStock} | Remaining: {item.remaining}");
                        }
                    }

                    Debug.Log($"[STOCK] Dificuldade: {CurrentDifficulty} | GameTime: {GameTime}");
	                    SaveStockLocally(jsonResponse); // Salva o estoque localmente
	                    
	                    // NOVO: Inicia o pré-cache das imagens
	                    StartCoroutine(PreCacheImages(response.items));
	                }
	            }
	            catch (System.Exception e)
	            {
	                Debug.LogError($"Erro ao parsear resposta do Stock: {e.Message}");
	            }
	            LastStockJson = jsonResponse;
	        }
        else
        {
            // 🔎 Para debug das outras rotas
            Debug.Log($"[API] Rota {endpoint} → {jsonResponse}");
        }

        callback?.Invoke(jsonResponse);
    }


    // ----------------------------------------------------
    // ROTAS
    // ----------------------------------------------------

    public IEnumerator GetStock(System.Action<string> callback)
    {
        yield return SendRequest("/totem-sync/stock", "GET", null, callback);
    }

    public bool HasAnyGiftAvailable()
    {
        foreach (var item in StockItems)
        {
            if (item.remaining > 0)
                return true;
        }
        return false;
    }

    // Método para ser chamado no início do jogo para garantir que a dificuldade seja carregada
    public IEnumerator LoadDifficulty(System.Action onLoaded)
    {
        yield return GetStock((response) =>
        {
            // O SendRequest já atualiza a CurrentDifficulty
            onLoaded?.Invoke();
        });
    }

    public IEnumerator LoadConfig(System.Action onLoaded)
    {
        // 1. Tenta o Ping para verificar a conexão
        bool isOnline = false;
        yield return Ping((response) =>
        {
            if (!response.StartsWith("ERRO"))
            {
                isOnline = true;
                Debug.Log("Ping bem-sucedido. Online.");
            }
            else
            {
                Debug.LogWarning("Ping falhou. Offline. " + response);
            }
        });

        if (isOnline)
        {
	            // 2. Se online, tenta sincronizar batches e eventos pendentes
	            yield return SyncPendingBatches();
	            yield return SyncPendingEvents();

            // 3. Puxa o estoque mais recente
            yield return GetStock((response) =>
            {
                // GetStock já preenche:
                // - CurrentDifficulty
                // - GameTime
                // - StockItems
                if (response.StartsWith("ERRO"))
                {
                    Debug.LogError("Erro ao carregar estoque online. Tentando carregar localmente.");
                    LoadStockLocally();
                }
            });
        }
        else
        {
            // 4. Se offline, carrega o estoque local
            LoadStockLocally();
        }

        onLoaded?.Invoke();
    }

    // Classes de dados para o JSON de Evento
    [System.Serializable]
    public class EventPayload
    {
        public int round;
    }

    [System.Serializable]
    public class TrackingEvent
    {
        public string type;
        public EventPayload payload;
    }

    public IEnumerator SendTrackingEvent(string eventType, int round, System.Action<string> callback)
    {
        TrackingEvent trackingEvent = new TrackingEvent
        {
            type = eventType,
            payload = new EventPayload { round = round }
        };

        string jsonBody = JsonUtility.ToJson(trackingEvent);
        Debug.Log($"Tentando enviar Evento de Rastreio: {jsonBody}");

        // Tenta o Ping para verificar a conexão
        bool isOnline = false;
        yield return Ping((response) =>
        {
            if (!response.StartsWith("ERRO"))
            {
                isOnline = true;
            }
        });

        if (isOnline)
        {
            // Se online, envia o Evento
            yield return SendRequest("/totem-sync/event", "POST", jsonBody, callback);
        }
        else
        {
            // Se offline, armazena localmente
            pendingEvents.Add(trackingEvent);
            SavePendingEvents();
            Debug.LogWarning("Offline. Evento salvo localmente.");
            callback?.Invoke("Evento salvo localmente (Offline)");
        }
    }

    public IEnumerator PostEvent(string message, System.Action<string> callback)
    {
        string body = $"{{\"event\":\"{message}\"}}";
        yield return SendRequest("/totem-sync/event", "POST", body, callback);
    }

    public IEnumerator PostBatch(string jsonBatch, System.Action<string> callback)
    {
        yield return SendRequest("/totem-sync/batch", "POST", jsonBatch, callback);
    }

    // ----------------------------------------------------
    // FUNÇÕES DE ARMAZENAMENTO LOCAL DE EVENTOS
    // ----------------------------------------------------

    // Salva a lista de eventos pendentes
    private void SavePendingEvents()
    {
        try
        {
            // Wrapper para serializar a lista
            string json = JsonUtility.ToJson(new EventListWrapper { events = pendingEvents });
            File.WriteAllText(GetLocalPath(EVENT_FILE_NAME), json);
            Debug.Log($"Eventos pendentes salvos localmente. Total: {pendingEvents.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao salvar eventos pendentes: {e.Message}");
        }
    }

    // Carrega a lista de eventos pendentes
    private void LoadPendingEvents()
    {
        string path = GetLocalPath(EVENT_FILE_NAME);
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                EventListWrapper wrapper = JsonUtility.FromJson<EventListWrapper>(json);
                pendingEvents = wrapper.events ?? new List<TrackingEvent>();
                Debug.Log($"Eventos pendentes carregados localmente. Total: {pendingEvents.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Erro ao carregar eventos pendentes: {e.Message}");
                pendingEvents = new List<TrackingEvent>();
            }
        }
        else
        {
            pendingEvents = new List<TrackingEvent>();
            Debug.Log("Arquivo de eventos pendentes não encontrado. Lista vazia.");
        }
    }

    // Classe wrapper para serializar a lista de TrackingEvent
    [System.Serializable]
    private class EventListWrapper
    {
        public List<TrackingEvent> events;
    }

    // ----------------------------------------------------
    // FUNÇÕES DE ARMAZENAMENTO LOCAL
    // ----------------------------------------------------

    private string GetLocalPath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    // Salva o último estoque com sucesso
    private void SaveStockLocally(string jsonStock)
    {
        try
        {
            File.WriteAllText(GetLocalPath(STOCK_FILE_NAME), jsonStock);
            Debug.Log("Estoque salvo localmente com sucesso.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao salvar estoque localmente: {e.Message}");
        }
    }

    // Carrega o estoque local
    private void LoadStockLocally()
    {
        string path = GetLocalPath(STOCK_FILE_NAME);
        if (File.Exists(path))
        {
            try
            {
                string jsonStock = File.ReadAllText(path);
                ApiTotemStockResponse response = JsonUtility.FromJson<ApiTotemStockResponse>(jsonStock);
                lastStockResponse = response;

                if (response != null)
                {
                    CurrentDifficulty = response.totemConfig?.difficulty ?? "NORMAL";
                    GameTime = response.totemConfig?.gameTime ?? 30;
                    StockItems = response.items ?? new List<TotemItem>();
                    LastStockJson = jsonStock;
                    Debug.Log($"Estoque carregado localmente. Dificuldade: {CurrentDifficulty} | GameTime: {GameTime}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Erro ao carregar estoque local: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("Arquivo de estoque local não encontrado. Usando valores padrão.");
        }
    }

    // Salva a lista de batches pendentes
    private void SavePendingBatches()
    {
        try
        {
            // Wrapper para serializar a lista
            string json = JsonUtility.ToJson(new BatchListWrapper { batches = pendingBatches });
            File.WriteAllText(GetLocalPath(BATCH_FILE_NAME), json);
            Debug.Log($"Batches pendentes salvos localmente. Total: {pendingBatches.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao salvar batches pendentes: {e.Message}");
        }
    }

    // Carrega a lista de batches pendentes
    private void LoadPendingBatches()
    {
        string path = GetLocalPath(BATCH_FILE_NAME);
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                BatchListWrapper wrapper = JsonUtility.FromJson<BatchListWrapper>(json);
                pendingBatches = wrapper.batches ?? new List<BatchPayload>();
                Debug.Log($"Batches pendentes carregados localmente. Total: {pendingBatches.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Erro ao carregar batches pendentes: {e.Message}");
                pendingBatches = new List<BatchPayload>();
            }
        }
        else
        {
            pendingBatches = new List<BatchPayload>();
            Debug.Log("Arquivo de batches pendentes não encontrado. Lista vazia.");
        }
    }

    // Classe wrapper para serializar a lista de BatchPayload
    [System.Serializable]
    private class BatchListWrapper
    {
        public List<BatchPayload> batches;
    }

    // ----------------------------------------------------
    // FUNÇÃO DE SINCRONIZAÇÃO
    // ----------------------------------------------------

	    public IEnumerator SyncPendingBatches()
	    {
	        // 1. Sincroniza vitórias pendentes primeiro
	        yield return SyncPendingWins();
	
	        // 2. Sincroniza batches pendentes (se houver)
	        if (!pendingBatches.Any())
	        {
	            Debug.Log("Nenhum batch pendente para sincronizar.");
	            yield break;
	        }
	
	        Debug.Log($"Iniciando sincronização de {pendingBatches.Count} batches pendentes...");
	
	        List<BatchPayload> batchesToSend = new List<BatchPayload>(pendingBatches);
	        pendingBatches.Clear(); // Limpa a lista localmente antes de enviar
	        SavePendingBatches(); // Salva a lista vazia
	
	        int successCount = 0;
	        foreach (var batch in batchesToSend)
	        {
	            string jsonBatch = JsonUtility.ToJson(batch);
	            string result = "";
	            yield return PostBatch(jsonBatch, (response) =>
	            {
	                result = response;
	            });
	
	            if (!result.StartsWith("ERRO"))
	            {
	                successCount++;
	                Debug.Log($"Batch sincronizado com sucesso: {result}");
	            }
	            else
	            {
	                // Se falhar, adiciona de volta à lista para tentar novamente depois
	                pendingBatches.Add(batch);
	                Debug.LogError($"Falha ao sincronizar batch: {result}. Adicionado de volta à fila.");
	            }
	        }
	
	        SavePendingBatches(); // Salva o que sobrou (se houver falhas)
	        Debug.Log($"Sincronização finalizada. Sucesso: {successCount}, Pendentes: {pendingBatches.Count}");
	    }

    public IEnumerator SyncPendingEvents()
    {
        if (!pendingEvents.Any())
        {
            Debug.Log("Nenhum evento pendente para sincronizar.");
            yield break;
        }

        Debug.Log($"Iniciando sincronização de {pendingEvents.Count} eventos pendentes...");

        List<TrackingEvent> eventsToSend = new List<TrackingEvent>(pendingEvents);
        pendingEvents.Clear(); // Limpa a lista localmente antes de enviar
        SavePendingEvents(); // Salva a lista vazia

        int successCount = 0;
        foreach (var trackingEvent in eventsToSend)
        {
            string jsonBody = JsonUtility.ToJson(trackingEvent);
            string result = "";
            yield return SendRequest("/totem-sync/event", "POST", jsonBody, (response) =>
            {
                result = response;
            });

            if (!result.StartsWith("ERRO"))
            {
                successCount++;
                Debug.Log($"Evento sincronizado com sucesso: {result}");
            }
            else
            {
                // Se falhar, adiciona de volta à lista para tentar novamente depois
                pendingEvents.Add(trackingEvent);
                Debug.LogError($"Falha ao sincronizar evento: {result}. Adicionado de volta à fila.");
            }
        }

        SavePendingEvents(); // Salva o que sobrou (se houver falhas)
        Debug.Log($"Sincronização de eventos finalizada. Sucesso: {successCount}, Pendentes: {pendingEvents.Count}");
    }

    private IEnumerator PeriodicPingAndSync()
    {
        // Espera 30 segundos antes do primeiro ping periódico
        yield return new WaitForSeconds(15f);

        while (true)
        {
            Debug.Log("Iniciando Ping Periódico...");
            bool isOnline = false;
            yield return Ping((response) =>
            {
                if (!response.StartsWith("ERRO"))
                {
                    isOnline = true;
                    Debug.Log("Ping Periódico bem-sucedido. Online.");
                }
                else
                {
                    Debug.LogWarning("Ping Periódico falhou. Offline.");
                }
            });

            if (isOnline)
            {
	            // Tenta sincronizar batches e eventos
	                yield return SyncPendingBatches();
	                yield return SyncPendingEvents();
            }

            // Espera mais 30 segundos
            yield return new WaitForSeconds(15f);
        }
    }

    // Classes de dados para o JSON de Batch
    [System.Serializable]
    public class BatchRecord
    {
        public string localRecordId;
        public string giftId;
        public string result; // WIN, LOSE, etc.
        public string timestamp;
        public long duration; // em milissegundos
    }

    [System.Serializable]
	    public class StockUpdate
	    {
	        public string giftId;
	        public int remaining;
	        public int decrement; // NOVO: Quantidade a ser decrementada do estoque
	    public int minPresents; // NOVO: Mínimo de brindes coletados para este prêmio
	    public string imageUrl; // NOVO: URL da imagem do prêmio
	    }

    [System.Serializable]
    public class BatchPayload
    {
        public List<BatchRecord> records = new List<BatchRecord>();
        public List<StockUpdate> stockUpdates = new List<StockUpdate>();
    }
	    // O método UpdateLocalStockRemaining não é mais necessário, pois a atualização
	    // é feita dentro do RegisterWin.
	    // public void UpdateLocalStockRemaining(string giftId, int newRemaining)
	    // {
	    //     TotemItem item = StockItems.FirstOrDefault(i => i.giftId == giftId);
	    //     if (item != null)
	    //     {
	    //         item.remaining = newRemaining;
	    //         Debug.Log($"[LOCAL STOCK UPDATE] {item.name} atualizado para {newRemaining}.");
	    //     }
	    // }

	    // NOVO: Estrutura para armazenar a vitória
	    [System.Serializable]
	    public class WinRecord
	    {
	        public string giftId;
	        public string timestamp;
	        public long duration; // em milissegundos
	    }
	
	    private const string WINS_FILE_NAME = "pending_wins.json";
	
	    // NOVO: Método para registrar uma vitória localmente
	    public void RegisterWin(string giftId, float durationSeconds, System.Action<string> callback)
	    {
	        WinRecord winRecord = new WinRecord
	        {
	            giftId = giftId,
	            timestamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
	            duration = (long)(durationSeconds * 1000) // Converte segundos para milissegundos
	        };
	
	        // 1. Atualiza o estoque localmente (para que o próximo jogo use o estoque correto)
	        TotemItem item = StockItems.FirstOrDefault(i => i.giftId == giftId);
	        if (item != null)
	        {
	            item.remaining = item.remaining - 1;
	            Debug.Log($"[LOCAL STOCK UPDATE] {item.name} atualizado para {item.remaining}.");
	            // Salva o estoque localmente para persistir a mudança
	            SaveStockLocally(JsonUtility.ToJson(lastStockResponse));
	        }
	
	        // 2. Adiciona à lista de vitórias pendentes
	        pendingWins.Add(winRecord);
	        SavePendingWins();
	
	        callback?.Invoke("Vitória registrada localmente.");
	    }
	
	    // NOVO: Salva a lista de vitórias pendentes no disco
	    private void SavePendingWins()
	    {
	        string path = Path.Combine(Application.persistentDataPath, WINS_FILE_NAME);
	        WinListWrapper wrapper = new WinListWrapper { wins = pendingWins };
	        string json = JsonUtility.ToJson(wrapper);
	        File.WriteAllText(path, json);
	        Debug.Log($"Vitórias pendentes salvas localmente. Total: {pendingWins.Count}");
	    }
	
	    // NOVO: Carrega a lista de vitórias pendentes do disco
	    private void LoadPendingWins()
	    {
	        string path = Path.Combine(Application.persistentDataPath, WINS_FILE_NAME);
	        if (File.Exists(path))
	        {
	            try
	            {
	                string json = File.ReadAllText(path);
	                WinListWrapper wrapper = JsonUtility.FromJson<WinListWrapper>(json);
	                pendingWins = wrapper.wins ?? new List<WinRecord>();
	                Debug.Log($"Vitórias pendentes carregadas localmente. Total: {pendingWins.Count}");
	            }
	            catch (Exception e)
	            {
	                Debug.LogError($"Erro ao carregar vitórias pendentes: {e.Message}");
	                pendingWins = new List<WinRecord>();
	            }
	        }
	        else
	        {
	            pendingWins = new List<WinRecord>();
	            Debug.Log("Arquivo de vitórias pendentes não encontrado. Lista vazia.");
	        }
	    }
	
	    // NOVO: Classe wrapper para serializar a lista de WinRecord
	    [System.Serializable]
	    private class WinListWrapper
	    {
	        public List<WinRecord> wins;
	    }
	
	    // NOVO: Função de sincronização de vitórias pendentes
	    public IEnumerator SyncPendingWins()
	    {
	        if (!pendingWins.Any())
	        {
	            Debug.Log("Nenhuma vitória pendente para sincronizar.");
	            yield break;
	        }
	
	        Debug.Log($"Iniciando sincronização de {pendingWins.Count} vitórias pendentes...");
	
	        // Agrupa as vitórias por giftId e conta a quantidade
	        var groupedWins = pendingWins
	            .GroupBy(w => w.giftId)
	            .Select(g => new
	            {
	                GiftId = g.Key,
	                Count = g.Count(),
	                // Pega o timestamp e duration do primeiro registro para o BatchRecord
	                FirstWin = g.First()
	            })
	            .ToList();
	
	        // Cria um BatchPayload único para todas as vitórias
	        BatchPayload payload = new BatchPayload();
	
	        // 1. Cria os BatchRecords (um por vitória)
	        foreach (var win in pendingWins)
	        {
	            BatchRecord winRecord = new BatchRecord
	            {
	                localRecordId = System.Guid.NewGuid().ToString(),
	                giftId = win.giftId,
	                result = "WIN",
	                timestamp = win.timestamp,
	                duration = win.duration
	            };
	            payload.records.Add(winRecord);
	        }
	
	        // 2. Cria as StockUpdates (uma por giftId único)
	        foreach (var group in groupedWins)
	        {
	            // Calcula o novo remaining no servidor
		            // A lógica é: o estoque atual no servidor (antes do sync) menos a quantidade de vitórias offline.
		            // A abordagem mais robusta é enviar a quantidade de decremento (group.Count)
		            // e o backend faz o cálculo.
		
		            TotemItem item = StockItems.FirstOrDefault(i => i.giftId == group.GiftId);
		            if (item != null)
		            {
		                StockUpdate stockUpdate = new StockUpdate
		                {
		                    giftId = group.GiftId,
		                    // Envia a quantidade de decremento
		                    decrement = group.Count,
		                    // O valor de 'remaining' no StockItems já está correto (decrementado)
		                    // Mas é o valor local. Vamos enviar o decremento para o backend calcular.
		                    remaining = item.remaining // Mantemos o remaining para debug/validação, mas o backend deve usar 'decrement'
		                };
		                payload.stockUpdates.Add(stockUpdate);
		                Debug.Log($"[SYNC BATCH] StockUpdate para {group.GiftId}: Decremento de {group.Count}. Remaining local: {item.remaining}");
		            }
	            else
	            {
	                Debug.LogError($"[SYNC BATCH] Item {group.GiftId} não encontrado no estoque local. Ignorando StockUpdate.");
	            }
	        }
	
	        // Converte o payload para JSON
	        string jsonBatch = JsonUtility.ToJson(payload);
	        Debug.Log($"Tentando enviar Batch de Sincronização: {jsonBatch}");
	
	        string result = "";
	        Debug.Log($"[SYNC] Enviando Batch de Sincronização para o servidor...");
	        yield return PostBatch(jsonBatch, (response) =>
	        {
	            result = response;
	        });
	
	        if (!result.StartsWith("ERRO"))
	        {
	            // Se for sucesso, limpa a lista de vitórias pendentes
	            pendingWins.Clear();
	            SavePendingWins();
	            Debug.Log($"[SYNC SUCESSO] Sincronização de {payload.records.Count} vitórias e {payload.stockUpdates.Count} StockUpdates concluída com sucesso. Resposta do servidor: {result}");
	        }
	        else
	        {
	            // Se falhar, as vitórias pendentes permanecem na lista para a próxima tentativa
	            Debug.LogError($"[SYNC FALHA] Falha ao sincronizar vitórias: {result}. Elas serão tentadas novamente.");
	        }
	    }
	
    // ----------------------------------------------------
    // FUNÇÕES DE CACHE DE IMAGEM
    // ----------------------------------------------------

    // NOVO: Método auxiliar para obter o caminho de cache local
    public string GetLocalImagePath(string imageUrl)
    {
        // Usa o hash da URL para criar um nome de arquivo único e seguro
        using (var sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(imageUrl));
            string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            
            // Usa Application.persistentDataPath para salvar em um local seguro e persistente
            return Path.Combine(Application.persistentDataPath, hash + ".png");
        }
    }

    // NOVO: Corrotina para pré-cache de imagens
    private IEnumerator PreCacheImages(List<TotemItem> items)
    {
        if (items == null) yield break;

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.imageUrl)) continue;

            string localPath = GetLocalImagePath(item.imageUrl);

            // 1. Tenta carregar do cache local
            if (File.Exists(localPath))
            {
                Debug.Log($"[CACHE] Imagem já existe no cache: {localPath}");
                continue; // Pula para o próximo item
            }

            // 2. Se não estiver no cache, baixa da URL
            Debug.Log($"[CACHE] Baixando imagem para cache: {item.imageUrl}");
            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(item.imageUrl))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[CACHE] Erro ao baixar imagem {item.imageUrl}: {www.error}");
                }
                else
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(www);
                    
                    // 3. Salva no cache local
                    try
                    {
                        byte[] bytes = texture.EncodeToPNG(); // Codifica para PNG para salvar
                        File.WriteAllBytes(localPath, bytes);
                        Debug.Log($"[CACHE] Imagem salva no cache: {localPath}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[CACHE] Erro ao salvar imagem no cache: {e.Message}");
                    }
                }
            }
            // Pequena pausa para não travar o jogo
            yield return null;
        }
        Debug.Log("[CACHE] Pré-cache de imagens finalizado.");
        gameController.LoadUpdatedConfig();
    }

    public IEnumerator Ping(System.Action<string> callback)
    {
        string body = "{\"ping\": true}";
        // O Ping não deve usar o SendRequest normal, pois ele não deve ser afetado pela lógica de offline
        // O SendRequest normal já faz o Ping dentro do LoadConfig.
        // Este método é usado *para* verificar a conexão.

        TotemConfig cfg = ConfigManager.Load();
        string url = $"{BaseUrl}/totem-sync/ping";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        // headers obrigatórios
        request.SetRequestHeader("x-unity-key", cfg.unityKey);
        request.SetRequestHeader("x-device-secret", cfg.deviceSecret);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            callback?.Invoke($"ERRO: {request.result}\n{request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        callback?.Invoke(request.downloadHandler.text);
    }
}
