using UnityEngine;
using UnityEngine.UI;
using TMPro; // Adicionar para TextMeshPro
using System.Collections.Generic;
// using System.Security.Cryptography; // Removido, agora está no TotemApiClient
using System.Collections; // Adicionado para IEnumerator
using System.Linq;
using static TotemApiClient;
using UnityEngine.SceneManagement;
using System.Collections; // Para acessar ApiTotemStockResponse e ApiTotemConfig

// A classe TotemItem precisa estar no escopo global para ser acessível pelo TotemApiClient
// e para que o PrizeManager possa usá-la diretamente.
[System.Serializable]
public class TotemItem
{
    public string giftId;
    public string name;
    public int totalStock;
    public int remaining;
    public int minPresents; // NOVO: Mínimo de brindes coletados para este prêmio
    public string imageUrl; // NOVO: URL da imagem do prêmio
}

public class PrizeManager : MonoBehaviour
{
    public static float LastGameDurationSeconds = 0f;
    public static int CollectedGiftsCount = 0; // Para ser setado antes de carregar a cena EndGame

    [Header("UI References")]
    public Image prizeImage;
    public TextMeshProUGUI prizeNameText; // NOVO: Referência para o TextMeshPro
    public Sprite fallbackSprite; // Imagem de fallback para quando o cache/download falhar

    // O mapeamento de prêmios agora será feito diretamente com base nos TotemItems carregados.
    // A lista prizeMap não é mais necessária, pois a lógica de minCollectedCount (agora minPresents)
    // virá diretamente do TotemItem.
    // Mantemos a estrutura CountPrizeMap apenas para referência, mas ela será removida.
    // O script usará a lista TotemApiClient.Instance.StockItems.

    // Estrutura para armazenar o resultado final do jogo

    // Estrutura para armazenar o resultado final do jogo
    public static Dictionary<string, int> FinalPrizeDistribution = new Dictionary<string, int>();

    void Start()
    {
        // Pega a contagem de brindes coletados do ClawController (setado antes de carregar a cena)
        CollectedGiftsCount = 0;
        DeterminePrize();
        // StartCoroutine(WaitToReload());
    }

    IEnumerator WaitToReload() {
        yield return new WaitForSeconds(5f);
        SceneManager.LoadScene(0);
    }

    public void ReloadScene()
    {
        SceneManager.LoadScene(0);
    }

