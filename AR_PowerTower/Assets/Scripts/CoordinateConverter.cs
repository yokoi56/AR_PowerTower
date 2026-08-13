using System;
using UnityEngine;

public static class CoordinateConverter
{
    private const double EarthRadiusMeters = 6371000.0; // 地球の平均半径（m）

    /// <summary>
    /// ハバーサイン（Haversine）公式による2点間の正確な大圏距離（メートル）
    /// </summary>
    public static float CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return (float)(EarthRadiusMeters * c);
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// 現在地に対するターゲット地点のメートル単位相対位置
    /// </summary>
    public static Vector3 LatLonToUnityPosition(double userLat, double userLon, double targetLat, double targetLon)
    {
        double deltaLat = targetLat - userLat;
        double z = deltaLat * 111320.0;

        double deltaLon = targetLon - userLon;
        double userLatRad = ToRadians(userLat);
        double x = deltaLon * 111320.0 * Math.Cos(userLatRad);

        return new Vector3((float)x, 0f, (float)z);
    }
}