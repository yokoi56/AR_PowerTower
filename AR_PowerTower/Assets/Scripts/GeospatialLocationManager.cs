using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class GeospatialLocationManager : MonoBehaviour, ILocationProvider
{
    [Header("AR Foundation / ARCore Extensions")]
    [SerializeField] private AREarthManager earthManager;

    [Header("PCエディタ用モック設定")]
    [SerializeField] private bool useMockInEditor = true;
    [SerializeField] private double mockLatitude = 35.704432;
    [SerializeField] private double mockLongitude = 139.646847;
    [SerializeField] private double mockAltitude = 50.0;
    [SerializeField] private double mockHeading = 0.0;

    public bool IsTrackingReady { get; private set; }
    public float HeadingOffset { get; private set; } = 0f;

    public double CurrentLatitude { get; private set; }
    public double CurrentLongitude { get; private set; }
    public double CurrentAltitude { get; private set; }
    public double CurrentHeading { get; private set; }
    public double CurrentHorizontalAccuracy { get; private set; }
    public double CurrentYawAccuracy { get; private set; }
    public string TrackingStatusText { get; private set; } = "位置情報権限の確認中...";

    private void Awake()
    {
        if (earthManager == null) earthManager = FindFirstObjectByType<AREarthManager>();
        // 【修正】カメラストリームを壊すため、コードから enabled = false や Config のトグル操作は行わない
    }

    private void Start()
    {
#if UNITY_EDITOR
        if (useMockInEditor)
        {
            IsTrackingReady = true;
            TrackingStatusText = "[Editor Mock VPS] 測位完了";
            return;
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(RequestLocationPermission());
#endif
    }

    private IEnumerator RequestLocationPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            TrackingStatusText = "[ポップアップ表示中] 位置情報権限を許可してください";
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1.5f);
        }
#endif
        TrackingStatusText = "VPS初期化待ち...";
        yield return null;
    }

    void Update()
    {
#if UNITY_EDITOR
        if (useMockInEditor)
        {
            CurrentLatitude = mockLatitude;
            CurrentLongitude = mockLongitude;
            CurrentAltitude = mockAltitude;
            CurrentHeading = mockHeading;

            Transform mainCamMock = Camera.main != null ? Camera.main.transform : null;
            float camYawMock = mainCamMock != null ? mainCamMock.eulerAngles.y : 0f;
            HeadingOffset = camYawMock - (float)CurrentHeading;

            IsTrackingReady = true;
            return;
        }
#endif

        if (earthManager == null)
        {
            TrackingStatusText = "【エラー】AREarthManager が未設定です";
            IsTrackingReady = false;
            return;
        }

        // 1. EarthState の観測（ErrorEarthNotReady 中もカメラ背景は正常に描画され続ける）
        var earthState = earthManager.EarthState;
        if (earthState != EarthState.Enabled)
        {
            TrackingStatusText = $"VPS初期化中... (EarthState: {earthState})";
            IsTrackingReady = false;
            return;
        }

        // 2. EarthTrackingState の観測
        var trackingState = earthManager.EarthTrackingState;
        if (trackingState != TrackingState.Tracking)
        {
            TrackingStatusText = $"VPSカメラ照合中 (周りにかざしてください) [{trackingState}]";
            IsTrackingReady = false;
            return;
        }

        // 3. 測位成功
        GeospatialPose pose = earthManager.CameraGeospatialPose;

        CurrentLatitude = pose.Latitude;
        CurrentLongitude = pose.Longitude;
        CurrentAltitude = pose.Altitude;
        CurrentHeading = pose.EunRotation.eulerAngles.y;
        CurrentHorizontalAccuracy = pose.HorizontalAccuracy;
        CurrentYawAccuracy = pose.OrientationYawAccuracy;

        // AR空間の真北角度(HeadingOffset)の算出
        Transform mainCam = Camera.main != null ? Camera.main.transform : null;
        float cameraYaw = mainCam != null ? mainCam.eulerAngles.y : 0f;
        HeadingOffset = cameraYaw - (float)CurrentHeading;

        IsTrackingReady = true;
        TrackingStatusText = $"VPS測位中 (位置誤差:±{pose.HorizontalAccuracy:F1}m, 方位誤差:±{pose.OrientationYawAccuracy:F1}°)";
    }

    public void SetMockCoordinates(double lat, double lon, double alt)
    {
        mockLatitude = lat;
        mockLongitude = lon;
        mockAltitude = alt;
        CurrentLatitude = lat;
        CurrentLongitude = lon;
        CurrentAltitude = alt;
    }
}