    private void DeterminePrize()
    {
        TotemApiClient apiClient = TotemApiClient.Instance;

        if (apiClient == null)
        {
            Debug.LogError("TotemApiClient não encontrado!");
        //    return;
        }

        if (prizeImage == null)
        {
            Debug.LogError("Prize Image não atribuído no Inspector!");
        //    return;
        }

        if (prizeNameText == null)
        {
            Debug.LogWarning("Prize Name Text (TextMeshProUGUI) não atribuído no Inspector. O nome do prêmio não será exibido.");
            // Não retorna, apenas avisa, pois a imagem e o resto da lógica ainda podem funcionar.
        }

        // 1. Limpa o resultado anterior
        FinalPrizeDistribution.Clear();

        // 2. Verifica se algum brinde foi coletado
        //if (CollectedGiftsCount == 0)
        //{
        //    Debug.Log("Nenhum brinde coletado. Não há prêmio.");
        //    prizeImage.enabled = false;
        //    return;
        //}

        // 3. Determina o prêmio baseado na contagem
        // O prêmio é o que tem o maior minPresents que é <= CollectedGiftsCount

        // 4. Obtém o estoque atual
        ApiTotemStockResponse stockResponse = apiClient.GetLastStockResponse();

        if (stockResponse == null || stockResponse.items == null)
        {
            Debug.LogError("Resposta de estoque inválida ou não carregada!");
            return;
        }

        // Lógica de seleção: Encontra o item com o maior minPresents que é menor ou igual a CollectedGiftsCount E que está em estoque
        // 1. Filtra todos os itens elegíveis e em estoque
        var eligibleAndInStock = stockResponse.items
            .Where(item => item.minPresents <= CollectedGiftsCount && item.remaining > 0)
            .ToList();

        TotemItem awardedPrize = null;

        if (eligibleAndInStock.Count > 0)
        {
            // 2. Encontra o maior minPresents entre os elegíveis
            int maxMinPresents = eligibleAndInStock.Max(item => item.minPresents);

            // 3. Filtra a lista para incluir apenas os prêmios com o maior minPresents (o "nível" de maior elegibilidade)
            List<TotemItem> topTierPrizes = eligibleAndInStock
                .Where(item => item.minPresents == maxMinPresents)
                .ToList();

            // 4. Seleciona um prêmio aleatoriamente da lista de maior elegibilidade
            int randomIndex = Random.Range(0, topTierPrizes.Count);
            awardedPrize = topTierPrizes[randomIndex];
        }
        // Pega o primeiro (o maior elegível e em estoque)

        if (awardedPrize == null)
        {
            Debug.LogWarning($"Nenhum prêmio elegível E em estoque encontrado para {CollectedGiftsCount} brindes coletados. Transicionando para GameOver.");
            //FindObjectOfType<TransitionPlayer>().PlayTransition("GameOver");
            SceneManager.LoadScene("GameOver");
            return;
        }

        string awardedGiftId = awardedPrize.giftId;
        Debug.Log($"Prêmio determinado: {awardedGiftId} (Mínimo: {awardedPrize.minPresents}, Estoque: {awardedPrize.remaining})");

        // As verificações de awardedPrize == null e awardedPrize.remaining <= 0 foram movidas para o filtro LINQ.
        // Apenas mantemos a verificação de awardedPrize == null para o caso de nenhum item ser encontrado.

        // 5. O prêmio já está em estoque devido ao filtro LINQ.
        // Removemos a verificação redundante de estoque aqui.

        // 6. Registra a distribuição final (1 unidade do prêmio)
        FinalPrizeDistribution.Add(awardedGiftId, 1);

        // NOVO: Exibe o nome do prêmio
        if (prizeNameText != null)
        {
            prizeNameText.text = awardedPrize.name;
        }

        // 7. Carrega a imagem do prêmio via URL (com cache local)
        if (string.IsNullOrEmpty(awardedPrize.imageUrl))
        {
            Debug.LogError($"URL da imagem não encontrada para GiftID={awardedPrize.giftId}");
            prizeImage.enabled = false;
            return;
        }

        // Inicia a corrotina para carregar a imagem do cache
        apiClient.StartCoroutine(LoadImageFromCache(awardedPrize.imageUrl));

        Debug.Log($"🏆 Prêmio sorteado: {awardedPrize.name} (ID: {awardedPrize.giftId})");

        // 8. Prepara e envia o Batch
        // O batch deve subtrair 1 do estoque do prêmio atribuído.
        // **NOVO**: Não atualiza o estoque localmente nem envia o batch aqui.
        // Apenas registra a vitória no TotemApiClient para ser processada no Sync.
        float duration = LastGameDurationSeconds > 0 ? LastGameDurationSeconds : 5f;

        Debug.Log($"Registrando Vitória: GiftID={awardedPrize.giftId}, Duration={duration}s");

        apiClient.RegisterWin(
            awardedPrize.giftId,
            duration,
            (response) =>
            {
                if (response.StartsWith("ERRO"))
                    Debug.LogError("Erro ao registrar Vitória: " + response);
                else
                    Debug.Log("Vitória registrada com sucesso: " + response);
            }
        );
    }

    // NOVO: Método auxiliar para aplicar a textura ao componente Image
    private void ApplyTextureToImage(Texture2D texture)
    {
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
        prizeImage.sprite = sprite;
        prizeImage.enabled = true;
        Debug.Log("Imagem do prêmio aplicada com sucesso.");
    }

    private void ApplyFallback()
    {
        if (fallbackSprite != null)
        {
            prizeImage.sprite = fallbackSprite;
            prizeImage.enabled = true;
            Debug.LogWarning("Aplicando imagem de fallback.");
        }
        else
        {
            prizeImage.enabled = false;
            Debug.LogError("Imagem de fallback não configurada e imagem real não pôde ser carregada.");
        }
    }

    // NOVO: Corrotina para carregar a imagem do cache local
    private IEnumerator LoadImageFromCache(string url)
    {
        TotemApiClient apiClient = TotemApiClient.Instance;
        string localPath = apiClient.GetLocalImagePath(url);
        Texture2D texture = null;

        // 1. Tenta carregar do cache local
        if (System.IO.File.Exists(localPath))
        {
            Debug.Log($"Carregando imagem do cache: {localPath}");
            try
            {
                byte[] fileData = System.IO.File.ReadAllBytes(localPath);
                texture = new Texture2D(2, 2);
                if (texture.LoadImage(fileData)) // Carrega a imagem do array de bytes
                {
                    ApplyTextureToImage(texture);
                    yield break; // Imagem carregada do cache, finaliza
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Falha ao carregar imagem do cache ({e.Message}). Aplicando fallback.");
            }
        }

        // Se chegou aqui, o cache falhou ou não existe.
        ApplyFallback();
        yield break;
    }
}
