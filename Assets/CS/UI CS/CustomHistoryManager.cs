using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using ARFurniture; // 為了使用 ModelLoader1
using System.Linq; // 為了使用 .Find()

public class CustomHistoryManager : MonoBehaviour
{
    [Header("面板與按鈕參考")]
    public GameObject panelRoot;
    public GameObject historyPanel;           // 客製化歷史紀錄的主面板
    public GameObject productListPanel;       // 原始商品列表的主面板
    public Button customizeHistoryButton;     // ModelConverter 上的歷史按鈕

    [Header("歷史項目 UI")]
    public GameObject customProductItemPrefab; // 歷史紀錄項目的 Prefab
    public Transform contentParent;            // 放置歷史項目的容器 (Content)
    public GameObject loadingPanel;            // 載入提示面板

    [Header("功能腳本參考")]
    public ProductListManager productListManager; // 用於取得原始商品資料
    public ModelLoader1 modelLoader1;             // 用於觸發 AR 載入

    [Header("狀態顯示用 Sprite")]
    public Sprite statusDoneSprite;
    public Sprite statusNotDoneSprite;

    // PlayerPrefs 的儲存鍵 (必須與 ModelConverter.cs 中的一致)
    private const string HistoryPlayerPrefsKey = "ModelConversionHistory";
    private Dictionary<string, ModelConverter.ConversionData> conversionHistory;

    // 用於解析 action=accept API 回應的結構
    [System.Serializable]
    private class AcceptApiResponse
    {
        public bool success;
        public string message;
    }

    void Start()
    {
        if (customizeHistoryButton != null)
        {
            customizeHistoryButton.onClick.AddListener(ToggleHistoryPanel);
        }
        else
        {
            Debug.LogError("CustomizeHistoryButton 尚未在 Inspector 中指派！");
        }

        // 初始狀態下，隱藏歷史面板
        if (historyPanel != null)
        {
            historyPanel.SetActive(false);
        }
    }

    // 按下按鈕時觸發的切換功能
    private void ToggleHistoryPanel()
    {
        bool isHistoryPanelActive = historyPanel.activeSelf;

        // 切換面板可見性
        historyPanel.SetActive(!isHistoryPanelActive);
        productListPanel.SetActive(isHistoryPanelActive);

        // 如果是正要顯示歷史面板，就開始載入並更新內容
        if (!isHistoryPanelActive)
        {
            StartCoroutine(LoadAndDisplayHistory());
        }
    }

