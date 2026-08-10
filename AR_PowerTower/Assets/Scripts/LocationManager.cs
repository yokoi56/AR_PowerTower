using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android; // Android用権限ライブラリ
#endif

public class LocationManager : MonoBehaviour
{
    [Header("PCテスト用（モック）位置設定")]
    [SerializeField] private bool useMockLocation = true;
    [SerializeField] private double mockLatitude = 35.681236;
    [SerializeField] private double mockLongitude = 139.767125;
    [SerializeField] private float mockHeading = 0f;

    public double CurrentLatitude { get; private set; }
    public double CurrentLongitude { get; private set; }
    public float CurrentHeading { get; private set; }
    public bool IsLocationReady { get; private set; }

    void Awake()
    {
        #if UNITY_EDITOR
        useMockLocation = true;
        #else
        useMockLocation = false; // 実機では自動でリアルGPSモード
        #endif
    }

    void Start()
    {
        if (useMockLocation)
        {
            SetMockLocation();
        }
        else
        {
            StartCoroutine(StartGpsAndCompass());
        }
    }

    void Update()
    {
        if (useMockLocation)
        {
            SetMockLocation();
        }
        else if (Input.location.status == LocationServiceStatus.Running)
        {
            CurrentLatitude = Input.location.lastData.latitude;
            CurrentLongitude = Input.location.lastData.longitude;
            CurrentHeading = Input.compass.trueHeading; // コンパス角度取得
            IsLocationReady = true;
        }
    }

    private void SetMockLocation()
    {
        CurrentLatitude = mockLatitude;
        CurrentLongitude = mockLongitude;
        CurrentHeading = mockHeading;
        IsLocationReady = true;
    }

    private IEnumerator StartGpsAndCompass()
    {
        #if UNITY_ANDROID
        // 1. Android OSへ位置情報アクセス権限（精密位置情報）を明示的に要求する
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1.5f); // 許可ダイアログの表示・操作を待つ
        }
        if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
        {
            Permission.RequestUserPermission(Permission.CoarseLocation);
            yield return new WaitForSeconds(1.0f);
        }
        #endif

        // 2. スマホのGPS設定が有効か確認
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("位置情報サービスが無効化されています。許可を待ちます...");
            yield return new WaitForSeconds(2.0f);
        }

        // 3. GPSと電子コンパスの起動
        Input.location.Start(1f, 1f);
        Input.compass.enabled = true;

        // 4. 測位完了を待つ (最大30秒)
        int maxWait = 30;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait < 1 || Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("GPSの開始に失敗しました: " + Input.location.status);
            yield break;
        }

        IsLocationReady = true;
        Debug.Log("GPS測位が完了しました！");
    }
}