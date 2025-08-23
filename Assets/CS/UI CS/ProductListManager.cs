using UnityEngine; 
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using ARFurniture;

#region 產品 JSON 資料結構定義

// 產品資料物件，用來存放解析後的產品資訊
[System.Serializable]
public class ProductData
{
    public string modelURL;           // 3D 模型檔案的位址
    public string productName;        // 產品名稱
    public float price;               // 價格
    public string url;                // 產品連結
    public string otherInfo;          // 產品描述資訊
    public Sprite productImage;       // 產品主要顯示圖片
    public List<Sprite> allSprites = new List<Sprite>();  // 所有圖片的 Sprite 列表
    public string sizeOptions;        // 尺寸資訊
    public bool from;                 // 區分來源用的旗標，UI1 預設為 false
    public string productId;          // 產品 ID
}

// **修改：新的 3D 模型 API 回應結構**
[System.Serializable]
public class NewAPIResponse
{
    public bool success;              // API 是否成功
    public string message;            // 回應訊息
    public int count;                 // 產品數量
    public NewJSONProduct[] products; // 產品陣列
}

// **修改：新的產品資料結構**
[System.Serializable]
public class NewJSONProduct
{
    public string id;                 // 產品 ID
    public string name;               // 產品名稱
    public float price;               // 價格
    public string main_type;          // 主要類型
    public string url;                // 產品連結
    public float? height;             // 高度（可為 null）
    public float? width;              // 寬度（可為 null）
    public float? depth;              // 深度（可為 null）
    public string description;        // 產品描述
    public string proxy_image_url;    // 代理圖片 URL
    public string proxy_model_url;    // 代理模型 URL
}

// JSON 輔助解析工具，用於處理 JSON 陣列資料
public static class JsonHelper
{
    // **修改：解析新的 API 回應格式**
    public static NewAPIResponse FromNewJson(string json)
    {
        return JsonUtility.FromJson<NewAPIResponse>(json);
    }

    // 保留原有的陣列解析方法以備用
    public static T[] FromJsonArray<T>(string json)
    {
        string newJson = "{ \"array\": " + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
        return wrapper.array;
    }

    [System.Serializable]
    private class Wrapper<T>
    {
        public T[] array;
    }
}

#endregion

public class ProductListManager : MonoBehaviour
{
    public static ProductListManager Instance { get; private set; }
    
    // **修改：更新 API 基礎 URL**
    private string apiBaseUrl = "http://140.127.114.38:5008/php";  // API 基礎 URL
    
    [Header("產品項目 Prefab")]
    public GameObject productItemPrefab;

    [Header("產品列表容器")]
    public Transform productContent;

    [Header("產品資訊面板")]
    public GameObject panelRoot;

    [Header("其他 UI 按鈕")]
    public Button listBtn;
    public GameObject listBtnImage;
    public bool activeSelf = true;
    public Sprite upSprite;
    public Sprite downSprite;

    public List<ProductData> allProducts;
    public ModelLoader1 modelLoader1;

    [Header("商品加載")]
    public GameObject loadingPanel;

    [Header("網格佈局設置")]
    [SerializeField] private Vector2 cellSize = new Vector2(300f, 350f);
    [SerializeField] private Vector2 spacing = new Vector2(20f, 20f);
    [SerializeField] private int paddingLeft = 20;
    [SerializeField] private int paddingRight = 20;
    [SerializeField] private int paddingTop = 20;
    [SerializeField] private int paddingBottom = 20;

    // **新增：分頁相關變數**
    [Header("分頁設置")]
    public int itemsPerPage = 20;           // 每頁顯示的商品數量
    public Button previousPageBtn;          // 上一頁按鈕
    public Button nextPageBtn;              // 下一頁按鈕
    public Text pageInfoText;               // 頁面資訊文字 (例如: "第 1 頁 / 共 36 頁")
    public Text itemCountText;              // 商品數量文字 (例如: "共 733 個商品")
    
    private int currentPage = 1;            // 當前頁數
    private int totalPages = 0;             // 總頁數
    private int totalItemCount = 0;         // 總商品數量
    public List<NewJSONProduct> allRawProducts = new List<NewJSONProduct>(); // 存儲所有原始商品資料
    private bool isLoadingPage = false;     // 防止重複載入

