using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;

public class ARPowerTowerManager : MonoBehaviour
{
    [Header("参照コンポーネント")]
    [SerializeField] private GeoDataManager geoDataManager;
    [SerializeField] private GeospatialLocationManager locationManager;
    [SerializeField] private ARAnchorManager anchorManager;

    [Header("生成アセット設定")]
    [SerializeField] private GameObject towerLabelPrefab;
    [SerializeField] private float maxRadiusMeters = 2000f;  // テスト用に検索範囲を2000m(2km)に拡大
    [SerializeField] private float heightOffsetMeters = 25f; // 地上高25m

    [Header("テスト・デバッグ設定")]
    [SerializeField] private bool autoSetMockToFirstTower = true;
    [Tooltip("ONにするとVPSトラッキング未完了でも強制的にアンカー生成を試みます（テスト用）")]
    [SerializeField] private bool forceSpawnWithoutTracking = false;

    private class ActiveTower
    {
        public ARGeospatialAnchor Anchor;
        public GameObject MockGameObject;
        public TowerLabelController LabelController;
        public GeoDataManager.TowerPoint Data;
    }

    private Dictionary<string, ActiveTower> activeTowers = new Dictionary<string, ActiveTower>();
    private string nearestName = "初期化中...";
    private float nearestDistance = -1f;
    private double nearestLat = 0.0;
    private double nearestLon = 0.0;
    private bool mockPosInitialized = false;

    // デバッグ・診断用
    private float updateTimer = 0f;
    private const float UpdateInterval = 1.0f;
    private string spawnBlockedReason = "未実行";
    private int inRangeCount = 0;

    void Update()
    {
        updateTimer += Time.deltaTime;
        if (updateTimer >= UpdateInterval)
        {
            updateTimer = 0f;
            UpdateTowersSafe();
        }
    }

    private void UpdateTowersSafe()
    {
        try
        {
            UpdateTowers();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ARPowerTowerManager] エラー発生: {e.Message}");
            spawnBlockedReason = $"スクリプトエラー: {e.Message}";
        }
    }

    public void UpdateTowers()
    {
        // コンポーネントアタッチチェック
        if (geoDataManager == null || locationManager == null)
        {
            nearestName = "マネージャー未設定";
            spawnBlockedReason = "エラー: InspectorでGeoData/LocationManagerが未設定";
            return;
        }

        if (geoDataManager.LoadedTowers == null || geoDataManager.LoadedTowers.Count == 0)
        {
            nearestName = "データ未読み込み(0件)";
            nearestDistance = -1f;
            spawnBlockedReason = "エラー: 鉄塔データが0件";
            return;
        }

        if (towerLabelPrefab == null)
        {
            spawnBlockedReason = "エラー: InspectorでTower Label Prefabが未設定";
            return;
        }

#if UNITY_EDITOR
        if (autoSetMockToFirstTower && !mockPosInitialized)
        {
            var firstTower = geoDataManager.LoadedTowers[0];
            locationManager.SetMockCoordinates(firstTower.Latitude - 0.001, firstTower.Longitude, 50.0);
            mockPosInitialized = true;
        }
#endif

        double userLat = locationManager.CurrentLatitude;
        double userLon = locationManager.CurrentLongitude;
        double userAlt = locationManager.CurrentAltitude;

        if (Math.Abs(userLat) < 1.0 || Math.Abs(userLon) < 1.0)
        {
            nearestName = $"測位待ち (Lat:{userLat:F3}, Lon:{userLon:F3})";
            nearestDistance = -1f;
            spawnBlockedReason = "GPS/VPS未測位";
            return;
        }

        // 3Dアンカーを描画可能な条件かチェック
#if UNITY_EDITOR
        bool canSpawnAnchor = true;
#else
        bool canSpawnAnchor = forceSpawnWithoutTracking || locationManager.IsTrackingReady;
#endif

        float minDistance = float.MaxValue;
        GeoDataManager.TowerPoint nearestTower = null;
        HashSet<string> towersInRange = new HashSet<string>();
        inRangeCount = 0;

        int towerCount = geoDataManager.LoadedTowers.Count;
        for (int i = 0; i < towerCount; i++)
        {
            var tower = geoDataManager.LoadedTowers[i];
            if (tower == null) continue;

            float distance = CoordinateConverter.CalculateDistance(userLat, userLon, tower.Latitude, tower.Longitude);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearestTower = tower;
            }

            // 指定半径内の鉄塔をカウント
            if (distance <= maxRadiusMeters)
            {
                inRangeCount++;
                towersInRange.Add(tower.Id);

                if (canSpawnAnchor)
                {
                    if (!activeTowers.ContainsKey(tower.Id))
                    {
                        SpawnTowerAnchor(tower, userAlt);
                    }

                    if (activeTowers.TryGetValue(tower.Id, out var activeTower))
                    {
                        activeTower.LabelController?.UpdateDistance(distance);

#if UNITY_EDITOR
                        UpdateMockPosition(activeTower, userLat, userLon);
#endif
                    }
                }
            }
        }

        // 最寄り情報を反映
        if (nearestTower != null)
        {
            nearestDistance = minDistance;
            nearestName = $"{nearestTower.TowerName} No.{nearestTower.TowerNum}";
            nearestLat = nearestTower.Latitude;
            nearestLon = nearestTower.Longitude;
        }

        // 診断理由の更新
        if (!canSpawnAnchor)
        {
            spawnBlockedReason = "VPS未追従(IsTrackingReady=false)";
        }
        else if (inRangeCount == 0)
        {
            spawnBlockedReason = $"半径{maxRadiusMeters}m内に鉄塔なし (最寄り:{nearestDistance:F0}m)";
        }
        else if (activeTowers.Count > 0)
        {
            spawnBlockedReason = "正常描画中";
        }

        // 範囲外アンカーの破棄
        List<string> idsToRemove = new List<string>();
        foreach (var key in activeTowers.Keys)
        {
            if (!towersInRange.Contains(key) || !canSpawnAnchor)
            {
                idsToRemove.Add(key);
            }
        }

        for (int i = 0; i < idsToRemove.Count; i++)
        {
            RemoveTowerAnchor(idsToRemove[i]);
        }
    }

    private void SpawnTowerAnchor(GeoDataManager.TowerPoint tower, double userAltitude)
    {
        double targetAltitude = userAltitude + heightOffsetMeters;
        Quaternion rotation = Quaternion.identity;

#if UNITY_EDITOR
        Vector3 relativePos = CoordinateConverter.LatLonToUnityPosition(
            locationManager.CurrentLatitude, locationManager.CurrentLongitude,
            tower.Latitude, tower.Longitude
        );
        relativePos.y = heightOffsetMeters;

        Vector3 cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Vector3 worldTargetPos = cameraPos + relativePos;

        GameObject mockObj = new GameObject($"MockTower_{tower.Id}");
        mockObj.transform.position = worldTargetPos;

        GameObject labelObjMock = Instantiate(towerLabelPrefab, mockObj.transform);
        labelObjMock.transform.localPosition = Vector3.zero;

        var controllerMock = labelObjMock.GetComponent<TowerLabelController>();
        if (controllerMock != null)
        {
            controllerMock.SetInfo(tower.TowerName, tower.TowerNum);
        }

        activeTowers.Add(tower.Id, new ActiveTower
        {
            Anchor = null,
            MockGameObject = mockObj,
            LabelController = controllerMock,
            Data = tower
        });
#else
        if (anchorManager == null)
        {
            spawnBlockedReason = "エラー: InspectorでARAnchorManager未設定";
            return;
        }

        try
        {
            ARGeospatialAnchor anchor = anchorManager.AddAnchor(tower.Latitude, tower.Longitude, targetAltitude, rotation);

            if (anchor != null)
            {
                GameObject labelObj = Instantiate(towerLabelPrefab, anchor.transform);
                labelObj.transform.localPosition = Vector3.zero;

                var controller = labelObj.GetComponent<TowerLabelController>();
                if (controller != null)
                {
                    controller.SetInfo(tower.TowerName, tower.TowerNum);
                }

                activeTowers.Add(tower.Id, new ActiveTower
                {
                    Anchor = anchor,
                    MockGameObject = null,
                    LabelController = controller,
                    Data = tower
                });
            }
            else
            {
                spawnBlockedReason = "AddAnchorがnullを返却(ARCore拡張不調)";
            }
        }
        catch (Exception e)
        {
            spawnBlockedReason = $"AddAnchor例外: {e.Message}";
        }
#endif
    }

