﻿using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using GLTFast;
using UnityEngine.Networking;
using System.Linq;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;

using ARFurniture;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ARFurniture
{
    [RequireComponent(typeof(ARRaycastManager))]
    public class ModelLoader1 : MonoBehaviour
    {
        public Camera arCamera;  // AR Camera
        public ARRaycastManager arRaycastManager;
        // 模型下載網址（直接來自 JSON 的 model_url）
        public string githubModelURL;
        public TMP_Text textMeshPro;
        
        List<ARRaycastHit> hits = new List<ARRaycastHit>();

        [SerializeField] GameObject placementIndicator;
        [SerializeField] InputAction touchInput;

        // 已放置的模型與其對應的產品資料
        private List<GameObject> spawnedObjects = new List<GameObject>();
        private GameObject currentEditingObject = null;
        private Dictionary<GameObject, ProductData> spawnedModelInfo = new Dictionary<GameObject, ProductData>();

        // 編輯用元件 Prefab
        public GameObject forwardArrowPrefab;
        public GameObject leftRightArrowPrefab;
        public GameObject rotateRingPrefab;
        public Material editMaterial;

        public GameObject loadingPanel;

        public GameObject moveButtonPrefab;
        public GameObject rotateButtonPrefab;
        public GameObject measureButtonPrefab;
        public GameObject exitEditModeButtonPrefab;
        public GameObject deleteModelButtonPrefab;
        public GameObject endOperationButtonPrefab;
        public GameObject InfoButtonPrefab;

        public ModelConverter modelConverter; 
        public GameObject CustomizeButtonPrefab;
        public GameObject CustomizePanelPrefab;
        public GameObject cornerSpherePrefab;
        public Material LineRendererMat;
        public Material bgMaterial;

        // 編輯模式下產生的元件參考
        private GameObject currentForwardArrow = null;
        private GameObject currentLeftRightArrow = null;
        private GameObject currentRotateRing = null;
        private GameObject currentEditButtonPanel = null;
        private GameObject currentEditButtonPanelMove = null;
        private GameObject currentEditButtonPanelRotate = null;
        private GameObject currentEditButtonPanelSize = null;
        private GameObject currentEndOpButton = null;
        // private GameObject measurementTextContainer;
        private GameObject measurementBoxObj;
        private List<GameObject> measurementTextObjs = new List<GameObject>();

        public GameObject CancelPlacePanel;
        public Button CancelPlace;

        enum EditAction { None, MoveForwardBackward, MoveLeftRight, Rotate }
        private EditAction currentEditAction = EditAction.None;
        private Vector2 lastInputPosition;
        private Vector3 moveOffset = Vector3.zero;
        public float moveSensitivity = 0.001f;
        public float rotateSensitivity = 0.1f;
        public bool loading = false;

        // 目前要載入的產品資訊（由 UI1 傳入）
        private ProductData selectedProductData;

        private ModelCacheManager cacheManager;

        // 添加以下字段來設置輪廓效果
        [Header("輪廓設置")]
        public Color outlineColor = new Color(1f, 0.6f, 0f, 1f); // 橙色默認輪廓
        public float outlineWidth = 5f;

        private void Awake()
        {
            EnhancedTouchSupport.Enable();
            arRaycastManager = GetComponent<ARRaycastManager>();
            touchInput.performed += ctx => {
                Vector2 pos = ctx.ReadValue<Vector2>();
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    Debug.Log("觸碰到 UI 元件");
                    return;
                }
                if (currentEditingObject != null)
                {
                    Ray ray = arCamera.ScreenPointToRay(pos);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        if ((currentForwardArrow != null && hit.transform.IsChildOf(currentForwardArrow.transform)) ||
                            (currentLeftRightArrow != null && hit.transform.IsChildOf(currentLeftRightArrow.transform)) ||
                            (currentRotateRing != null && hit.transform.IsChildOf(currentRotateRing.transform)) ||
                            (currentEditButtonPanel != null && hit.transform.IsChildOf(currentEditButtonPanel.transform))||
                            (currentEditButtonPanelSize != null && hit.transform.IsChildOf(currentEditButtonPanelSize.transform)))
                        {
                            return;
                        }
                    }
                }
                Ray ray2 = arCamera.ScreenPointToRay(pos);
                if (Physics.Raycast(ray2, out RaycastHit hit2))
                {
                    GameObject hitModel = GetRootModel(hit2.transform.gameObject);
                    if (hitModel != null)
                    {
                        ToggleEditMode(hitModel);
                        return;
                    }
                }
                if (currentEditingObject == null && placementIndicator.activeInHierarchy)
                {
                    PlaceObject();
                }
            };

            placementIndicator.SetActive(false);
        }

        private void OnEnable() { touchInput.Enable(); }
        private void OnDisable() { touchInput.Disable(); }

        private void Start()
        {
            cacheManager = GetComponent<ModelCacheManager>();
            if (cacheManager == null)
            {
                cacheManager = gameObject.AddComponent<ModelCacheManager>();
            }
        }

        // 傳入模型網址與產品資料
        public void SetModelToLoad(string modelURL, ProductData pd)
        {
            githubModelURL = modelURL;
            selectedProductData = pd;

            Debug.Log($"[ModelLoader] 設定要載入的模型:");
            Debug.Log($"  模型 URL: {modelURL}");
            Debug.Log($"  產品名稱: {pd?.productName ?? "未知"}");
            Debug.Log($"  產品 ID: {pd?.productId ?? "未知"}");
            Debug.Log($"  尺寸資訊: {pd?.sizeOptions ?? "未知"}");
            
        }

        void Update()
        {
            // 1. AR Raycast 取得中心平面並顯示 placementIndicator
            List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();

            if (arRaycastManager.Raycast(new Vector2(Screen.width / 2, Screen.height / 2), raycastHits, TrackableType.PlaneWithinPolygon) 
                && currentEditButtonPanel == null && !string.IsNullOrEmpty(githubModelURL) && loading == false)
            {
                var hitPose = raycastHits[0].pose;
                placementIndicator.transform.SetPositionAndRotation(hitPose.position, hitPose.rotation);
                if (!placementIndicator.activeInHierarchy)
                    placementIndicator.SetActive(true);
            }
            else
            {
                placementIndicator.SetActive(false);
            }

            // 2. 處理取消放置面板
            if (!string.IsNullOrEmpty(githubModelURL))
            {
                CancelPlacePanel.SetActive(true);
                CancelPlace.onClick.RemoveAllListeners();
                CancelPlace.onClick.AddListener(() => { githubModelURL = ""; CancelPlacePanel.SetActive(false); });
            }

            // 3. 更新編輯控制面板位置（例如 Info、移動、旋轉按鈕面板）
            if (currentEditButtonPanel != null && currentEditingObject != null){
                UpdatePanelPosition(currentEditButtonPanel);
            }

            if (currentEditButtonPanelMove != null && currentEditingObject != null){
                UpdatePanelPosition(currentEditButtonPanelMove);
            }

            if (currentEditButtonPanelRotate != null && currentEditingObject != null){
                UpdatePanelPosition(currentEditButtonPanelRotate);
            }

            if (currentEditButtonPanelSize != null && currentEditingObject != null){
                UpdatePanelPosition(currentEditButtonPanelSize);
            }
            
            UpdateMeasurementTextOrientation();

            // 4. 處理一般點擊輸入：點擊非 UI 物件時切換編輯模式或放置模型
            Action<Vector2> processInput = (Vector2 screenPos) =>
            {
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    Debug.Log("UI Element was touched.");
                    return;
                }

                if (currentEditingObject != null)
                {
                    Ray ray = arCamera.ScreenPointToRay(screenPos);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        // 若點擊到任何編輯控制元件，則不執行切換
                        if ((currentForwardArrow != null && hit.transform.IsChildOf(currentForwardArrow.transform)) ||
                            (currentLeftRightArrow != null && hit.transform.IsChildOf(currentLeftRightArrow.transform)) ||
                            (currentRotateRing != null && hit.transform.IsChildOf(currentRotateRing.transform)))
                        {
                            return;
                        }
                    }
                }

                Ray ray2 = arCamera.ScreenPointToRay(screenPos);
                if (Physics.Raycast(ray2, out RaycastHit hit2))
                {
                    GameObject hitModel = GetRootModel(hit2.transform.gameObject);
                    if (hitModel != null)
                    {
                        ToggleEditMode(hitModel);
                        return;
                    }
                }

                if (currentEditingObject == null && placementIndicator.activeInHierarchy)
                {
                    PlaceObject();
                }
            };

    #if UNITY_EDITOR
            // 處理滑鼠點擊輸入（Editor 模式下）
            if (Input.GetMouseButtonDown(0))
            {
                processInput(Input.mousePosition);
            }

            // 當處於編輯模式且已點選移動控制元件時（非旋轉），使用射線與水平面取得新位置
            if (currentEditingObject != null)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Ray ray = arCamera.ScreenPointToRay(Input.mousePosition);
                    // 檢查是否點擊到移動控制元件
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        if ((currentForwardArrow != null && hit.transform.IsChildOf(currentForwardArrow.transform)) ||
                            (currentLeftRightArrow != null && hit.transform.IsChildOf(currentLeftRightArrow.transform)))
                        {
                            currentEditAction = (currentForwardArrow != null && hit.transform.IsChildOf(currentForwardArrow.transform)) 
                                                ? EditAction.MoveForwardBackward : EditAction.MoveLeftRight;
                            // 使用射線與水平面計算偏移量
                            Plane movePlane = new Plane(Vector3.up, currentEditingObject.transform.position);
                            if (movePlane.Raycast(ray, out float enter))
                            {
                                Vector3 hitPoint = ray.GetPoint(enter);
                                moveOffset = currentEditingObject.transform.position - hitPoint;
                            }
                        }
                        else if (currentRotateRing != null && hit.transform.IsChildOf(currentRotateRing.transform))
                        {
                            currentEditAction = EditAction.Rotate;
                            lastInputPosition = Input.mousePosition;
                        }
                    }
                }

                // 持續拖曳更新
                if (Input.GetMouseButton(0) && currentEditAction != EditAction.None)
                {
                    if (currentEditAction == EditAction.MoveForwardBackward || currentEditAction == EditAction.MoveLeftRight)
                    {
                        // 使用射線與水平面更新物件位置
                        Ray ray = arCamera.ScreenPointToRay(Input.mousePosition);
                        Plane movePlane = new Plane(Vector3.up, currentEditingObject.transform.position);
                        if (movePlane.Raycast(ray, out float enter))
                        {
                            Vector3 hitPoint = ray.GetPoint(enter);
                            currentEditingObject.transform.position = hitPoint + moveOffset;
                        }
                    }
                    else if (currentEditAction == EditAction.Rotate)
                    {
                        // 旋轉方式維持原本拖動旋轉環Prefab的方式
                        Vector2 currentPos = Input.mousePosition;
                        Vector2 delta = currentPos - lastInputPosition;
                        lastInputPosition = currentPos;
                        float rotationAmount = delta.x * rotateSensitivity;
                        currentEditingObject.transform.Rotate(Vector3.up, rotationAmount, Space.World);
                    }
                }

                if (Input.GetMouseButtonUp(0))
                {
                    currentEditAction = EditAction.None;
                }
            }
    #endif

    #if UNITY_IOS || UNITY_ANDROID
            // 手機版處理觸控輸入
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == UnityEngine.TouchPhase.Began)
            {
                UnityEngine.Touch touch = Input.GetTouch(0);
                PointerEventData pointerData = new PointerEventData(EventSystem.current);
                pointerData.position = touch.position;
                List<RaycastResult> uiResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, uiResults);
                if (uiResults.Count > 0)
                {
                    Debug.Log("We hit a UI Element");
                    return;
                }
                processInput(touch.position);
            }

            // 手機版編輯模式下：單指拖曳移動
            if (currentEditingObject != null)
            {
                if (Input.touchCount == 1)
                {
                    UnityEngine.Touch touch = Input.GetTouch(0);
                    if (touch.phase == UnityEngine.TouchPhase.Began)
                    {
                        Ray ray = arCamera.ScreenPointToRay(touch.position);
                        if (Physics.Raycast(ray, out RaycastHit hit))
                        {
                            if (currentForwardArrow != null && hit.transform.IsChildOf(currentForwardArrow.transform))
                            {
                                currentEditAction = EditAction.MoveForwardBackward;
                                Plane plane = new Plane(Vector3.up, currentEditingObject.transform.position);
                                if (plane.Raycast(ray, out float enter))
                                {
                                    Vector3 hitPoint = ray.GetPoint(enter);
                                    moveOffset = currentEditingObject.transform.position - hitPoint;
                                }
                            }
                            else if (currentLeftRightArrow != null && hit.transform.IsChildOf(currentLeftRightArrow.transform))
                            {
                                currentEditAction = EditAction.MoveLeftRight;
                                Plane plane = new Plane(Vector3.up, currentEditingObject.transform.position);
                                if (plane.Raycast(ray, out float enter))
                                {
                                    Vector3 hitPoint = ray.GetPoint(enter);
                                    moveOffset = currentEditingObject.transform.position - hitPoint;
                                }
                            }
                            else if (currentRotateRing != null && hit.transform.IsChildOf(currentRotateRing.transform))
                            {
                                currentEditAction = EditAction.Rotate;
                                lastInputPosition = touch.position;
                            }
                        }
                    }
                    else if (touch.phase == UnityEngine.TouchPhase.Moved && currentEditAction != EditAction.None)
                    {
                        if (currentEditAction == EditAction.MoveForwardBackward || currentEditAction == EditAction.MoveLeftRight)
                        {
                            Ray ray = arCamera.ScreenPointToRay(touch.position);
                            Plane plane = new Plane(Vector3.up, currentEditingObject.transform.position);
                            if (plane.Raycast(ray, out float enter))
                            {
                                Vector3 hitPoint = ray.GetPoint(enter);
                                currentEditingObject.transform.position = hitPoint + moveOffset;
                            }
                        }
                        else if (currentEditAction == EditAction.Rotate)
                        {
                            Vector2 currentPos = touch.position;
                            Vector2 delta = currentPos - lastInputPosition;
                            lastInputPosition = currentPos;
                            float rotationAmount = delta.x * rotateSensitivity;
                            currentEditingObject.transform.Rotate(Vector3.up, rotationAmount, Space.World);
                        }
                    }
                    else if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled)
                    {
                        currentEditAction = EditAction.None;
                    }
                }
            }
    #endif
        }
        void UpdatePanelPosition(GameObject panel)
        {
            Bounds modelBounds = GetModelBounds(currentEditingObject);
            float offset = 0.01f; // 稍微浮動在模型表面上
            Vector3 topPos = new Vector3(modelBounds.center.x, modelBounds.max.y + offset, modelBounds.center.z);
            panel.transform.position = topPos;
            panel.transform.LookAt(arCamera.transform.position);
            panel.transform.Rotate(0, 180f, 0);
        }
        private void UpdateMeasurementTextOrientation()
        {
            foreach (GameObject go in measurementTextObjs)
            {
                if (go.GetComponent<TextMesh>() != null)
                {
                    Vector3 direction = arCamera.transform.position - go.transform.position;
                    direction.y = 0;
                    if (direction != Vector3.zero){
                        go.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180f, 0);
                    }
                }
            }
        }



        IEnumerator AdjustModelPosition(GameObject parentObject, Vector3 desiredDimensions)
        {
            yield return new WaitForEndOfFrame();

            Quaternion originalRotation = parentObject.transform.rotation;
            parentObject.transform.rotation = Quaternion.identity;

            Bounds originalBounds = GetStableBounds(parentObject);
            Debug.Log($"原始 Bounds: {originalBounds}");
            float groundY;
            float yOffset;
            
            // if (originalBounds.size.x <= 0.001f || originalBounds.size.y <= 0.001f || originalBounds.size.z <= 0.001f)
            // {
            //     Debug.LogWarning("原始Bounds尺寸過小，延遲重試");
            //     yield return new WaitForSeconds(0.2f);
            //     originalBounds = GetStableBounds(parentObject);
            // }
            // Debug.Log($"穩定後的原始 Bounds: {originalBounds}");

            if (desiredDimensions.x != 0 && desiredDimensions.y != 0 && desiredDimensions.z != 0)
            {

                // 正確的縮放因子計算，確保座標軸與尺寸對應一致
                // X軸對應寬度(desiredDimensions.x)，Z軸對應深度(desiredDimensions.z)，Y軸對應高度(desiredDimensions.y)
                float scaleFactorX = desiredDimensions.x / originalBounds.size.x; // 寬度對應X軸
                float scaleFactorY = desiredDimensions.y / originalBounds.size.y; // 高度對應Y軸
                float scaleFactorZ = desiredDimensions.z / originalBounds.size.z; // 深度對應Z軸

                // 三個軸同時套用各自的縮放
                parentObject.transform.localScale = new Vector3(scaleFactorX, scaleFactorY, scaleFactorZ);
                // 三個軸套用各自的高度縮放
                // parentObject.transform.localScale = (Vector3.one * scaleFactorY);

                parentObject.transform.rotation = originalRotation;
                Quaternion tempRot = parentObject.transform.rotation;
                parentObject.transform.rotation = Quaternion.identity;
                Bounds scaledBounds = GetModelBounds(parentObject);
                parentObject.transform.rotation = tempRot;

                Debug.Log($"縮放後 Bounds: {scaledBounds}");
                Debug.Log($"目標尺寸: 寬={desiredDimensions.x * 100}公分, 深={desiredDimensions.z * 100}公分, 高={desiredDimensions.y * 100}公分");
                Debug.Log($"實際尺寸: 寬={scaledBounds.size.x * 100}公分, 深={scaledBounds.size.z * 100}公分, 高={scaledBounds.size.y * 100}公分");

                groundY = placementIndicator.transform.position.y;
                yOffset = groundY - scaledBounds.min.y;
            }
            else if (desiredDimensions == Vector3.zero)
            {
                Debug.Log("目標尺寸為零，不進行縮放");
                groundY = placementIndicator.transform.position.y;
                yOffset = groundY - originalBounds.min.y;
            }
            else
            {
                float scaleFactor = 1.0f;
                bool foundScale = false;
                // 優先順序：z -> y -> x，找到第一個不為0的軸計算縮放因子
                if (desiredDimensions.z != 0 && !foundScale)
                {
                    scaleFactor = desiredDimensions.z / originalBounds.size.z;
                    foundScale = true;
                    Debug.Log($"使用X軸縮放因子: {scaleFactor}");
                }
                if (desiredDimensions.y != 0 && !foundScale)
                {
                    scaleFactor = desiredDimensions.y / originalBounds.size.y;
                    foundScale = true;
                    Debug.Log($"使用Y軸縮放因子: {scaleFactor}");
                }
                if (desiredDimensions.x != 0 && !foundScale)
                {
                    scaleFactor = desiredDimensions.x / originalBounds.size.x;
                    foundScale = true;
                    Debug.Log($"使用Z軸縮放因子: {scaleFactor}");
                }

                // 統一縮放三個軸，保持模型比例
                parentObject.transform.localScale = Vector3.one * scaleFactor;

                parentObject.transform.rotation = originalRotation;
                Quaternion tempRot = parentObject.transform.rotation;
                parentObject.transform.rotation = Quaternion.identity;
                Bounds scaledBounds = GetModelBounds(parentObject);
                parentObject.transform.rotation = tempRot;

                Debug.Log($"等比例縮放後 Bounds: {scaledBounds}");
                Debug.Log($"縮放因子: {scaleFactor}");
                Debug.Log($"實際尺寸: 寬={scaledBounds.size.x * 100}公分, 深={scaledBounds.size.z * 100}公分, 高={scaledBounds.size.y * 100}公分");

                groundY = placementIndicator.transform.position.y;
                yOffset = groundY - scaledBounds.min.y;
            }
            
            parentObject.transform.position += new Vector3(0, yOffset, 0);
            Debug.Log($"模型位置調整至: {parentObject.transform.position}");
            
            parentObject.transform.LookAt(new Vector3(arCamera.transform.position.x, parentObject.transform.position.y, arCamera.transform.position.z));
        }
        Bounds GetModelBounds(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            bool firstValid = true;
            Bounds bounds = new Bounds();
            foreach (Renderer r in renderers)
            {
                // 如果 measurementBoxObj 存在，且該 Renderer 的 GameObject 為 measurementBoxObj 或其子物件，就跳過
                if (measurementBoxObj != null && r.gameObject.transform.IsChildOf(measurementBoxObj.transform))
                    continue;

                if (firstValid)
                {
                    bounds = r.bounds;
                    firstValid = false;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            if (firstValid)
            {
                return new Bounds(obj.transform.position, Vector3.zero);
            }
            return bounds;
        }


        Bounds GetStableBounds(GameObject obj)
        {
            Bounds bounds1 = GetModelBounds(obj);
            
            // **多次採樣確保Bounds穩定**
            for (int i = 0; i < 3; i++)
            {
                Bounds bounds2 = GetModelBounds(obj);
                
                // **檢查Bounds是否穩定（差異小於閾值）**
                if (Vector3.Distance(bounds1.size, bounds2.size) < 0.001f && 
                    Vector3.Distance(bounds1.center, bounds2.center) < 0.001f)
                {
                    return bounds2;
                }
                bounds1 = bounds2;
            }
            
            Debug.LogWarning("Bounds未能穩定，使用最後一次計算結果");
            return bounds1;
        }


        // 解析尺寸字串，格式如 "46深 x 61寬 x 122高 公分"
        // 解析尺寸字串，支援多種格式
Vector3 ParseSizeString(string sizeStr)
{
    if (string.IsNullOrEmpty(sizeStr))
    {
        Debug.LogError("尺寸字串為空");
        return Vector3.zero;
    }

    Debug.Log($"正在解析尺寸字串: {sizeStr}");

    // 清理字串，移除多餘的括號和空格
    string cleanStr = sizeStr.Replace("(", "").Replace(")", "").Replace("cm", "").Replace("公分", "").Trim();

    float depth = 0f, width = 0f, height = 0f;

    // **格式1：標準格式 "46深 x 61寬 x 122高"**
    if (cleanStr.Contains("x") || cleanStr.Contains("X"))
    {
        string[] parts = cleanStr.Split(new char[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length >= 3)
        {
            // 解析每個部分
            foreach (string part in parts)
            {
                string trimmedPart = part.Trim();
                
                if (trimmedPart.Contains("深"))
                {
                    string depthStr = Regex.Match(trimmedPart, @"\d+").Value;
                    float.TryParse(depthStr, out depth);
                }
                else if (trimmedPart.Contains("寬"))
                {
                    string widthStr = Regex.Match(trimmedPart, @"\d+").Value;
                    float.TryParse(widthStr, out width);
                }
                else if (trimmedPart.Contains("高"))
                {
                    string heightStr = Regex.Match(trimmedPart, @"\d+").Value;
                    float.TryParse(heightStr, out height);
                }
            }
            
            Debug.Log($"標準格式解析結果 - 深度: {depth}, 寬度: {width}, 高度: {height}");
        }
        else
        {
            Debug.Log($"標準格式解析失敗，部分數量不足: {parts.Length}");
            return Vector3.one;
        }
    }
    // **格式2：單一尺寸格式 "高37" 或類似**
    else
    {
        // 檢查包含的尺寸類型
        if (cleanStr.Contains("高"))
        {
            string heightStr = Regex.Match(cleanStr, @"\d+").Value;
            if (float.TryParse(heightStr, out height))
            {
                Debug.Log($"單一高度格式解析結果 - 高度: {height}");
                // 只有高度時，其他尺寸設為0，稍後會使用等比例縮放
                depth = 0f;
                width = 0f;
            }
        }
        else if (cleanStr.Contains("寬"))
        {
            string widthStr = Regex.Match(cleanStr, @"\d+").Value;
            if (float.TryParse(widthStr, out width))
            {
                Debug.Log($"單一寬度格式解析結果 - 寬度: {width}");
                depth = 0f;
                height = 0f;
            }
        }
        else if (cleanStr.Contains("深"))
        {
            string depthStr = Regex.Match(cleanStr, @"\d+").Value;
            if (float.TryParse(depthStr, out depth))
            {
                Debug.Log($"單一深度格式解析結果 - 深度: {depth}");
                width = 0f;
                height = 0f;
            }
        }
        // **格式3：純數字格式 "37"**
        else
        {
            string numberStr = Regex.Match(cleanStr, @"\d+").Value;
            if (float.TryParse(numberStr, out float dimension))
            {
                Debug.Log($"純數字格式解析結果，假設為高度: {dimension}");
                // 假設純數字為高度
                height = dimension;
                depth = 0f;
                width = 0f;
            }
            else
            {
                Debug.Log($"無法解析尺寸字串: {sizeStr}");
                return Vector3.zero;
            }
        }
    }

    // **轉換：座標軸對應關係**
    // Unity座標系統：X=寬度, Y=高度, Z=深度
    // 輸入格式：深 x 寬 x 高 (公分)
    // 轉換：公分轉為公尺（除以100）
    Vector3 result = new Vector3(width / 100f, height / 100f, depth / 100f);
    
    Debug.Log($"最終解析結果 - X(寬度): {result.x}m, Y(高度): {result.y}m, Z(深度): {result.z}m");
    
    return result;
}

        // 使用產品的尺寸資訊來取得目標尺寸
        private Vector3 GetDesiredDimensions()
        {
            if (selectedProductData == null || string.IsNullOrEmpty(selectedProductData.sizeOptions))
            {
                Debug.Log("未設定產品尺寸資訊");
                return Vector3.zero;
            }
            return ParseSizeString(selectedProductData.sizeOptions);
        }

        async void PlaceObject()
        {
            if (currentEditingObject != null) return;
            if (!placementIndicator.activeInHierarchy) return;
            if (string.IsNullOrEmpty(githubModelURL)) return;
            if (loadingPanel != null)
            {
                loading = true;
                loadingPanel.SetActive(true);
            }

            // 創建一個不包含圖片資訊的臨時 ProductData 物件
            var tempProductData = new ProductData
            {
                modelURL = selectedProductData.modelURL,
                productName = selectedProductData.productName,
                price = selectedProductData.price,
                url = selectedProductData.url,
                otherInfo = selectedProductData.otherInfo,
                sizeOptions = selectedProductData.sizeOptions,
                from = selectedProductData.from
            };

            await LoadModelFromUrl(githubModelURL, JsonConvert.SerializeObject(tempProductData));
            if (loadingPanel != null)
            {
                loading = false;
                loadingPanel.SetActive(false);
            }
        }
        void EnableModelShadows(GameObject model)
        {
            // 遍歷模型下所有 Renderer，並啟用陰影
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        void DisableModelShadows(GameObject model)
        {
            // 遍歷模型下所有 Renderer，並禁用陰影
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void AddPhysicsProperties(GameObject obj)
        {
            // 檢查是否已存在 Rigidbody
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = obj.AddComponent<Rigidbody>();
            }
            rb.ResetCenterOfMass();
            rb.mass = 0.1f;          // 設定質量，可依需求調整
            rb.useGravity = true;    // 開啟重力
            rb.isKinematic = false;  // 非運動學（可受物理運算影響）
        }

        GameObject GetRootModel(GameObject obj)
        {
            for (int i = spawnedObjects.Count - 1; i >= 0; i--)
            {
                if (spawnedObjects[i] == null)
                    spawnedObjects.RemoveAt(i);
            }
            foreach (var model in spawnedObjects)
            {
                if (obj.transform.IsChildOf(model.transform))
                {
                    return model;
                }
            }
            return null;
        }

        void ToggleEditMode(GameObject model)
        {
            // 若點擊的模型已經是當前編輯中的模型，就不做任何處理，避免不小心退出編輯模式
            if (currentEditingObject == model)
            {
                Debug.Log("已在編輯模式中，忽略重複點擊");
                return;
            }

            // 如果目前已有其他模型在編輯模式，先退出該模式
            if (currentEditingObject != null)
            {
                ExitEditMode(currentEditingObject);
            }
            EnterEditMode(model);
        }

        // 進入編輯模式：更改材質並加入編輯控制元件
        void EnterEditMode(GameObject model)
        {
            currentEditingObject = model;
            Debug.Log("進入編輯模式: " + model.name);
            // 關閉模型 Collider 避免重複點擊
            Collider[] colliders = model.GetComponentsInChildren<Collider>();
            foreach (var c in colliders)
            {
                c.enabled = false;
            }

            // 在模型的geometry0上添加輪廓效果
            Transform geometryTransform = FindGeometry0Transform(model);
            if (geometryTransform != null)
            {
                // 添加Outline組件
                Outline outline = geometryTransform.gameObject.AddComponent<Outline>();
                outline.OutlineMode = Outline.Mode.OutlineAll;
                outline.OutlineColor = outlineColor;
                outline.OutlineWidth = outlineWidth;
                Debug.Log($"已在 {geometryTransform.name} 上添加輪廓效果");
            }
            else
            {
                Debug.LogWarning("未找到Geometry0物件，將在整個模型上添加輪廓效果");
                // 在整個模型上添加輪廓效果
                Outline outline = model.AddComponent<Outline>();
                outline.OutlineMode = Outline.Mode.OutlineAll;
                outline.OutlineColor = outlineColor;
                outline.OutlineWidth = outlineWidth;
            }

            // 取得模型 Bounds 頂部位置
            Bounds modelBounds = GetModelBounds(model);
            Vector3 topCenter = new Vector3(modelBounds.center.x, modelBounds.max.y, modelBounds.center.z);

            // 建立編輯用的按鈕面板
            currentEditButtonPanel = new GameObject("EditButtonPanel");
            Canvas canvas = currentEditButtonPanel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = arCamera;
            currentEditButtonPanel.AddComponent<CanvasScaler>();
            currentEditButtonPanel.AddComponent<GraphicRaycaster>();

            RectTransform panelRect = currentEditButtonPanel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(0.6f, 0.1f); // 縮小面板尺寸
            currentEditButtonPanel.transform.localScale = Vector3.one;

            // 直接放在模型頂部，不額外上移
            currentEditButtonPanel.transform.position = topCenter + Vector3.up; // 僅微小偏移
            currentEditButtonPanel.transform.LookAt(arCamera.transform);
            currentEditButtonPanel.transform.Rotate(0, 180f, 0);

            HorizontalLayoutGroup layout = currentEditButtonPanel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 0.05f; // 減小按鈕間距
            layout.childAlignment = TextAnchor.MiddleCenter;

            // 以下繼續生成各種按鈕（Info、移動、旋轉、自訂、退出、刪除）
            if (InfoButtonPrefab != null)
            {
                GameObject InfoButton = Instantiate(InfoButtonPrefab, currentEditButtonPanel.transform);
                Button btn = InfoButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => {
                        Debug.Log("Info 按鈕被點擊");
                        ProductData pd = ParseProductInfo(model);
                        if (pd == null)
                        {
                            Debug.LogWarning("解析產品資訊失敗");
                            return;
                        }
                        InfoPanelController.Instance.ShowProductInfo(pd);
                    });
                }
            }
            if (moveButtonPrefab != null)
            {
                GameObject moveButton = Instantiate(moveButtonPrefab, currentEditButtonPanel.transform);
                Button btn = moveButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => { Debug.Log("移動按鈕被點擊"); StartMoveMode(model); });
                }
            }
            if (rotateButtonPrefab != null)
            {
                GameObject rotateButton = Instantiate(rotateButtonPrefab, currentEditButtonPanel.transform);
                Button btn = rotateButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => { Debug.Log("旋轉按鈕被點擊"); StartRotateMode(model); });
                }
            }
            if (measureButtonPrefab != null)
            {
                GameObject scaleButton = Instantiate(measureButtonPrefab, currentEditButtonPanel.transform);
                Button btn = scaleButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => { Debug.Log("尺寸按鈕被點擊"); ShowMeasurementBox(model);; });
                }
            }
            if (spawnedModelInfo.TryGetValue(model, out ProductData pd) && pd.from == false)
            {
                if (CustomizeButtonPrefab != null)
                {
                    GameObject CustomizeButton = Instantiate(CustomizeButtonPrefab, currentEditButtonPanel.transform);
                    Button btn = CustomizeButton.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.AddListener(() => { Debug.Log($"自訂化按鈕被點擊 {model.name}"); StartCustomize(model); });
                    }
                }
            }
            
            if (exitEditModeButtonPrefab != null)
            {
                GameObject exitButton = Instantiate(exitEditModeButtonPrefab, currentEditButtonPanel.transform);
                Button btn = exitButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => { Debug.Log("退出編輯模式按鈕被點擊"); ExitEditMode(model); });
                }
            }
            if (deleteModelButtonPrefab != null)
            {
                GameObject deleteButton = Instantiate(deleteModelButtonPrefab, currentEditButtonPanel.transform);
                Button btn = deleteButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => { Debug.Log("刪除模型按鈕被點擊"); DeleteModel(model); });
                }
            }
        }

        Dictionary<GameObject, Material[][]> originalMaterials = new Dictionary<GameObject, Material[][]>();
        private void ShowMeasurementBox(GameObject model)
        {
            if (currentEditButtonPanel != null)
                currentEditButtonPanel.SetActive(false);
            
            // 清除先前的測量框 (若有)
            ClearMeasurementBox();

            // 尋找 geometry_0
            Transform geometryTransform = model.transform.Find("world/geometry_0");
            if (geometryTransform == null) return;

            GameObject geometryObj = geometryTransform.gameObject;

            // 計算 geometry_0 的世界 Bounds
            Bounds b = GetModelBounds(geometryObj);

            // 在 geometry_0 上暫時加一個 BoxCollider (以便獲取 8 個頂點)
            BoxCollider tempBC = geometryObj.AddComponent<BoxCollider>();

            // 先將世界中心與大小轉成 geometry_0 的本地座標
            Vector3 worldCenter = b.center;
            Vector3 localCenter = geometryObj.transform.InverseTransformPoint(worldCenter);

            // 同理大小也要用 InverseTransformVector
            Vector3 worldSize = b.size;
            Vector3 localSize = geometryObj.transform.InverseTransformVector(worldSize);

            tempBC.center = localCenter;
            // tempBC.size   = localSize;  // 使 BoxCollider 與模型對齊

            // 取得 BoxCollider 的 8 個頂點 (世界座標)
            Vector3[] corners = GetBoxColliderWorldCorners(tempBC);

            // 建立一個空物件 measurementBoxObj，用來放置繪製立方體線條
            measurementBoxObj = new GameObject("MeasurementBox");
            measurementBoxObj.transform.SetParent(model.transform, false);

            // 計算模型的平均尺寸，用於確定線條和標籤的合適大小
            float avgModelSize = (b.size.x + b.size.y + b.size.z) / 3f;
            // 打印模型的實際尺寸，用於調試
            Debug.Log($"模型尺寸 X: {b.size.x*100}公分, Y: {b.size.y*100}公分, Z: {b.size.z*100}公分, 平均: {avgModelSize*100}公分");
            
            // 限制 avgModelSize 的最大值，防止線條過粗
            avgModelSize = Mathf.Min(avgModelSize, 1.0f);
            
            // 線條粗度係數 - 較小的模型需要較粗的線條(相對比例)，但有上限
            float lineWidthFactor = Mathf.Clamp(avgModelSize * 0.005f, 0.003f, 0.008f);

            // 建立 LineRenderer 並設定為局部座標
            LineRenderer lr = measurementBoxObj.AddComponent<LineRenderer>();
            
            // 設置線條的渲染優先級
            Material lineMat = new Material(LineRendererMat);
            lineMat.renderQueue = 2800; // 降低線條的渲染優先級，讓它在背景後方
            lr.material = lineMat;
            
            // 根據模型尺寸設定合適的線條粗度，但設有最大值
            lr.startWidth = lineWidthFactor;
            lr.endWidth = lineWidthFactor;
            lr.loop = false;
            lr.useWorldSpace = false;  // 改為局部座標模式

            // 取得 BoxCollider 的 8 個頂點（世界座標）
            Vector3[] cornersWorld = GetBoxColliderWorldCorners(tempBC);

            // 若需要只連接特定邊，例如 (0,1)、(1,2)、(2,3)、(3,0) 和 (3,7)
            Vector3[] worldEdges = BuildCubeEdges(cornersWorld);

            // 將世界座標轉換成 measurementBoxObj 的局部座標
            Vector3[] localEdges = new Vector3[worldEdges.Length];
            for (int i = 0; i < worldEdges.Length; i++)
            {
                localEdges[i] = measurementBoxObj.transform.InverseTransformPoint(worldEdges[i]);
            }

            lr.positionCount = localEdges.Length;
            lr.SetPositions(localEdges);

            // 計算各邊的線段長度，並在中點顯示
            // 先取得邊的清單(對應 corners index)
            var edges = new (int start, int end)[]{
                // 底面 
                (2,3), 
                (3,0),   
                // 垂直連線
                (3,7)
            };
            
            // 計算角球大小
            float cornerSphereSize = Mathf.Clamp(avgModelSize * 0.03f, 0.024f, 0.03f);
            CreateCornerSpheres(geometryObj, corners, cornerSphereSize);
            
            // 顯示每條線段長度
            // measurementTextObjs.Clear();
            for(int i=0; i<edges.Length; i++)
            {
                int s = edges[i].start;
                int e = edges[i].end;
                float dist = Vector3.Distance(corners[s], corners[e]); 
                // 中點
                Vector3 mid = (corners[s] + corners[e])*0.5f;
                
                // 調整底部尺寸標籤的位置 (寬度和深度) - 將標籤上移
                if (i < 2) // 底部的寬和深標籤
                {
                    mid.y += 0.08f; // 增加上移距離到0.08，確保不會陷入地面
                }
                
                // 生成文字，傳入模型平均尺寸用於計算合適的文字大小
                CreateEdgeLengthText(geometryObj, dist, mid, avgModelSize);
            }

            Destroy(tempBC);

            if (endOperationButtonPrefab != null)
            {
                Bounds modelBounds = GetModelBounds(model);
                Vector3 topCenter = new Vector3(modelBounds.center.x, modelBounds.max.y, modelBounds.center.z);
                currentEditButtonPanelSize = new GameObject("EditButtonPanel_Size");
                Canvas canvas = currentEditButtonPanelSize.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = arCamera;
                currentEditButtonPanelSize.AddComponent<CanvasScaler>();
                currentEditButtonPanelSize.AddComponent<GraphicRaycaster>();

                RectTransform panelRect = currentEditButtonPanelSize.GetComponent<RectTransform>();
                panelRect.sizeDelta = new Vector2(0.5f, 0.15f); // 縮小面板尺寸
                currentEditButtonPanelSize.transform.localScale = Vector3.one;

                // 直接放在模型頂部，不額外上移
                currentEditButtonPanelSize.transform.position = topCenter + Vector3.up*0.01f; // 僅微小偏移
                currentEditButtonPanelSize.transform.LookAt(arCamera.transform);
                currentEditButtonPanelSize.transform.Rotate(0, 180f, 0);

                HorizontalLayoutGroup layout = currentEditButtonPanelSize.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 0.01f; // 減小按鈕間距
                layout.childAlignment = TextAnchor.MiddleCenter;

                currentEndOpButton = Instantiate(endOperationButtonPrefab, currentEditButtonPanelSize.transform);
                // 移除多餘的位置設置，讓按鈕使用布局系統的排列
                currentEndOpButton.GetComponent<Button>().onClick.AddListener(() => { EndOperationMode(); });
            }
        }


        private Vector3[] GetBoxColliderWorldCorners(BoxCollider bc)
        {
            Vector3[] corners = new Vector3[8];
            // 半徑
            Vector3 half = bc.size * 0.5f;
            // 本地 8 點
            Vector3 center = bc.center;
            Vector3 p0 = center + new Vector3(-half.x, -half.y, -half.z);
            Vector3 p1 = center + new Vector3(half.x, -half.y, -half.z);
            Vector3 p2 = center + new Vector3(half.x, -half.y, half.z);
            Vector3 p3 = center + new Vector3(-half.x, -half.y, half.z);
            Vector3 p4 = center + new Vector3(-half.x, half.y, -half.z);
            Vector3 p5 = center + new Vector3(half.x, half.y, -half.z);
            Vector3 p6 = center + new Vector3(half.x, half.y, half.z);
            Vector3 p7 = center + new Vector3(-half.x, half.y, half.z);

            Transform t = bc.transform;
            corners[0] = t.TransformPoint(p0);
            corners[1] = t.TransformPoint(p1);
            corners[2] = t.TransformPoint(p2);
            corners[3] = t.TransformPoint(p3);
            corners[4] = t.TransformPoint(p4);
            corners[5] = t.TransformPoint(p5);
            corners[6] = t.TransformPoint(p6);
            corners[7] = t.TransformPoint(p7);

            return corners;
        }
        private void CreateCornerSpheres(GameObject parent, Vector3[] corners, float size = 0.01f)
        {
            if (cornerSpherePrefab == null || measurementBoxObj == null) return;

            for (int i = 0; i < 8; i++)
            {
                if(i == 0 || i == 1 || i == 2 || i == 3 || i == 7)
                {
                    GameObject sphereObj = Instantiate(cornerSpherePrefab);
                    sphereObj.name = "CornerSphere_" + i;
                    // 將 parent 設為 measurementBoxObj 而非 geometryObj
                    sphereObj.transform.SetParent(measurementBoxObj.transform, false);
                    sphereObj.transform.localPosition = measurementBoxObj.transform.InverseTransformPoint(corners[i]);
                    
                    // 設置角球大小，使用傳入的尺寸參數
                    sphereObj.transform.localScale = Vector3.one * size;
                    
                    // 確保角球始終保持可見性
                    MeshRenderer renderer = sphereObj.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        // 創建一個新的材質，避免共享材質被修改
                        Material sphereMat = new Material(renderer.material);
                        // 設置發光屬性，讓角球更醒目
                        sphereMat.SetColor("_EmissionColor", Color.white * 0.3f);
                        sphereMat.EnableKeyword("_EMISSION");
                        sphereMat.renderQueue = 2800; // 與線條相同的優先級
                        renderer.material = sphereMat;
                        
                        // 移除角球的碰撞體，避免干擾
                        Collider collider = sphereObj.GetComponent<Collider>();
                        if (collider != null)
                        {
                            Destroy(collider);
                        }
                    }
                }
            }
        }


        private Vector3[] BuildCubeEdges(Vector3[] corners)
        {
            Vector3[] edges = new Vector3[10];
            // 邊 (0,1)
            edges[0] = corners[0]; edges[1] = corners[1];
            // 邊 (1,2)
            edges[2] = corners[1]; edges[3] = corners[2];
            // 邊 (2,3)
            edges[4] = corners[2]; edges[5] = corners[3];
            // 邊 (3,0)
            edges[6] = corners[3]; edges[7] = corners[0];
            // 邊 (3,7)
            edges[8] = corners[3]; edges[9] = corners[7];
            return edges;
        }
        
        private void CreateEdgeLengthText(GameObject geometryObj, float distance, Vector3 worldPos, float modelSize = 1f)
        {
            // 建立文字物件
            GameObject textObj = new GameObject("EdgeLengthText");
            // 將文字物件設置為 measurementTextContainer 的子物件
            textObj.transform.SetParent(measurementBoxObj.transform, false);

            // 轉成 geometryObj 本地座標
            textObj.transform.localPosition = measurementBoxObj.transform.InverseTransformPoint(worldPos);

            // 添加調試日誌，觀察傳入的模型尺寸
            Debug.Log($"原始 modelSize: {modelSize}，距離: {distance}");
            
            // 強制限制 modelSize 的最大值，防止超大模型導致縮放過大
            modelSize = Mathf.Min(modelSize, 1.0f);
            
            // 使用分段函數處理不同尺寸的模型
            float charSize;
            if (modelSize < 0.5f) // 小模型
            {
                charSize = Mathf.Lerp(0.008f, 0.010f, modelSize / 0.5f);
            }
            else // 中型及大型模型
            {
                charSize = Mathf.Lerp(0.010f, 0.012f, (modelSize - 0.5f) / 0.5f);
            }
            
            // 添加主要文字
            TextMesh tm = textObj.AddComponent<TextMesh>();
            float cm = distance * 100f;
            tm.text = cm.ToString("F1") + " cm";
            tm.fontSize = 64;
            tm.color = Color.white;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = charSize;
            
            // 添加文字陰影效果
            GameObject shadow = new GameObject("TextShadow");
            shadow.transform.SetParent(textObj.transform, false);
            shadow.transform.localPosition = new Vector3(0.002f, -0.002f, 0.0005f);
            TextMesh shadowText = shadow.AddComponent<TextMesh>();
            shadowText.text = tm.text;
            shadowText.fontSize = tm.fontSize;
            shadowText.color = new Color(0f, 0f, 0f, 0.5f);
            shadowText.anchor = tm.anchor;
            shadowText.alignment = tm.alignment;
            shadowText.characterSize = charSize;

            // 使用協程動態計算背景尺寸，傳入模型尺寸
            StartCoroutine(AdjustBackgroundSize(textObj, tm, modelSize));

            textObj.transform.LookAt(arCamera.transform);
            textObj.transform.Rotate(0, 180f, 0);

            measurementTextObjs.Add(textObj);
        }

        private IEnumerator AdjustBackgroundSize(GameObject textObj, TextMesh tm, float modelSize = 1f)
        {
            // 等待兩幀讓TextMesh完全渲染
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            
            // 如果對象已被銷毀，則直接返回
            if (textObj == null || tm == null) yield break;
            
            // 添加文字背景
            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "TextBackground";
            background.transform.SetParent(textObj.transform, false);
            
            // 確保背景在文字後方
            background.transform.localPosition = new Vector3(0, 0, 0.003f);
            
            // 獲取文字內容
            string displayText = tm.text;
            
            // 計算文字的實際寬度（基於字元數量和字元大小）
            float textSize = tm.characterSize;  // 獲取字元大小
            int textLength = displayText.Length;
            
            // 每個字元的平均寬度（根據TextMesh字體特性調整）
            // 大幅增加字元寬度係數，確保背景足夠寬
            float charWidth = textSize * 2.3f;  // 增加到1.5倍的字元大小
            
            // 計算實際文字寬度
            float actualTextWidth = textLength * charWidth;
            
            // 設定最小和最大的背景寬度
            float minWidth = 0.2f;  // 增加最小寬度
            float maxWidth = 0.8f;  // 增加最大寬度限制
            
            // 確保背景寬度足夠容納文字，並且有更寬的額外空間
            float horizontalPadding = 0.12f;  // 大幅增加水平內邊距
            float textWidthWithPadding = actualTextWidth + horizontalPadding;
            
            // 最終背景寬度(受最小/最大值限制)
            float finalWidth = Mathf.Clamp(textWidthWithPadding, minWidth, maxWidth);
            
            // 固定背景高度，但略微增加
            float baseHeight = 0.07f;  // 增加背景高度
            
            // 設置背景尺寸
            background.transform.localScale = new Vector3(finalWidth, baseHeight, 1f);
            
            // 調試日誌，幫助檢查背景尺寸設置
            Debug.Log($"Text: '{displayText}', Length: {displayText.Length}, CharSize: {textSize}, " +
                     $"CalculatedWidth: {actualTextWidth}, FinalWidth: {finalWidth}");
            
            // 設置背景材質
            Renderer bgRenderer = background.GetComponent<Renderer>();
            if (bgMaterial != null)
            {
                // 創建材質實例避免共享問題
                Material instanceMaterial = new Material(bgMaterial);
                bgRenderer.material = instanceMaterial;
                
                // 黃色半透明背景
                instanceMaterial.color = new Color(1f, 0.7f, 0.047f, 0.8f);  // 增加不透明度到0.8
                
                // 設置背景的渲染優先級 - 讓它在線條前方、文字後方顯示
                instanceMaterial.renderQueue = 3100;
            }
            else
            {
                Material newMaterial = new Material(Shader.Find("Transparent/Diffuse"));
                newMaterial.color = new Color(1f, 0.7f, 0.047f, 0.8f); // 黃色半透明，增加不透明度
                newMaterial.renderQueue = 3100; // 讓它在線條前方顯示
                bgRenderer.material = newMaterial;
            }
            
            // 移除背景的碰撞體，避免干擾
            Collider collider = background.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            
            // 確保文字顯示在背景前方
            // 通過調整文字的渲染優先級
            MeshRenderer textRenderer = tm.GetComponent<MeshRenderer>();
            if (textRenderer != null)
            {
                Material textMaterial = new Material(textRenderer.material);
                textMaterial.renderQueue = 3200; // 高於背景的渲染優先級
                textRenderer.material = textMaterial;
            }
            
            // 也調整陰影文字的渲染優先級
            Transform shadowTrans = textObj.transform.Find("TextShadow");
            if (shadowTrans != null)
            {
                MeshRenderer shadowRenderer = shadowTrans.GetComponent<MeshRenderer>();
                if (shadowRenderer != null)
                {
                    Material shadowMaterial = new Material(shadowRenderer.material);
                    shadowMaterial.renderQueue = 3150; // 介於背景和文字之間
                    shadowRenderer.material = shadowMaterial;
                }
            }
        }

        private void ClearMeasurementBox()
        {
            if (measurementBoxObj != null)
            {
                Destroy(measurementBoxObj);
                measurementBoxObj = null;
            }
            foreach (var go in measurementTextObjs)
            {
                Destroy(go);
            }
            measurementTextObjs.Clear();
        }
        void StartMoveMode(GameObject model)
        {
            if (currentEditButtonPanel != null)
                currentEditButtonPanel.SetActive(false);
            if (forwardArrowPrefab != null)
            {
                currentForwardArrow = Instantiate(forwardArrowPrefab, currentEditingObject.transform);
                currentForwardArrow.transform.localPosition = Vector3.zero;
            }
            if (leftRightArrowPrefab != null)
            {
                currentLeftRightArrow = Instantiate(leftRightArrowPrefab, currentEditingObject.transform);
                currentLeftRightArrow.transform.localPosition = Vector3.zero;
            }
            // 禁用模型影子
            DisableModelShadows(model);
            
            // 確保在移動模式下輪廓仍然可見
            Transform geometryTransform = FindGeometry0Transform(model);
            if (geometryTransform != null)
            {
                Outline outline = geometryTransform.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.OutlineWidth = outlineWidth * 1.5f; // 在移動模式下略微加粗輪廓
                }
            }

            if (endOperationButtonPrefab != null)
            {
                Bounds modelBounds = GetModelBounds(model);
                Vector3 topCenter = new Vector3(modelBounds.center.x, modelBounds.max.y, modelBounds.center.z);
                currentEditButtonPanelMove = new GameObject("EditButtonPanel_Move");
                Canvas canvas = currentEditButtonPanelMove.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = arCamera;
                currentEditButtonPanelMove.AddComponent<CanvasScaler>();
                currentEditButtonPanelMove.AddComponent<GraphicRaycaster>();

                RectTransform panelRect = currentEditButtonPanelMove.GetComponent<RectTransform>();
                panelRect.sizeDelta = new Vector2(0.5f, 0.15f); // 縮小面板尺寸
                currentEditButtonPanelMove.transform.localScale = Vector3.one;

                // 直接放在模型頂部，不額外上移
                currentEditButtonPanelMove.transform.position = topCenter + Vector3.up*0.01f; // 僅微小偏移
                currentEditButtonPanelMove.transform.LookAt(arCamera.transform);
                currentEditButtonPanelMove.transform.Rotate(0, 180f, 0);

                HorizontalLayoutGroup layout = currentEditButtonPanelMove.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 0.01f; // 減小按鈕間距
                layout.childAlignment = TextAnchor.MiddleCenter;

                currentEndOpButton = Instantiate(endOperationButtonPrefab, currentEditButtonPanelMove.transform);
                // 移除多餘的位置設置，讓按鈕使用布局系統的排列
                currentEndOpButton.GetComponent<Button>().onClick.AddListener(() => { EndOperationMode(); });
            }
        }

        void StartRotateMode(GameObject model)
        {
            if (currentEditButtonPanel != null)
                currentEditButtonPanel.SetActive(false);
            if (rotateRingPrefab != null)
            {
                currentRotateRing = Instantiate(rotateRingPrefab, currentEditingObject.transform);
                currentRotateRing.transform.localPosition = Vector3.zero;
            }
            // 禁用模型影子
            DisableModelShadows(model);
            
            // 確保在旋轉模式下輪廓仍然可見
            Transform geometryTransform = FindGeometry0Transform(model);
            if (geometryTransform != null)
            {
                Outline outline = geometryTransform.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.OutlineWidth = outlineWidth * 1.5f; // 在旋轉模式下略微加粗輪廓
                }
            }

            if (endOperationButtonPrefab != null)
            {
                Bounds modelBounds = GetModelBounds(model);
                Vector3 topCenter = new Vector3(modelBounds.center.x, modelBounds.max.y, modelBounds.center.z);
                currentEditButtonPanelRotate = new GameObject("EditButtonPanel_Rotate");
                Canvas canvas = currentEditButtonPanelRotate.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = arCamera;
                currentEditButtonPanelRotate.AddComponent<CanvasScaler>();
                currentEditButtonPanelRotate.AddComponent<GraphicRaycaster>();

                RectTransform panelRect = currentEditButtonPanelRotate.GetComponent<RectTransform>();
                panelRect.sizeDelta = new Vector2(0.5f, 0.15f); // 縮小面板尺寸
                currentEditButtonPanelRotate.transform.localScale = Vector3.one;

                // 直接放在模型頂部，不額外上移
                currentEditButtonPanelRotate.transform.position = topCenter + Vector3.up*0.01f; // 僅微小偏移
                currentEditButtonPanelRotate.transform.LookAt(arCamera.transform);
                currentEditButtonPanelRotate.transform.Rotate(0, 180f, 0);

                HorizontalLayoutGroup layout = currentEditButtonPanelRotate.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 0.01f; // 減小按鈕間距
                layout.childAlignment = TextAnchor.MiddleCenter;

                currentEndOpButton = Instantiate(endOperationButtonPrefab, currentEditButtonPanelRotate.transform);
                // 移除多餘的位置設置，讓按鈕使用布局系統的排列
                currentEndOpButton.GetComponent<Button>().onClick.AddListener(() => { EndOperationMode(); });
            }
        }


        void EndOperationMode()
        {
            if (currentForwardArrow != null) { Destroy(currentForwardArrow); currentForwardArrow = null; }
            if (currentLeftRightArrow != null) { Destroy(currentLeftRightArrow); currentLeftRightArrow = null; }
            if (currentRotateRing != null) { Destroy(currentRotateRing); currentRotateRing = null; }
            if (currentEndOpButton != null) { Destroy(currentEndOpButton); currentEndOpButton = null; }
            if (currentEditButtonPanelMove != null) { Destroy(currentEditButtonPanelMove); currentEditButtonPanelMove = null; }
            if (currentEditButtonPanelRotate != null) { Destroy(currentEditButtonPanelRotate); currentEditButtonPanelRotate = null; }
            if (currentEditButtonPanel != null)
                currentEditButtonPanel.SetActive(true);
            // 重新啟用模型影子
            if (currentEditingObject != null)
            {
                EnableModelShadows(currentEditingObject);
            }
            ClearMeasurementBox();
        }

        private ProductData ParseProductInfo(GameObject model)
        {
            if (!spawnedModelInfo.TryGetValue(model, out ProductData pd))
            {
                Debug.LogError("找不到該模型對應的 ProductData");
                return null;
            }
            pd.from = true;
            return pd;
        }

        void ExitEditMode(GameObject model)
        {
            Debug.Log("退出編輯模式: " + model.name);

            ClearMeasurementBox();

            // 移除所有Outline組件
            RemoveAllOutlines(model);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (originalMaterials.ContainsKey(model))
            {
                Material[][] matsArray = originalMaterials[model];
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (i < matsArray.Length)
                    {
                        renderers[i].materials = matsArray[i];
                    }
                }
                originalMaterials.Remove(model);
            }
            if (currentEditButtonPanel != null)
            {
                Destroy(currentEditButtonPanel);
                currentEditButtonPanel = null;
            }
            if (currentForwardArrow != null) { Destroy(currentForwardArrow); currentForwardArrow = null; }
            if (currentLeftRightArrow != null) { Destroy(currentLeftRightArrow); currentLeftRightArrow = null; }
            if (currentRotateRing != null) { Destroy(currentRotateRing); currentRotateRing = null; }
            if (currentEndOpButton != null) { Destroy(currentEndOpButton); currentEndOpButton = null; }
            Collider[] colliders = model.GetComponentsInChildren<Collider>();
            foreach (var c in colliders)
            {
                c.enabled = true;
            }
            currentEditingObject = null;
        }

        void DeleteModel(GameObject model)
        {
            if (spawnedObjects.Contains(model))
                spawnedObjects.Remove(model);
            if (spawnedModelInfo.ContainsKey(model))
                spawnedModelInfo.Remove(model);
            ClearMeasurementBox();
            Destroy(model);
            
            currentEditingObject = null;
            if (currentEditButtonPanel != null)
            {
                Destroy(currentEditButtonPanel);
                currentEditButtonPanel = null;
            }
        }

        void StartCustomize(GameObject model)
        {
            if (CustomizePanelPrefab != null)
            {
                CustomizePanelPrefab.SetActive(true);
            }

            // 檢查 modelConverter 是否已指派
            if (modelConverter != null)
            {
                // 從 spawnedModelInfo 字典中取得當前模型的 ProductData
                if (spawnedModelInfo.TryGetValue(model, out ProductData pd))
                {
                    // 呼叫並傳入模型的 productId
                    modelConverter.SetCurrentModelId(pd.productId);
                }
                else
                {
                    Debug.LogError($"在 spawnedModelInfo 中找不到模型 {model.name} 的資料！");
                }
            }
            else
            {
                Debug.LogError("ModelConverter 尚未在 ModelLoader1 的 Inspector 中指派！");
            }

        }

        private async Task LoadModelFromUrl(string modelUrl, string productJson)
        {
            try
            {
                // 嘗試從緩存加載
                byte[] modelData;
                if (cacheManager.TryLoadFromCache(modelUrl, out modelData, out productJson))
                {
                    Debug.Log("從緩存加載模型");
                    await LoadModelFromBytes(modelData, productJson);
                    return;
                }

                // 如果緩存中沒有，從網絡下載
                using (UnityWebRequest www = UnityWebRequest.Get(modelUrl))
                {
                    var operation = www.SendWebRequest();
                    while (!operation.isDone)
                        await Task.Yield();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        modelData = www.downloadHandler.data;
                        // 保存到緩存
                        await cacheManager.CacheModel(modelUrl, modelData, productJson);
                        await LoadModelFromBytes(modelData, productJson);
                    }
                    else
                    {
                        Debug.LogError($"下載模型失敗: {www.error}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"加載模型時發生錯誤: {e.Message}");
            }
        }

        private async Task LoadModelFromBytes(byte[] modelData, string productJson)
        {
            try
            {
                var gltf = new GltfImport();
                bool success = await gltf.LoadGltfBinary(modelData);
                
                if (success)
                {
                    GameObject parentObject = new GameObject("ARModel");
                    parentObject.transform.position = placementIndicator.transform.position;
                    parentObject.transform.rotation = placementIndicator.transform.rotation;
                    spawnedObjects.Add(parentObject);
                    // 儲存該模型對應的產品資訊
                    spawnedModelInfo[parentObject] = selectedProductData;
                    
                    var instance = await gltf.InstantiateMainSceneAsync(parentObject.transform);
                    if (instance != null)
                    {
                        // await Task.Delay(200);

                        EnableModelShadows(parentObject);
                        Vector3 desiredDimensions = GetDesiredDimensions();
                        
                        // 添加ModelCollider組件並顯式調用生成方法
                        ARFurniture.ModelCollider modelCollider = parentObject.AddComponent<ARFurniture.ModelCollider>();
                        modelCollider.voxelResolution = 80; // 設置合適的解析度
                        modelCollider.inflateAmount = 0f; // 設置膨脹量
                        modelCollider.debugShowColliders = false; // 是否顯示碰撞體視覺效果
                        modelCollider.autoGenerateColliders = false; // 關閉自動生成，改為手動調用
                        modelCollider.GenerateColliders(); // 顯式調用生成方法
                        
                        StartCoroutine(DelayedColliderGeneration(modelCollider, parentObject, desiredDimensions));
                    }
                }
                else
                {
                    Debug.LogError("模型加載失敗");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"處理模型數據時發生錯誤: {e.Message}");
            }
        }

        IEnumerator DelayedColliderGeneration(ARFurniture.ModelCollider modelCollider, GameObject parentObject, Vector3 desiredDimensions)
        {
            // **先調整尺寸**
            yield return StartCoroutine(AdjustModelPosition(parentObject, desiredDimensions));
            
            // **尺寸調整完成後再生成碰撞體**
            yield return new WaitForSeconds(0.1f);
            modelCollider.GenerateColliders();
        }

        // 添加輔助方法來查找geometry0物件
        private Transform FindGeometry0Transform(GameObject model)
        {
            // 首先嘗試查找標準路徑 armodel/world/geometry0
            Transform arModel = model.transform.Find("ARModel");
            if (arModel != null)
            {
                Transform world = arModel.Find("world");
                if (world != null)
                {
                    Transform geometry0 = world.Find("geometry0");
                    if (geometry0 != null)
                    {
                        return geometry0;
                    }
                }
            }

            // 嘗試直接查找geometry0
            Transform directGeometry0 = model.transform.Find("geometry0");
            if (directGeometry0 != null)
            {
                return directGeometry0;
            }

            // 遞歸查找名稱包含"geometry"的物件
            return FindChildWithNameContaining(model.transform, "geometry");
        }

        // 遞歸查找名稱包含特定字符串的子物件
        private Transform FindChildWithNameContaining(Transform parent, string nameContains)
        {
            foreach (Transform child in parent)
            {
                if (child.name.ToLower().Contains(nameContains.ToLower()))
                {
                    return child;
                }

                Transform foundInChild = FindChildWithNameContaining(child, nameContains);
                if (foundInChild != null)
                {
                    return foundInChild;
                }
            }
            return null;
        }

        // 添加輔助方法來移除所有Outline組件
        private void RemoveAllOutlines(GameObject model)
        {
            // 移除模型自身的Outline
            Outline modelOutline = model.GetComponent<Outline>();
            if (modelOutline != null)
            {
                Destroy(modelOutline);
            }

            // 移除所有子物件上的Outline
            foreach (Outline outline in model.GetComponentsInChildren<Outline>())
            {
                Destroy(outline);
            }
        }

        public async void LoadModel(string githubModelURL, ProductData productData)
        {
            if (loading)
            {
                Debug.Log("正在加載中，請稍候...");
                return;
            }

            selectedProductData = productData;
            loading = true;

            if (loadingPanel != null)
            {
                loadingPanel.SetActive(true);
            }

            await LoadModelFromUrl(githubModelURL, JsonConvert.SerializeObject(productData));

            if (loadingPanel != null)
            {
                loading = false;
                loadingPanel.SetActive(false);
            }
        }
    }
}