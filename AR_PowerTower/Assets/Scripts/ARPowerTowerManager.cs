using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;

public class ARPowerTowerManager : MonoBehaviour
{
    public enum LocationMode
    {
        GPS_FilteredCompass,
        VPS_Geospatial
    }

    [Header("動作モード設定")]
    [SerializeField] private LocationMode currentLocationMode = LocationMode.GPS_FilteredCompass;

    [Header("参照コンポーネント")]
    [SerializeField] private GeoDataManager geoDataManager;
    [SerializeField] private GPSLocationManager gpsLocationManager;
    [SerializeField] private GeospatialLocationManager vpsLocationManager;
    [SerializeField] private ARAnchorManager anchorManager;

    [Header("生成アセット設定")]
    [SerializeField] private GameObject towerLabelPrefab;
    [SerializeField] private float maxRadiusMeters = 2000f;
    
    [Tooltip("鉄塔ラベルの地上高度(m)")]
    [SerializeField] private float towerHeightOffsetMeters = 25f;
    
    [Tooltip("送電線の地上高度(m)")]
    [SerializeField] private float lineHeightOffsetMeters = 35f;

    [Header("テスト・デバッグ設定")]
    [SerializeField] private bool autoSetMockToFirstTower = true;
    [SerializeField] private bool forceSpawnWithoutTracking = false;

    private class ActiveTower
    {
        public ARGeospatialAnchor Anchor;
        public GameObject MockGameObject;
        public TowerLabelController LabelController;
        public GeoDataManager.TowerPoint Data;
    }

    private Dictionary<string, ActiveTower> activeTowers = new Dictionary<string, ActiveTower>();
    private Dictionary<string, PowerLineController> activeLines = new Dictionary<string, PowerLineController>();

    private string nearestName = "初期化中...";
    private float nearestDistance = -1f;
    private bool mockPosInitialized = false;

    private float updateTimer = 0f;
    private const float UpdateInterval = 1.0f;
    private string spawnBlockedReason = "未実行";
    private int inRangeTowerCount = 0;
    private int inRangeLineCount = 0;

    public ILocationProvider ActiveLocationProvider
    {
        get
        {
            if (currentLocationMode == LocationMode.VPS_Geospatial && vpsLocationManager != null)
            {
                return vpsLocationManager;
            }
            return gpsLocationManager;
        }
    }

    private void Awake()
    {
        if (geoDataManager == null) geoDataManager = GetComponent<GeoDataManager>();
        if (gpsLocationManager == null) gpsLocationManager = GetComponent<GPSLocationManager>();
        if (vpsLocationManager == null) vpsLocationManager = GetComponent<GeospatialLocationManager>();
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
    }

    private void Update()
    {
        updateTimer += Time.deltaTime;
        if (updateTimer >= UpdateInterval)
        {
            updateTimer = 0f;
            UpdateObjectsSafe();
        }

        UpdateDynamicPositions();
    }

    public void SwitchLocationMode(LocationMode newMode)
    {
        if (currentLocationMode == newMode) return;

        Debug.Log($"[ARPowerTowerManager] モード切り替え: {currentLocationMode} -> {newMode}");
        currentLocationMode = newMode;

        ClearAllObjects();
        UpdateObjectsSafe();
    }

