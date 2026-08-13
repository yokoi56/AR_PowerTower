using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class GeospatialLocationManager : MonoBehaviour
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
    public double CurrentLatitude { get; private set; }
    public double CurrentLongitude { get; private set; }
    public double CurrentAltitude { get; private set; }
    public double CurrentHeading { get; private set; }
    public double CurrentHorizontalAccuracy { get; private set; }
    public double CurrentYawAccuracy { get; private set; }
    public string TrackingStatusText { get; private set; } = "初期化中...";

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(RequestLocationPermission());
#endif
    }

    private IEnumerator RequestLocationPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1.0f);
        }
#endif
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
            CurrentHorizontalAccuracy = 0.0;
            CurrentYawAccuracy = 0.0;
            IsTrackingReady = true;
            TrackingStatusText = "[Editor Mock] 測位完了";
            return;
        }
#endif

        if (earthManager == null)
        {
            TrackingStatusText = "AREarthManager が未設定です";
            IsTrackingReady = false;
            return;
        }

        var earthState = earthManager.EarthState;
        if (earthState != EarthState.Enabled)
        {
            TrackingStatusText = $"VPS機能準備中... (EarthState: {earthState})";
            IsTrackingReady = false;
            return;
        }

        var trackingState = earthManager.EarthTrackingState;
        if (trackingState != TrackingState.Tracking)
        {
            TrackingStatusText = $"VPSカメラ照合中... (State: {trackingState})";
            IsTrackingReady = false;
            return;
        }

        // 閾値チェックを行わず、VPSの現在の測位値をダイレクトに出力
        GeospatialPose pose = earthManager.CameraGeospatialPose;

        CurrentLatitude = pose.Latitude;
        CurrentLongitude = pose.Longitude;
        CurrentAltitude = pose.Altitude;
        CurrentHeading = pose.EunRotation.eulerAngles.y;
        CurrentHorizontalAccuracy = pose.HorizontalAccuracy;
        CurrentYawAccuracy = pose.OrientationYawAccuracy;

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