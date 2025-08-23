using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System;
using System.Linq;

public class SearchDialogManager : MonoBehaviour
{
    [System.Serializable]
    private class SearchIdResponse
    {
        public bool success;
        public string[] product_ids;
    }

    [Header("搜尋對話框 UI 元件")]
    public GameObject searchDialog;
    public Dropdown categoryDropdown;
    public Button confirmButton;
    public Button cancelButton;
    public Button searchButton;
    public InputField searchInputField;
    public Button clearSearchButton;
    public Button reloadButton;


    [Header("API 設定")]
    public string apiBaseUrl = "http://140.127.114.38:5008/php";   

    private Dictionary<string, string> categoryMapping = new Dictionary<string, string>()
    {
        { "全部", "All" },
        { "吧台椅", "Bar Stool" },
        { "餐桌", "Dining Table" },
        { "衣櫃", "Wardrobe" },
        { "化妝台", "Vanity Table" },
        { "書架", "Bookshelf" },
        { "收納腳凳", "Storage Ottoman" },
        { "餐具櫃", "Sideboard" },
        { "餐椅", "Dining Chair" },
        { "沙發", "Sofa" },
        { "辦公桌", "Office Desk" },
        { "長凳", "Bench" },
        { "檔案櫃", "Filing Cabinet" },
        { "床", "Bed" },
        { "電視櫃", "TV Stand" },
        { "躺椅", "Recliner" }
    };

    private ProductListManager productListManager;
    private bool isInSearchMode = false;
    private string currentCategoryKey = "";
    private string currentCategoryDisplay = "";
    private string currentSearchQuery = "";

    void Start()
    {
        productListManager = FindObjectOfType<ProductListManager>();
        if (productListManager == null)
        {
            Debug.LogError("未找到 ProductListManager！");
        }
        
        SetupButtonEvents();
        SetupCategoryDropdown();

        if (searchDialog != null)
        {
            searchDialog.SetActive(false);
        }
    }