    private void UpdateObjectsSafe()
    {
        try
        {
            UpdateObjects();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ARPowerTowerManager] エラー発生: {e.Message}\n{e.StackTrace}");
            spawnBlockedReason = $"スクリプト例外: {e.Message}";
        }
    }

    public void UpdateObjects()
    {
        ILocationProvider provider = ActiveLocationProvider;

        if (geoDataManager == null || provider == null)
        {
            spawnBlockedReason = "エラー: Inspector設定不足";
            return;
        }

        if (towerLabelPrefab == null)
        {
            spawnBlockedReason = "エラー: Tower Label Prefabが未設定";
            return;
        }

#if UNITY_EDITOR
        if (autoSetMockToFirstTower && !mockPosInitialized && gpsLocationManager != null)
        {
            if (geoDataManager.LoadedTowers.Count > 0)
            {
                var firstTower = geoDataManager.LoadedTowers[0];
                gpsLocationManager.SetMockLocation(firstTower.Latitude - 0.001, firstTower.Longitude, 50.0, 0f);
                if (vpsLocationManager != null)
                {
                    vpsLocationManager.SetMockCoordinates(firstTower.Latitude - 0.001, firstTower.Longitude, 50.0);
                }
            }
            mockPosInitialized = true;
        }
#endif

        double userLat = provider.CurrentLatitude;
        double userLon = provider.CurrentLongitude;
        double userAlt = provider.CurrentAltitude;

        if (Math.Abs(userLat) < 1.0 || Math.Abs(userLon) < 1.0)
        {
            spawnBlockedReason = "未測位 (緯度経度がほぼ0)";
            return;
        }

        bool canSpawn = forceSpawnWithoutTracking || provider.IsTrackingReady;

        // 1. 鉄塔 (Point) 更新
        float minDistance = float.MaxValue;
        GeoDataManager.TowerPoint nearestTower = null;
        HashSet<string> towersInRange = new HashSet<string>();
        inRangeTowerCount = 0;

        foreach (var tower in geoDataManager.LoadedTowers)
        {
            if (tower == null) continue;
            float distance = CoordinateConverter.CalculateDistance(userLat, userLon, tower.Latitude, tower.Longitude);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearestTower = tower;
            }

            if (distance <= maxRadiusMeters)
            {
                inRangeTowerCount++;
                towersInRange.Add(tower.Id);

                if (canSpawn)
                {
                    if (!activeTowers.ContainsKey(tower.Id))
                    {
                        SpawnTowerObject(tower, userLat, userLon, userAlt);
                    }
                    if (activeTowers.TryGetValue(tower.Id, out var activeTower))
                    {
                        activeTower.LabelController?.UpdateDistance(distance);
                    }
                }
            }
        }

        if (nearestTower != null)
        {
            nearestDistance = minDistance;
            nearestName = $"{nearestTower.TowerName} No.{nearestTower.TowerNum}";
        }

        List<string> towersToRemove = new List<string>();
        foreach (var key in activeTowers.Keys)
        {
            if (!towersInRange.Contains(key) || !canSpawn) towersToRemove.Add(key);
        }
        foreach (var id in towersToRemove) RemoveTowerAnchor(id);

        // 2. 送電線 (LineString) 更新
        HashSet<string> linesInRange = new HashSet<string>();
        inRangeLineCount = 0;

        foreach (var lineData in geoDataManager.LoadedLines)
        {
            if (lineData == null || lineData.Coordinates.Count < 2) continue;

            if (IsPowerLineCrossesRadius(userLat, userLon, lineData.Coordinates, maxRadiusMeters))
            {
                inRangeLineCount++;
                linesInRange.Add(lineData.Id);

                if (canSpawn && !activeLines.ContainsKey(lineData.Id))
                {
                    SpawnPowerLineObject(lineData);
                }
            }
        }

        List<string> linesToRemove = new List<string>();
        foreach (var key in activeLines.Keys)
        {
            if (!linesInRange.Contains(key) || !canSpawn) linesToRemove.Add(key);
        }
        foreach (var id in linesToRemove)
        {
            if (activeLines.TryGetValue(id, out var lineCtrl))
            {
                Destroy(lineCtrl.gameObject);
                activeLines.Remove(id);
            }
        }

        if (!canSpawn) spawnBlockedReason = "トラッキング未準備 (IsTrackingReady=false)";
        else spawnBlockedReason = $"正常描画中 ({currentLocationMode}モード)";
    }

    private bool IsPowerLineCrossesRadius(double userLat, double userLon, List<GeoDataManager.PowerLineData.Vector2D> coords, float radiusMeters)
    {
        int count = coords.Count;

        for (int i = 0; i < count; i++)
        {
            float dist = CoordinateConverter.CalculateDistance(userLat, userLon, coords[i].Latitude, coords[i].Longitude);
            if (dist <= radiusMeters) return true;
        }

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 p1 = CoordinateConverter.LatLonToUnityPosition(userLat, userLon, coords[i].Latitude, coords[i].Longitude);
            Vector3 p2 = CoordinateConverter.LatLonToUnityPosition(userLat, userLon, coords[i + 1].Latitude, coords[i + 1].Longitude);

            float segmentDist = DistanceToSegment2D(Vector3.zero, p1, p2);
            if (segmentDist <= radiusMeters) return true;
        }

        return false;
    }

    private float DistanceToSegment2D(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 p2 = new Vector2(p.x, p.z);
        Vector2 a2 = new Vector2(a.x, a.z);
        Vector2 b2 = new Vector2(b.x, b.z);

        Vector2 ab = b2 - a2;
        float sqrLen = ab.sqrMagnitude;
        if (sqrLen < 0.0001f) return Vector2.Distance(p2, a2);

        float t = Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / sqrLen);
        Vector2 projection = a2 + t * ab;
        return Vector2.Distance(p2, projection);
    }

    private void SpawnTowerObject(GeoDataManager.TowerPoint tower, double userLat, double userLon, double userAlt)
    {
        ILocationProvider provider = ActiveLocationProvider;

#if !UNITY_EDITOR
        if (currentLocationMode == LocationMode.VPS_Geospatial && anchorManager != null)
        {
            try
            {
                double targetAltitude = userAlt + towerHeightOffsetMeters;
                ARGeospatialAnchor anchor = anchorManager.AddAnchor(tower.Latitude, tower.Longitude, targetAltitude, Quaternion.identity);

                if (anchor != null)
                {
                    GameObject labelObj = Instantiate(towerLabelPrefab, anchor.transform);
                    labelObj.transform.localPosition = Vector3.zero;

                    var controller = labelObj.GetComponent<TowerLabelController>();
                    if (controller != null) controller.SetInfo(tower.TowerName, tower.TowerNum);

                    activeTowers.Add(tower.Id, new ActiveTower
                    {
                        Anchor = anchor,
                        MockGameObject = null,
                        LabelController = controller,
                        Data = tower
                    });
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ARPowerTowerManager] VPS Anchor作成失敗: {e.Message}");
            }
        }
#endif

        GameObject mockObj = new GameObject($"TowerObj_{tower.Id}");
        GameObject labelObjMock = Instantiate(towerLabelPrefab, mockObj.transform);
        labelObjMock.transform.localPosition = Vector3.zero;

        var controllerMock = labelObjMock.GetComponent<TowerLabelController>();
        if (controllerMock != null) controllerMock.SetInfo(tower.TowerName, tower.TowerNum);

        var activeTowerEntry = new ActiveTower
        {
            Anchor = null,
            MockGameObject = mockObj,
            LabelController = controllerMock,
            Data = tower
        };

        activeTowers.Add(tower.Id, activeTowerEntry);
        UpdateSingleTowerPosition(activeTowerEntry, userLat, userLon, provider.HeadingOffset);
    }

    private void SpawnPowerLineObject(GeoDataManager.PowerLineData lineData)
    {
        GameObject lineObj = new GameObject($"PowerLineObj_{lineData.Id}");
        var lineCtrl = lineObj.AddComponent<PowerLineController>();
        lineCtrl.Init(lineData, towerLabelPrefab, lineHeightOffsetMeters);

        activeLines.Add(lineData.Id, lineCtrl);
    }

    private void UpdateDynamicPositions()
    {
        ILocationProvider provider = ActiveLocationProvider;
        if (provider == null) return;

        double userLat = provider.CurrentLatitude;
        double userLon = provider.CurrentLongitude;
        float headingOffset = provider.HeadingOffset;
        Vector3 cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        if (currentLocationMode == LocationMode.GPS_FilteredCompass || Application.isEditor)
        {
            foreach (var activeTower in activeTowers.Values)
            {
                if (activeTower.MockGameObject != null)
                {
                    UpdateSingleTowerPosition(activeTower, userLat, userLon, headingOffset);
                }
            }
        }

        foreach (var lineCtrl in activeLines.Values)
        {
            if (lineCtrl != null)
            {
                lineCtrl.UpdatePositions(userLat, userLon, headingOffset, cameraPos);
            }
        }
    }

    private void UpdateSingleTowerPosition(ActiveTower activeTower, double userLat, double userLon, float arNorthAngle)
    {
        if (activeTower.MockGameObject == null || activeTower.Data == null) return;

        Vector3 basePos = CoordinateConverter.LatLonToUnityPosition(userLat, userLon, activeTower.Data.Latitude, activeTower.Data.Longitude);
        Vector3 rotatedPos = Quaternion.Euler(0, arNorthAngle, 0) * basePos;
        rotatedPos.y = towerHeightOffsetMeters;

        Vector3 cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        activeTower.MockGameObject.transform.position = cameraPos + rotatedPos;
    }

    /// <summary>
    /// スマホの画角の中心（カメラの視線方向）に最も近い鉄塔情報を取得
    /// </summary>
    private string GetCenterFocusedTowerInfo()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null || activeTowers.Count == 0) return "画面内に鉄塔なし";

        Vector3 camPos = mainCam.transform.position;
        Vector3 camForward = mainCam.transform.forward;

        float minAngle = float.MaxValue;
        string focusedTowerText = "対象なし (画角外)";

        ILocationProvider provider = ActiveLocationProvider;
        double userLat = provider != null ? provider.CurrentLatitude : 0.0;
        double userLon = provider != null ? provider.CurrentLongitude : 0.0;

        foreach (var towerEntry in activeTowers.Values)
        {
            Vector3 towerWorldPos = Vector3.zero;

            if (towerEntry.Anchor != null)
            {
                towerWorldPos = towerEntry.Anchor.transform.position;
            }
            else if (towerEntry.MockGameObject != null)
            {
                towerWorldPos = towerEntry.MockGameObject.transform.position;
            }
            else continue;

            Vector3 dirToTower = (towerWorldPos - camPos).normalized;
            float angle = Vector3.Angle(camForward, dirToTower);

            // 画角内(中心から60度以内)かつ最も中央に近い鉄塔を選出
            if (angle < 60f && angle < minAngle)
            {
                minAngle = angle;
                float dist = CoordinateConverter.CalculateDistance(userLat, userLon, towerEntry.Data.Latitude, towerEntry.Data.Longitude);
                string numStr = string.IsNullOrEmpty(towerEntry.Data.TowerNum) || towerEntry.Data.TowerNum == "-" ? "" : $" No.{towerEntry.Data.TowerNum}";
                focusedTowerText = $"{towerEntry.Data.TowerName}{numStr} ({dist:F0}m)";
            }
        }

        return focusedTowerText;
    }

    private void RemoveTowerAnchor(string id)
    {
        if (activeTowers.TryGetValue(id, out var activeTower))
        {
            if (activeTower.Anchor != null) Destroy(activeTower.Anchor.gameObject);
            if (activeTower.MockGameObject != null) Destroy(activeTower.MockGameObject);
            activeTowers.Remove(id);
        }
    }

    private void ClearAllObjects()
    {
        foreach (var activeTower in activeTowers.Values)
        {
            if (activeTower.Anchor != null) Destroy(activeTower.Anchor.gameObject);
            if (activeTower.MockGameObject != null) Destroy(activeTower.MockGameObject);
        }
        activeTowers.Clear();

        foreach (var lineCtrl in activeLines.Values)
        {
            if (lineCtrl != null) Destroy(lineCtrl.gameObject);
        }
        activeLines.Clear();
    }

    void OnGUI()
    {
        // -------------------------------------------------------------------------
        // 1. 画面上部中央: カメラ正面（画角中心）で捉えている鉄塔情報のリアルタイム表示
        // -------------------------------------------------------------------------
        float topWidth = Mathf.Min(Screen.width * 0.9f, 750f);
        float topHeight = 70f;
        float topX = (Screen.width - topWidth) / 2f;

        GUIStyle topHeaderStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.cyan }
        };

        string centerTowerInfo = GetCenterFocusedTowerInfo();
        GUI.Box(new Rect(topX, 20, topWidth, topHeight), $"🎯 照準中の鉄塔: {centerTowerInfo}", topHeaderStyle);

        // -------------------------------------------------------------------------
        // 2. 画面下部中央: GPS/VPS モード切り替えボタン
        // -------------------------------------------------------------------------
        float btnWidth = Mathf.Min(Screen.width * 0.85f, 650f);
        float btnHeight = 85f;
        float btnX = (Screen.width - btnWidth) / 2f;
        float btnY = Screen.height - btnHeight - 40f; // 画面下部から40px上

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold
        };

        string nextModeText = (currentLocationMode == LocationMode.GPS_FilteredCompass) 
            ? "モード切替 [現在: GPS ➔ TAPでVPSへ]" 
            : "モード切替 [現在: VPS ➔ TAPでGPSへ]";

        if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), nextModeText, buttonStyle))
        {
            LocationMode nextMode = (currentLocationMode == LocationMode.GPS_FilteredCompass) 
                ? LocationMode.VPS_Geospatial 
                : LocationMode.GPS_FilteredCompass;
            SwitchLocationMode(nextMode);
        }

        // -------------------------------------------------------------------------
        // 3. 左上: デバッグステータス表示（ボタン・上部パネルと被らない配置）
        // -------------------------------------------------------------------------
        GUIStyle labelStyle = new GUIStyle
        {
            fontSize = 22,
            normal = { textColor = Color.yellow }
        };

        ILocationProvider provider = ActiveLocationProvider;
        string trackingText = (provider != null) ? provider.TrackingStatusText : "プロバイダー未設定";
        double curLat = (provider != null) ? provider.CurrentLatitude : 0.0;
        double curLon = (provider != null) ? provider.CurrentLongitude : 0.0;
        float offset = (provider != null) ? provider.HeadingOffset : 0f;

        string distStr = (nearestDistance >= 0f) ? $"{nearestDistance:F1}m" : "計算不可";
        int loadedTowerCount = (geoDataManager != null && geoDataManager.LoadedTowers != null) ? geoDataManager.LoadedTowers.Count : 0;
        int loadedLineCount = (geoDataManager != null && geoDataManager.LoadedLines != null) ? geoDataManager.LoadedLines.Count : 0;

        string debugText = $"----------------------------------------\n" +
                          $"[プロバイダー] {trackingText}\n" +
                          $"[現在地] Lat:{curLat:F6}, Lon:{curLon:F6}\n" +
                          $"[AR真北角] {offset:F1}°\n" +
                          $"[GeoJSONデータ] 鉄塔:{loadedTowerCount}基 / 送電線:{loadedLineCount}本\n" +
                          $"[最寄り鉄塔] {nearestName} ({distStr})\n" +
                          $"[画面内描画数] 鉄塔:{activeTowers.Count}基 / 送電線:{activeLines.Count}本 (範囲内:{inRangeLineCount}本)\n" +
                          $"[判定] {spawnBlockedReason}\n" +
                          $"----------------------------------------";

        GUI.Label(new Rect(30, 100, 850, 400), debugText, labelStyle);
    }
}