#if UNITY_EDITOR
    private void UpdateMockPosition(ActiveTower activeTower, double userLat, double userLon)
    {
        if (activeTower.MockGameObject == null || activeTower.Data == null) return;

        Vector3 relativePos = CoordinateConverter.LatLonToUnityPosition(
            userLat, userLon,
            activeTower.Data.Latitude, activeTower.Data.Longitude
        );
        relativePos.y = heightOffsetMeters;

        Vector3 cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        activeTower.MockGameObject.transform.position = cameraPos + relativePos;
    }
#endif

    private void RemoveTowerAnchor(string id)
    {
        if (activeTowers.TryGetValue(id, out var activeTower))
        {
            if (activeTower.Anchor != null)
            {
                Destroy(activeTower.Anchor.gameObject);
            }
            if (activeTower.MockGameObject != null)
            {
                Destroy(activeTower.MockGameObject);
            }
            activeTowers.Remove(id);
        }
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 28;
        style.normal.textColor = Color.yellow;

        int totalDbCount = (geoDataManager != null && geoDataManager.LoadedTowers != null) ? geoDataManager.LoadedTowers.Count : 0;

        string trackingText = (locationManager != null) ? locationManager.TrackingStatusText : "未設定";
        double curLat = (locationManager != null) ? locationManager.CurrentLatitude : 0.0;
        double curLon = (locationManager != null) ? locationManager.CurrentLongitude : 0.0;
        double curAlt = (locationManager != null) ? locationManager.CurrentAltitude : 0.0;

        string distStr = (nearestDistance >= 0f) ? $"{nearestDistance:F1}m" : "計算不可";

        string statusText = $"[VPSステータス] {trackingText}\n" +
                           $"[現在地] 緯度:{curLat:F5}, 経度:{curLon:F5}, 標高:{curAlt:F1}m\n" +
                           $"[DB保持数] {totalDbCount} 件\n" +
                           $"[最寄鉄塔] {nearestName}\n" +
                           $"[最寄距離] {distStr} (目標座標: {nearestLat:F5}, {nearestLon:F5})\n" +
                           $"[範囲内数] {inRangeCount} 件 (設定半径: {maxRadiusMeters}m)\n" +
                           $"[描画基数] {activeTowers.Count} 基\n" +
                           $"[診断結果] {spawnBlockedReason}";

        GUI.Label(new Rect(30, 40, 950, 500), style: style, text: statusText);
    }
}