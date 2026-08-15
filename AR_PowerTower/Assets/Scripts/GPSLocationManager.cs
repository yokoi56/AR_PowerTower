using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class GPSLocationManager : MonoBehaviour, ILocationProvider
{
    [Header("コンパスフィルター設定")]
    [Tooltip("コンパス差分角の追従速度(推奨: 1.0~2.0)")]
    [SerializeField] private float filterLerpSpeed = 1.5f;

    [Header("PCエディタ用モック設定")]
    [SerializeField] private bool useMockInEditor = true;
    [SerializeField] private double mockLatitude = 35.704432;
    [SerializeField] private double mockLongitude = 139.646847;
    [SerializeField] private double mockAltitude = 50.0;
    [SerializeField] private float mockHeading = 0f;

    // ILocationProvider 実装
    public bool IsTrackingReady { get; private set; }
    public double CurrentLatitude { get; private set; }
    public double CurrentLongitude { get; private set; }
    public double CurrentAltitude { get; private set; }
    
    // AR空間における真北の角度 (smoothedNorthAngle)
    public float HeadingOffset => smoothedNorthAngle;
    public string TrackingStatusText { get; private set; } = "初期化中...";

    // デバッグ用
    public float RawTrueHeading { get; private set; }
    public float CameraYaw { get; private set; }

    private float smoothedNorthAngle = 0f;
    private bool isCompassInitialized = false;

    private void Awake()
    {
#if UNITY_EDITOR
        if (useMockInEditor)
        {
            CurrentLatitude = mockLatitude;
            CurrentLongitude = mockLongitude;
            CurrentAltitude = mockAltitude;
            IsTrackingReady = true;
            TrackingStatusText = "[Editor Mock GPS] 測位完了";
        }
#endif
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(StartGpsAndCompass());
#endif
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (useMockInEditor)
        {
            CurrentLatitude = mockLatitude;
            CurrentLongitude = mockLongitude;
            CurrentAltitude = mockAltitude;
            RawTrueHeading = mockHeading;

            Transform mainCam = Camera.main != null ? Camera.main.transform : null;
            CameraYaw = mainCam != null ? mainCam.eulerAngles.y : 0f;

            // =========================================================================
            // 【修正前のコード（不具合の原因）】
            // float targetOffset = Mathf.DeltaAngle(CameraYaw, -RawTrueHeading);
            // currentOffsetAngle = Mathf.LerpAngle(currentOffsetAngle, targetOffset, filterLerpSpeed * Time.deltaTime);
            // =========================================================================

            // 【修正後のコード】正解の公式: AR空間の真北 = CameraYaw - RawTrueHeading
            float targetNorthAngle = CameraYaw - RawTrueHeading;

            if (!isCompassInitialized)
            {
                smoothedNorthAngle = targetNorthAngle;
                isCompassInitialized = true;
            }
            else
            {
                smoothedNorthAngle = Mathf.LerpAngle(smoothedNorthAngle, targetNorthAngle, filterLerpSpeed * Time.deltaTime);
            }
            return;
        }
#endif

        if (Input.location.status == LocationServiceStatus.Running)
        {
            CurrentLatitude = Input.location.lastData.latitude;
            CurrentLongitude = Input.location.lastData.longitude;
            CurrentAltitude = Input.location.lastData.altitude;
            IsTrackingReady = true;

            RawTrueHeading = Input.compass.trueHeading;

            // 1. ARカメラの現在のYaw角 (Y軸オイラー角)
            Transform mainCam = Camera.main != null ? Camera.main.transform : null;
            CameraYaw = mainCam != null ? mainCam.eulerAngles.y : 0f;

            // =========================================================================
            // 【修正前のコード（なぜ振り回すと300度狂うかの原因）】
            // float targetOffset = Mathf.DeltaAngle(CameraYaw, -RawTrueHeading);
            // 理由: -RawTrueHeading と CameraYaw の差を取ったことで、スマホを角度 θ 回転させた際に
            // targetOffset が -2θ（2倍速の逆回転）で激しく変動し、180度境界を超えて LerpAngle が
            // 逆走・符号ワープを起こし、回転のたびに位相のねじれが蓄積していました。
            // =========================================================================

            // 【修正後のコード】
            // 正しい公式: AR空間における目標の真北角度 = CameraYaw - RawTrueHeading
            // 理由: スマホを回転(θ)させても CameraYaw(θ) - RawTrueHeading(θ) ＝ 0 となり、
            // スマホの回転運動成分 θ が引き算で相殺消去されます。そのため振り回しても目標角度が揺れず、
            // 磁気コンパスの地磁気ノイズのみが LerpAngle で綺麗に打ち消されます。
            float targetNorthAngle = CameraYaw - RawTrueHeading;

            if (!isCompassInitialized)
            {
                // 初回は即時反映して回転アニメーションの跳ねを防止
                smoothedNorthAngle = targetNorthAngle;
                isCompassInitialized = true;
            }
            else
            {
                // 地磁気ノイズ(ブレ)のみを LerpAngle で緩やかに平滑化
                smoothedNorthAngle = Mathf.LerpAngle(smoothedNorthAngle, targetNorthAngle, filterLerpSpeed * Time.deltaTime);
            }

            TrackingStatusText = $"GPS/コンパス (Raw: {RawTrueHeading:F1}°, AR真北: {smoothedNorthAngle:F1}°)";
        }
        else
        {
            IsTrackingReady = false;
            TrackingStatusText = $"GPS測位待ち... (Status: {Input.location.status})";
        }
    }

    private IEnumerator StartGpsAndCompass()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1.5f);
        }
#endif

        if (!Input.location.isEnabledByUser)
        {
            TrackingStatusText = "位置情報サービスが無効化されています";
            yield break;
        }

        Input.location.Start(1f, 1f);
        Input.compass.enabled = true;

        int maxWait = 30;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait < 1 || Input.location.status == LocationServiceStatus.Failed)
        {
            TrackingStatusText = "GPS起動失敗";
            yield break;
        }

        IsTrackingReady = true;
    }

    public void SetMockLocation(double lat, double lon, double alt, float heading)
    {
        mockLatitude = lat;
        mockLongitude = lon;
        mockAltitude = alt;
        mockHeading = heading;
        CurrentLatitude = lat;
        CurrentLongitude = lon;
        CurrentAltitude = alt;
    }
}