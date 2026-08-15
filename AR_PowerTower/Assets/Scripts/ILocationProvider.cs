using UnityEngine;

public interface ILocationProvider
{
    bool IsTrackingReady { get; }
    double CurrentLatitude { get; }
    double CurrentLongitude { get; }
    double CurrentAltitude { get; }
    
    /// <summary>
    /// UnityのARカメラ座標系(+Z軸)から見た真北のオフセット角度(度)
    /// GPSモード時: コンパスフィルターで平滑化された差分角
    /// VPSモード時: 0 (AREarthManagerが自動追従するため)
    /// </summary>
    float HeadingOffset { get; }
    
    /// <summary>
    /// デバッグ表示用ステータステキスト
    /// </summary>
    string TrackingStatusText { get; }
}