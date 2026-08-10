using UnityEngine;

/// <summary>
/// 緯度経度を Unity の 3D 空間座標（メートル単位）に変換するユーティリティクラス
/// </summary>
public static class CoordinateConverter
{
    // 緯度1度あたりの距離（約111,320m）
    private const double MetersPerDegreeLatitude = 111320.0;

    /// <summary>
    /// 現在地 (0,0,0) に対するターゲット地点の相対位置 (X, Z) を計算
    /// X = 東西方向（メートル）、Z = 南北方向（メートル）
    /// </summary>
    public static Vector3 LatLonToUnityPosition(double userLat, double userLon, double targetLat, double targetLon)
    {
        // 南北方向（Z軸）の距離計算
        double deltaLat = targetLat - userLat;
        double z = deltaLat * MetersPerDegreeLatitude;

        // 東西方向（X軸）の距離計算（緯度による経度間隔の補正 cos(緯度)）
        double deltaLon = targetLon - userLon;
        double userLatRad = userLat * System.Math.PI / 180.0;
        double x = deltaLon * MetersPerDegreeLatitude * System.Math.Cos(userLatRad);

        return new Vector3((float)x, 0f, (float)z);
    }

    /// <summary>
    /// 2点間の直線距離（メートル）を計算
    /// </summary>
    public static float CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        Vector3 relativePos = LatLonToUnityPosition(lat1, lon1, lat2, lon2);
        return relativePos.magnitude; // ベクトルの長さ（距離）
    }
}