    // 載入並顯示所有歷史紀錄的協程
    private IEnumerator LoadAndDisplayHistory()
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);

        // 清空現有的歷史項目
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        // 從 PlayerPrefs 載入歷史紀錄
        LoadHistoryFromPlayerPrefs();
        if (conversionHistory == null || conversionHistory.Count == 0)
        {
            Debug.Log("沒有找到任何客製化歷史紀錄。");
            if (loadingPanel != null) loadingPanel.SetActive(false);
            // 可在此處顯示 "無歷史紀錄" 的提示 UI
            yield break;
        }

        // 遍歷每一筆紀錄，檢查狀態並生成 UI
        foreach (var item in conversionHistory)
        {
            ModelConverter.ConversionData historyData = item.Value;
            string cleanedID = item.Key.Trim();
            string convertID = UnityWebRequest.EscapeURL(cleanedID);

            // 組合 API URL
            string url = $"http://140.127.114.38:5008/php/3d_convert.php?action=accept&id={convertID}";

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                string responseText = "";
                if (www.result == UnityWebRequest.Result.Success)
                {
                    responseText = www.downloadHandler.text;
                }
                else if (www.responseCode == 400)
                {
                    // 伺服器回傳 400，視為「轉換未完成」的特殊情況
                    Debug.LogWarning($"收到伺服器 400 錯誤，但將其視為『轉換未完成』處理。ConvertID: {cleanedID}");
                    // 即使是 400 錯誤，依然嘗試讀取回應的內容
                    responseText = www.downloadHandler.text;
                }
                else // 其他所有真正的錯誤 (如 500 伺服器內部錯誤、網路中斷等)
                {
                    Debug.LogError($"檢查 ConvertID {cleanedID} 狀態失敗: {www.error}");
                    responseText = "Error";
                }
                
                // 根據原始商品 ID 查找商品資訊
                NewJSONProduct originalProduct = productListManager.allRawProducts.FirstOrDefault(p => p.id == historyData.id);
                if (originalProduct == null) {
                    Debug.LogWarning($"在 ProductListManager 中找不到 ID 為 {historyData.id} 的原始商品");
                }
                
                // 創建並填充 UI 項目
                CreateHistoryItemUI(historyData, originalProduct, responseText, url);
            }
        }

        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    // 創建並填充單個歷史項目 UI
    void CreateHistoryItemUI(ModelConverter.ConversionData historyData, NewJSONProduct originalProduct, string apiResponse, string requestUrl)
    {
        GameObject itemObj = Instantiate(customProductItemPrefab, contentParent);

        // 獲取 UI 元件參考
        Button viewARButton = itemObj.transform.Find("ViewARButton").GetComponent<Button>();
        TextMeshProUGUI statusText = itemObj.transform.Find("StatusText").GetComponent<TextMeshProUGUI>();
        Button deleteButton = itemObj.transform.Find("DeleteButton").GetComponent<Button>();
        Image productImage = itemObj.transform.Find("ProductImage/Image").GetComponent<Image>();
        Text nameText = itemObj.transform.Find("NameText").GetComponent<Text>();
        Text convertText = itemObj.transform.Find("convertText").GetComponent<Text>();
        Image statImg = itemObj.transform.Find("statImg").GetComponent<Image>();

        // 填充通用資訊
        nameText.text = originalProduct?.name ?? "未知商品";
        convertText.text = $"Prompt: {historyData.prompt}";

        string convertIDToDelete = conversionHistory.FirstOrDefault(x => x.Value == historyData).Key;
        deleteButton.onClick.AddListener(() => {
            DeleteHistoryItem(convertIDToDelete, itemObj);
        });
        
        // 下載原始商品圖片
        if (originalProduct != null && !string.IsNullOrEmpty(originalProduct.proxy_image_url))
        {
            StartCoroutine(DownloadImage(productImage, originalProduct.proxy_image_url));
        }

        // 解析 API 回應，判斷轉換狀態
        bool isConversionDone = false;
        string newModelUrl = "";

        try
        {
            // 嘗試解析為 JSON，如果成功，代表尚未完成
            JsonConvert.DeserializeObject<AcceptApiResponse>(apiResponse);
            isConversionDone = false;
        }
        catch (JsonException)
        {
            // 解析 JSON 失敗，代表回傳的是空內容，轉換已完成
            isConversionDone = true;
            // 根據新的規則，請求的 URL 本身就是新的模型 URL
            newModelUrl = requestUrl;
        }
        
        // 根據狀態更新 UI
        if (isConversionDone)
        {
            statusText.text = "Convert Done!";
            statImg.sprite = statusDoneSprite;
            viewARButton.gameObject.SetActive(true);

            // 為 AR 按鈕設定點擊事件
            viewARButton.onClick.AddListener(() => {
                // 準備 ProductData
                ProductData pd = new ProductData
                {
                    modelURL = newModelUrl,
                    productName = $"{originalProduct.name} (Custom)",
                    productId = historyData.id, // 可保留原始 ID
                    from = true
                };

                // 呼叫 ModelLoader1 的方法
                modelLoader1.SetModelToLoad(newModelUrl, pd);

                // 點擊後自動切換回主畫面，方便使用者放置模型
                productListManager.OnClickToggleImage();
            });
        }
        else
        {
            statusText.text = "Convert not Done";
            statImg.sprite = statusNotDoneSprite;
            viewARButton.gameObject.SetActive(false);
        }
    }

    public void DeleteHistoryItem(string convertID, GameObject itemObject)
{
    // 1. 從記憶體中的字典移除
    if (conversionHistory.ContainsKey(convertID))
    {
        conversionHistory.Remove(convertID);
    }

    // 2. 更新 PlayerPrefs
    string jsonHistory = JsonConvert.SerializeObject(conversionHistory, Formatting.Indented);
    PlayerPrefs.SetString(HistoryPlayerPrefsKey, jsonHistory);
    PlayerPrefs.Save();

    // 3. 從畫面上移除 UI 物件
    Destroy(itemObject);

    // 4. 檢查是否還有歷史紀錄
    if (conversionHistory.Count == 0)
    {
        Debug.Log("所有歷史紀錄已被刪除。");
        
        // 隱藏歷史按鈕
        if(customizeHistoryButton != null)
        {
            customizeHistoryButton.gameObject.SetActive(false);
        }

        // 隱藏歷史面板，顯示商品列表
        historyPanel.SetActive(false);
        productListPanel.SetActive(true);
    }
}

    // 從 PlayerPrefs 載入歷史紀錄
    private void LoadHistoryFromPlayerPrefs()
    {
        if (PlayerPrefs.HasKey(HistoryPlayerPrefsKey))
        {
            string jsonHistory = PlayerPrefs.GetString(HistoryPlayerPrefsKey);
            conversionHistory = JsonConvert.DeserializeObject<Dictionary<string, ModelConverter.ConversionData>>(jsonHistory);
        }
        else
        {
            conversionHistory = new Dictionary<string, ModelConverter.ConversionData>();
        }
    }

    // 協程：下載圖片並設置
    private IEnumerator DownloadImage(Image image, string url)
    {
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
        else
        {
            Debug.Log($"下載圖片失敗: {url} - {request.error}");
        }
    }
}