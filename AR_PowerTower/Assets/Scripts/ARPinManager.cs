using System.Collections.Generic;
using UnityEngine;

public class ARPinManager : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private GeoDataManager geoDataManager;
    [SerializeField] private LocationManager locationManager;
    
    [Header("3Dピン設定")]
    [SerializeField] private GameObject pinPrefab;
    [SerializeField] private float maxRadiusMeters = 300f; // 周囲300m以内を対象

    private List<GameObject> spawnedPins = new List<GameObject>();
    private float nearestDistance = -1f;
    private string nearestName = "なし";

    void Start()
    {
        InvokeRepeating(nameof(UpdatePins), 1.0f, 1.5f);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            UpdatePins();
        }
    }

    public void UpdatePins()
    {
        ClearPins();

        if (geoDataManager == null || locationManager == null || !locationManager.IsLocationReady)
        {
            return;
        }

        double userLat = locationManager.CurrentLatitude;
        double userLon = locationManager.CurrentLongitude;
        float heading = locationManager.CurrentHeading;

        Quaternion compassRotation = Quaternion.Euler(0, -heading, 0);

        // ★現在のARカメラのワールド位置を取得（カメラ位置基準で配置）
        Vector3 cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        float minDistance = float.MaxValue;
        string minName = "なし";

        foreach (var pt in geoDataManager.LoadedPoints)
        {
            float distance = CoordinateConverter.CalculateDistance(userLat, userLon, pt.Latitude, pt.Longitude);

            // 一番近いPointの距離と名前を記録
            if (distance < minDistance)
            {
                minDistance = distance;
                minName = pt.Name;
            }

            if (distance <= maxRadiusMeters)
            {
                // 北基準相対座標
                Vector3 relativePos = CoordinateConverter.LatLonToUnityPosition(userLat, userLon, pt.Latitude, pt.Longitude);

                // コンパス回転適用
                Vector3 rotatedPos = compassRotation * relativePos;

                // ★カメラの位置を原点としたワールド座標に変換（高さYはカメラ目線）
                Vector3 worldTargetPos = cameraPos + rotatedPos;
                worldTargetPos.y = cameraPos.y; // カメラと同じ高さ（目線）に浮かす

                // 3Dピンを生成
                GameObject newPin = Instantiate(pinPrefab, worldTargetPos, Quaternion.identity);
                newPin.name = $"Pin_{pt.Name}";

                // ★遠くからでも超目立つようにピンを拡大 (3m × 3m × 3m の巨大ピン)
                newPin.transform.localScale = new Vector3(3.0f, 3.0f, 3.0f);

                spawnedPins.Add(newPin);
            }
        }

        nearestDistance = (minDistance == float.MaxValue) ? -1f : minDistance;
        nearestName = minName;
    }

    private void ClearPins()
    {
        foreach (var pin in spawnedPins)
        {
            if (pin != null)
            {
                Destroy(pin);
            }
        }
        spawnedPins.Clear();
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 32;
        style.normal.textColor = Color.yellow;

        int dbCount = (geoDataManager != null) ? geoDataManager.LoadedPoints.Count : 0;
        string nearestText = (nearestDistance >= 0) ? $"{nearestName} ({nearestDistance:F1}m)" : "なし";

        string statusText = $"[GPS状態] {(locationManager.IsLocationReady ? "測位完了" : "測位中...")}\n" +
                           $"[現在地] 緯度:{locationManager.CurrentLatitude:F5}, 経度:{locationManager.CurrentLongitude:F5}\n" +
                           $"[方角] {locationManager.CurrentHeading:F1}°\n" +
                           $"[DB保持件数] {dbCount}件\n" +
                           $"[最寄りのスポット] {nearestText}\n" +
                           $"[描画ピン数] {spawnedPins.Count}個 (半径{maxRadiusMeters}m内)\n" +
                           $"※画面タップで更新";

        GUI.Label(new Rect(30, 40, 850, 450), statusText, style);
    }
}