    void Start()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
        }
        listBtn.image.sprite = downSprite;
        listBtn.onClick.AddListener(OnClickToggleImage);
        
        // **新增：設置分頁按鈕事件**
        SetupPaginationButtons();
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        allProducts = new List<ProductData>();

        // **修改：使用新的 3D 模型 API 端點**
        StartCoroutine(DownloadProductJson());
    }

    // **新增：設置分頁按鈕**
    private void SetupPaginationButtons()
    {
        if (previousPageBtn != null)
        {
            previousPageBtn.onClick.AddListener(LoadPreviousPage);
        }
        
        if (nextPageBtn != null)
        {
            nextPageBtn.onClick.AddListener(LoadNextPage);
        }
        
        UpdatePaginationUI();
    }

    // **新增：載入上一頁**
    public void LoadPreviousPage()
    {
        if (isLoadingPage || currentPage <= 1) return;
        
        currentPage--;
        StartCoroutine(LoadCurrentPage());
    }

    // **新增：載入下一頁**
    public void LoadNextPage()
    {
        if (isLoadingPage || currentPage >= totalPages) return;
        
        currentPage++;
        StartCoroutine(LoadCurrentPage());
    }

    // **新增：跳轉到指定頁面**
    public void GoToPage(int pageNumber)
    {
        if (isLoadingPage || pageNumber < 1 || pageNumber > totalPages) return;
        
        currentPage = pageNumber;
        StartCoroutine(LoadCurrentPage());
    }

    // **新增：載入當前頁面的商品**
    private IEnumerator LoadCurrentPage()
    {
        if (isLoadingPage) yield break;
        
        isLoadingPage = true;
        
        // 顯示載入狀態
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
        }
        
        // 清空當前商品列表
        ClearCurrentProducts();
        
        // 計算當前頁面的商品範圍
        int startIndex = (currentPage - 1) * itemsPerPage;
        int endIndex = Mathf.Min(startIndex + itemsPerPage, allRawProducts.Count);
        
        Debug.Log($"載入第 {currentPage} 頁，商品範圍: {startIndex} - {endIndex}");
        
        // 處理當前頁面的商品
        int loadedCount = 0;
        for (int i = startIndex; i < endIndex; i++)
        {
            if (i >= allRawProducts.Count) break;
            
            NewJSONProduct jp = allRawProducts[i];
            ProductData pd = ConvertToProductData(jp);
            allProducts.Add(pd);
            
            // 下載圖片
            yield return StartCoroutine(DownloadImageForProduct(jp.proxy_image_url, pd));
            loadedCount++;
        }
        
        // 創建 UI
        CreateProductItems();
        
        // 更新分頁 UI
        UpdatePaginationUI();
        
        // 隱藏載入面板
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }
        
        isLoadingPage = false;
        
        Debug.Log($"第 {currentPage} 頁載入完成，載入了 {loadedCount} 個商品");
    }

    // **新增：將原始商品資料轉換為 ProductData**
    private ProductData ConvertToProductData(NewJSONProduct jp)
    {
        ProductData pd = new ProductData();
        pd.productName = jp.name;
        pd.price = jp.price;
        pd.url = jp.url;
        pd.otherInfo = jp.description ?? "";
        pd.productId = jp.id;
        
        // 組合尺寸資訊
        List<string> sizeComponents = new List<string>();
        if (jp.height.HasValue && jp.height.Value > 0)
            sizeComponents.Add($"高{jp.height.Value}");
        if (jp.width.HasValue && jp.width.Value > 0)
            sizeComponents.Add($"寬{jp.width.Value}");
        if (jp.depth.HasValue && jp.depth.Value > 0)
            sizeComponents.Add($"深{jp.depth.Value}");
        
        pd.sizeOptions = sizeComponents.Count > 0 ? string.Join(" x ", sizeComponents) + " (英吋)" : "尺寸資訊不完整";
        
        // 使用代理模型 URL
        if (!string.IsNullOrEmpty(jp.proxy_model_url))
        {
            pd.modelURL = jp.proxy_model_url;
        }
        
        pd.from = false;
        
        return pd;
    }

    // **新增：更新分頁 UI**
    private void UpdatePaginationUI()
    {
        // 更新按鈕狀態
        if (previousPageBtn != null)
        {
            previousPageBtn.interactable = currentPage > 1;
        }
        
        if (nextPageBtn != null)
        {
            nextPageBtn.interactable = currentPage < totalPages;
        }
        
        // 更新頁面資訊
        if (pageInfoText != null)
        {
            pageInfoText.text = $"第 {currentPage} 頁 / 共 {totalPages} 頁";
        }
        
        // 更新商品數量資訊
        if (itemCountText != null)
        {
            int currentPageItems = Mathf.Min(itemsPerPage, totalItemCount - (currentPage - 1) * itemsPerPage);
            itemCountText.text = $"顯示 {currentPageItems} 個商品 / 共 {totalItemCount} 個商品";
        }
    }

    // **新增：清空當前頁面的商品**
    private void ClearCurrentProducts()
    {
        // 清空商品列表
        allProducts.Clear();
        
        // 清空 UI
        if (productContent != null)
        {
            foreach (Transform child in productContent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    // **修改：解析 JSON 時不立即處理所有商品，只存儲原始資料**
    void ParseJsonProducts(string jsonText)
    {
        try
        {
            NewAPIResponse response = JsonHelper.FromNewJson(jsonText);
            
            if (!response.success)
            {
                Debug.LogError("API 回應失敗: " + response.message);
                if (loadingPanel != null)
                {
                    loadingPanel.SetActive(false);
                }
                return;
            }

            // **修改：只存儲原始資料，不立即處理**
            allRawProducts.Clear();
            allRawProducts.AddRange(response.products);
            
            totalItemCount = response.products.Length;
            totalPages = Mathf.CeilToInt((float)totalItemCount / itemsPerPage);
            currentPage = 1;
            
            Debug.Log($"成功載入 {totalItemCount} 個 3D 模型商品資料，共 {totalPages} 頁");
            
            // **修改：載入第一頁**
            StartCoroutine(LoadCurrentPage());
        }
        catch (System.Exception e)
        {
            Debug.LogError("解析 JSON 失敗: " + e.Message);
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(false);
            }
        }
    }

    // **修改：處理單一圖片格式**
    IEnumerator DownloadImageForProduct(string imageUrl, ProductData pd)
    {
        if (string.IsNullOrEmpty(imageUrl))
        {
            Debug.LogWarning($"產品 {pd.productName} 沒有圖片 URL");
            yield break;
        }

        UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            
            pd.productImage = sprite;
            pd.allSprites.Add(sprite);
        }
        else
        {
            Debug.LogWarning($"下載圖片失敗 - 產品: {pd.productName}, URL: {imageUrl}, 錯誤: {request.error}");
        }
    }

    // **修改：使用新的 3D 模型 API 端點**
    public IEnumerator DownloadProductJson()
    {
        string jsonUrl = $"{apiBaseUrl}/3d_model_db.php?action=list";
        Debug.Log($"正在從新的 3D 模型 API 載入商品資料: {jsonUrl}");
        
        UnityWebRequest www = UnityWebRequest.Get(jsonUrl);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"下載 3D 模型商品資料失敗: {www.error}");
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(false);
            }
        }
        else
        {
            string jsonText = www.downloadHandler.text;
            Debug.Log($"收到 3D 模型 API 回應: {jsonText.Substring(0, Mathf.Min(200, jsonText.Length))}...");
            ParseJsonProducts(jsonText);
        }
    }

    private void SetupGridLayout()
    {
        var existingVertical = productContent.GetComponent<VerticalLayoutGroup>();
        if (existingVertical != null)
        {
            DestroyImmediate(existingVertical);
        }

        var existingHorizontal = productContent.GetComponent<HorizontalLayoutGroup>();
        if (existingHorizontal != null)
        {
            DestroyImmediate(existingHorizontal);
        }

        var existingGrid = productContent.GetComponent<GridLayoutGroup>();
        if (existingGrid != null)
        {
            DestroyImmediate(existingGrid);
        }

        GridLayoutGroup gridLayout = productContent.gameObject.AddComponent<GridLayoutGroup>();
        
        gridLayout.cellSize = cellSize;
        gridLayout.spacing = spacing;
        gridLayout.padding = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 2;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
    }

    public void CreateProductItems()
    {
        SetupGridLayout();

        foreach (var pd in allProducts)
        {
            GameObject itemObj = Instantiate(productItemPrefab, productContent);
            
            RectTransform btnRect = itemObj.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(400f, 400f);

            var productImageTransform = itemObj.transform.Find("ProductImage")?.GetComponent<Image>();
            var nameTxt = itemObj.transform.Find("NameText")?.GetComponentInChildren<Text>();
            var priceTxt = itemObj.transform.Find("PriceText")?.GetComponentInChildren<TextMeshProUGUI>();
            var arBtn = itemObj.transform.Find("ViewARButton")?.GetComponent<Button>();
            var infoBtn = itemObj.transform.Find("InfoButton")?.GetComponent<Button>();
            
            if (productImageTransform != null)
            {
                Transform imgTransform = productImageTransform.transform.Find("Image");
                if (imgTransform != null)
                {
                    Image img = imgTransform.GetComponent<Image>();
                    if (img != null && pd.productImage != null)
                    {
                        img.sprite = pd.productImage;

                        float fixedSize = 400f; 
                        float spriteWidth = pd.productImage.rect.width;
                        float spriteHeight = pd.productImage.rect.height;
                        float calculatedWidth = fixedSize * (spriteWidth / spriteHeight);
                        float calculatedHeight = fixedSize * (spriteHeight / spriteWidth);

                        RectTransform imgRect = img.GetComponent<RectTransform>();
                        if (calculatedWidth > fixedSize)
                        {
                            imgRect.sizeDelta = new Vector2(fixedSize, calculatedHeight);
                        }
                        else
                        {
                            imgRect.sizeDelta = new Vector2(calculatedWidth, fixedSize);
                        }
                        imgRect.anchoredPosition = Vector2.zero;
                    }
                }
            }

            if (nameTxt != null)
                nameTxt.text = pd.productName;
            if (priceTxt != null)
                priceTxt.text = "$" + pd.price.ToString("F2");

            if (infoBtn != null)
            {
                ProductData capturedPd = pd;
                infoBtn.onClick.AddListener(() =>
                {
                    OnClickToggleImage();
                    InfoPanelController.Instance.ShowProductInfo(capturedPd);
                });
            }

            if (arBtn != null)
            {
                ProductData capturedPd = pd;
                arBtn.onClick.AddListener(() =>
                {
                    OnClickToggleImage();
                    modelLoader1.SetModelToLoad(capturedPd.modelURL, capturedPd);
                });
            }
        }
    }

    public void OnClickToggleImage()
    {
        Sprite currentSprite = listBtn.image.sprite;

        if (currentSprite == upSprite)
        {
            listBtn.image.sprite = downSprite;
            panelRoot.SetActive(false);
        }
        else
        {
            listBtn.image.sprite = upSprite;
            panelRoot.SetActive(true);
        }
    }

    public void OnClicklist(){
        listBtnImage.SetActive(!listBtnImage.activeSelf);
    }

    // **新增：提供給搜尋功能使用的方法**
    public void SetSearchResults(List<NewJSONProduct> searchResults, string categoryName)
    {
        allRawProducts.Clear();
        allRawProducts.AddRange(searchResults);
        
        totalItemCount = searchResults.Count;
        totalPages = Mathf.CeilToInt((float)totalItemCount / itemsPerPage);
        currentPage = 1;
        
        Debug.Log($"設置搜尋結果: {categoryName}，共 {totalItemCount} 個商品，{totalPages} 頁");
        
        StartCoroutine(LoadCurrentPage());
    }

    // **新增：重置到所有商品模式**
    public void ResetToAllProducts()
    {
        StartCoroutine(DownloadProductJson());
    }
}