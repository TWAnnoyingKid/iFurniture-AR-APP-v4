using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

public class ModelConverter : MonoBehaviour
{
    [Header("UI 元件參考")]
    public GameObject customizePanel;
    public InputField customizePartInput; // 對應 "part" 參數的輸入框
    public InputField customizeInput;     // 對應 "prompt" 參數的輸入框
    public Button customizeSendButton;    // 送出請求的按鈕
    public Button customizeCancelButton;   // 取消請求的按鈕
    public Button customizeHistoryButton;  // 顯示歷史紀錄的按鈕

    [Header("API 設置")]
    private const string ApiBaseUrl = "http://140.127.114.38:5008/php/3d_convert.php";
    
    // 用來儲存當前要客製化的模型 ID
    private string currentModelId;

    // PlayerPrefs 的儲存鍵
    private const string HistoryPlayerPrefsKey = "ModelConversionHistory";

    // 記憶體中的歷史紀錄字典
    private Dictionary<string, ConversionData> conversionHistory;

    #region 資料結構定義

    // 用於解析 API 回應的 JSON
    [System.Serializable]
    private class ApiResponse
    {
        public bool success;
        public string convertID;
    }

    // 用於儲存每筆轉換紀錄的詳細資料
    [System.Serializable]
    public class ConversionData
    {
        public string id;
        public string part;
        public string prompt;
    }

    #endregion

    void Start()
    {
        // 載入儲存在裝置上的歷史紀錄
        LoadHistory();

        // 根據是否有歷史紀錄來決定是否顯示歷史按鈕
        UpdateHistoryButtonVisibility();

        if (customizeSendButton != null)
        {
            customizeSendButton.onClick.AddListener(OnCustomizeSend);
        }
        else
        {
            Debug.LogError("CustomizeSendButton 尚未在 Inspector 中指派！");
        }

        if (customizeCancelButton != null)
        {
            customizeCancelButton.onClick.AddListener(OnCustomizeCancel);
        }
        else
        {
            Debug.LogError("CustomizeCancelButton 尚未在 Inspector 中指派！");
        }
    }

    private void OnCustomizeCancel()
    {
        // 清空兩個輸入框的文字
        if (customizePartInput != null)
        {
            customizePartInput.text = "";
        }
        if (customizeInput != null)
        {
            customizeInput.text = "";
        }

        // 關閉面板
        if (customizePanel != null)
        {
            customizePanel.SetActive(false);
        }
    }

    // 設定當前要進行客製化的模型 ID (這個方法需要從外部呼叫，例如從 ModelLoader1.cs)
    /// <param name="modelId">模型的 ID</param>
    public void SetCurrentModelId(string modelId)
    {
        this.currentModelId = modelId;
        Debug.Log($"目前的模型 ID 已設定為: {this.currentModelId}");
    }

    // 當 "CustomizeSend" 按鈕被點擊時觸發
    private void OnCustomizeSend()
    {
        // 檢查參數是否齊全
        if (string.IsNullOrEmpty(currentModelId))
        {
            Debug.LogError("模型 ID 未設定，無法發送請求！");
            return;
        }

        string part = customizePartInput.text;
        string prompt = customizeInput.text;

        if (string.IsNullOrEmpty(part) || string.IsNullOrEmpty(prompt))
        {
            Debug.LogWarning("部位 (part) 或 提示 (prompt) 為空，請輸入內容。");
            return;
        }

        // 開始呼叫 API 的協程
        StartCoroutine(CallConvertApiCoroutine(currentModelId, part, prompt));
    }

    // 呼叫轉換 API 的協程
    private IEnumerator CallConvertApiCoroutine(string modelId, string part, string prompt)
    {
        // 使用 UnityWebRequest.EscapeURL 來對參數進行 URL 編碼，確保特殊字元 (如空格) 能正確傳遞
        string encodedPart = UnityWebRequest.EscapeURL(part);
        string encodedPrompt = UnityWebRequest.EscapeURL(prompt);

        // 組合完整的 API 網址
        string fullUrl = $"{ApiBaseUrl}?action=convert&id={modelId}&part={encodedPart}&prompt={encodedPrompt}";

        Debug.Log($"正在呼叫 API: {fullUrl}");

        using (UnityWebRequest www = UnityWebRequest.Get(fullUrl))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                // API 呼叫成功
                string jsonResponse = www.downloadHandler.text;
                Debug.Log($"收到 API 回應: {jsonResponse}");

                try
                {
                    // 解析 JSON 回應
                    ApiResponse response = JsonConvert.DeserializeObject<ApiResponse>(jsonResponse);

                    if (response.success && !string.IsNullOrEmpty(response.convertID))
                    {
                        // 建立新的紀錄資料
                        ConversionData newRecord = new ConversionData
                        {
                            id = modelId,
                            part = part,    // 儲存原始文字，而非編碼後的
                            prompt = prompt // 儲存原始文字
                        };

                        // 將新紀錄加入到字典中
                        conversionHistory[response.convertID] = newRecord;

                        // 儲存更新後的歷史紀錄到裝置
                        SaveHistory();

                        Debug.Log($"成功紀錄 ConvertID: {response.convertID}");

                        // 紀錄成功 清空所有input並關閉面板
                        OnCustomizeCancel();
                    }
                    else
                    {
                        Debug.LogError($"API 回應顯示失敗或缺少 convertID。");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"解析 API 回應失敗: {e.Message}");
                }
            }
            else
            {
                // API 呼叫失敗
                Debug.LogError($"API 請求失敗: {www.error}");
            }
        }
    }

    // 從 PlayerPrefs 載入歷史紀錄>
    private void LoadHistory()
    {
        if (PlayerPrefs.HasKey(HistoryPlayerPrefsKey))
        {
            string jsonHistory = PlayerPrefs.GetString(HistoryPlayerPrefsKey);
            // 使用 Newtonsoft.Json 將 JSON 字串反序列化回字典物件
            conversionHistory = JsonConvert.DeserializeObject<Dictionary<string, ConversionData>>(jsonHistory);
            Debug.Log($"已成功從 PlayerPrefs 載入 {conversionHistory.Count} 筆歷史紀錄。");
        }
        
        // 如果沒有紀錄或載入失敗，則初始化一個新的空字典
        if (conversionHistory == null)
        {
            conversionHistory = new Dictionary<string, ConversionData>();
            Debug.Log("未找到歷史紀錄，已初始化新的紀錄字典。");
        }
    }

    // 將目前的歷史紀錄儲存到 PlayerPrefs
    private void SaveHistory()
    {
        // 使用 Newtonsoft.Json 將字典物件序列化成 JSON 字串
        string jsonHistory = JsonConvert.SerializeObject(conversionHistory, Formatting.Indented);
        PlayerPrefs.SetString(HistoryPlayerPrefsKey, jsonHistory);
        PlayerPrefs.Save(); // 強制寫入磁碟，確保資料不會遺失

        Debug.Log("歷史紀錄已儲存到 PlayerPrefs。");
        
        // 每次儲存後都更新一次按鈕的可見性
        UpdateHistoryButtonVisibility();
    }

    // 根據歷史紀錄是否存在來更新歷史按鈕的顯示狀態
    public void UpdateHistoryButtonVisibility()
    {
        if (customizeHistoryButton != null)
        {
            // 如果字典不為空且至少有一筆紀錄，則顯示按鈕，否則隱藏
            bool hasHistory = conversionHistory != null && conversionHistory.Count > 0;
            customizeHistoryButton.gameObject.SetActive(hasHistory);
        }
    }
}