    private void SetupButtonEvents()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmSearch);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelSearch);

        if (searchButton != null)
            searchButton.onClick.AddListener(OpenSearchDialog);

        if (clearSearchButton != null)
            clearSearchButton.onClick.AddListener(ClearSearchResults);

        if (reloadButton != null)
            reloadButton.onClick.AddListener(ReloadCurrentPage);
    }

    private void SetupCategoryDropdown()
    {
        if (categoryDropdown == null) return;

        categoryDropdown.ClearOptions();
        List<string> categoryOptions = new List<string>(categoryMapping.Keys);
        categoryDropdown.AddOptions(categoryOptions);
        categoryDropdown.value = 0;
    }

    public void OpenSearchDialog()
    {
        if (searchDialog != null)
        {
            searchDialog.SetActive(true);
            Debug.Log("開啟搜尋對話框");
        }
    }

    public void OnCancelSearch()
    {
        if (searchDialog != null)
        {
            searchDialog.SetActive(false);
            Debug.Log("取消搜尋");
        }
    }

    public void OnConfirmSearch()
    {
        if (categoryDropdown == null) return;

        string selectedCategoryDisplay = categoryDropdown.options[categoryDropdown.value].text;
        if (!categoryMapping.TryGetValue(selectedCategoryDisplay, out string categoryKey))
        {
            Debug.LogError($"無效的類別選擇: {selectedCategoryDisplay}");
            return;
        }

        string searchQuery = (searchInputField != null) ? searchInputField.text : "";
        Debug.Log($"開始搜尋類別: {selectedCategoryDisplay} ({categoryKey}), 關鍵字: '{searchQuery}'");

        currentCategoryKey = categoryKey;
        currentCategoryDisplay = selectedCategoryDisplay;
        currentSearchQuery = searchQuery;
        isInSearchMode = true; // **修改：進入搜尋模式**

        if (searchDialog != null)
        {
            searchDialog.SetActive(false);
        }

        StartCoroutine(PerformSearch(categoryKey, selectedCategoryDisplay, searchQuery));
    }

    // **修改：簡化 ClearSearchResults 的邏輯**
    public void ClearSearchResults()
    {
        Debug.Log("清除搜尋結果，恢復所有商品列表");
        
        // 如果不在搜尋模式，則什麼都不做
        if (!isInSearchMode) return;
        
        currentCategoryKey = "";
        currentCategoryDisplay = "";
        currentSearchQuery = "";
        isInSearchMode = false;

        if (searchInputField != null)
        {
            searchInputField.text = "";
        }
        // **修改：直接呼叫 ProductListManager 的重置方法**
        productListManager.ResetToAllProducts();
    }

    // **修改：簡化 ReloadCurrentPage 的邏輯**
    public void ReloadCurrentPage()
    {
        Debug.Log("重新載入當前頁面");

        if (searchDialog != null && searchDialog.activeInHierarchy)
        {
            searchDialog.SetActive(false);
        }

        if (isInSearchMode)
        {
            // 如果在搜尋模式，重新執行搜尋
            Debug.Log($"重新載入搜尋類別: {currentCategoryDisplay}, 關鍵字: '{currentSearchQuery}");
            StartCoroutine(PerformSearch(currentCategoryKey, currentCategoryDisplay, currentSearchQuery));
        }
        else
        {
            // 如果不在搜尋模式，重置為所有商品
            Debug.Log("重新載入所有商品");
            productListManager.ResetToAllProducts();
        }
    }
    // **修改：重寫 PerformSearch，使其只負責獲取資料並傳遞**
    private IEnumerator PerformSearch(string categoryKey, string categoryDisplay, string searchQuery)
    {
        if (productListManager.loadingPanel != null)
            productListManager.loadingPanel.SetActive(true);

        string idSearchUrl = $"{apiBaseUrl}/3d_search.php?action=list";
        
        // ****** 修改 ******：重構 URL 組合邏輯，使其更清晰且能正確處理搜尋字串
        bool hasCategory = categoryKey.ToUpper() != "ALL";
        bool hasQuery = !string.IsNullOrEmpty(searchQuery);

        if (hasCategory)
        {
            idSearchUrl += $"&category={UnityWebRequest.EscapeURL(categoryKey)}";
        }
        if (hasQuery)
        {
            idSearchUrl += $"&query={UnityWebRequest.EscapeURL(searchQuery)}";
        }

        // 如果分類是 "全部" 且沒有搜尋文字，則直接重置列表
        if (!hasCategory && !hasQuery)
        {
            Debug.Log("執行 '全部' 類別且無關鍵字搜尋，顯示所有商品。");
            productListManager.ResetToAllProducts();
            // ResetToAllProducts 內部應該會處理 loadingPanel 的關閉
            yield break; // 結束協程
        }

        Debug.Log("請求 API URL: " + idSearchUrl);

        UnityWebRequest idRequest = UnityWebRequest.Get(idSearchUrl);
        yield return idRequest.SendWebRequest();

        if (idRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"搜尋失敗 (步驟一): {idRequest.error}");
            if (productListManager.loadingPanel != null)
                productListManager.loadingPanel.SetActive(false);
            yield break;
        }

        SearchIdResponse idResponse = JsonUtility.FromJson<SearchIdResponse>(idRequest.downloadHandler.text);

        if (!idResponse.success || idResponse.product_ids.Length == 0)
        {
            Debug.Log($"在類別 '{categoryDisplay}' 中未找到任何商品。");
            // **修改：呼叫 SetSearchResults 並傳入空列表**
            productListManager.SetSearchResults(new List<NewJSONProduct>(), categoryDisplay);
            yield break; // 直接結束協程，SetSearchResults 會處理後續 UI
        }

        // --- 第二步：請求詳細資料 ---
        string jsonIds = "[\"" + string.Join("\",\"", idResponse.product_ids) + "\"]";
        string detailsUrl = $"{apiBaseUrl}/3d_model_db.php?action=list&ids={UnityWebRequest.EscapeURL(jsonIds)}";
        UnityWebRequest detailsRequest = UnityWebRequest.Get(detailsUrl);
        yield return detailsRequest.SendWebRequest();

        if (detailsRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"搜尋失敗 (步驟二): {detailsRequest.error}");
            if (productListManager.loadingPanel != null)
                productListManager.loadingPanel.SetActive(false);
            yield break;
        }

        // --- 第三步：將最終結果交給 ProductListManager ---
        string productsJson = detailsRequest.downloadHandler.text;
        NewAPIResponse finalResponse = JsonHelper.FromNewJson(productsJson);

        if (finalResponse.success)
        {
            // **核心修改：呼叫 SetSearchResults，將搜尋結果和狀態傳遞過去**
            productListManager.SetSearchResults(new List<NewJSONProduct>(finalResponse.products), categoryDisplay);
        }
        else
        {
             Debug.LogError("獲取商品詳細資訊時 API 回應失敗。");
             // 即使失敗，也傳遞一個空列表以清空 UI
             productListManager.SetSearchResults(new List<NewJSONProduct>(), categoryDisplay);
        }
    }
}