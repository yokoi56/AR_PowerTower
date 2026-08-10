using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

public class GeoDataManager : MonoBehaviour
{
    // Resources フォルダ内のファイル名（※ .json 拡張子は省いた名前を指定）
    [SerializeField] private string resourceFileName = "kanto_points";

    public class LocationPoint
    {
        public string Name;
        public double Latitude;
        public double Longitude;
    }

    public List<LocationPoint> LoadedPoints { get; private set; } = new List<LocationPoint>();

    void Start()
    {
        LoadGeoJson();
    }

    void LoadGeoJson()
    {
        // Unityの Resources フォルダからテキストアセットとしてロード（PC / Android 共通対応）
        TextAsset geoJsonAsset = Resources.Load<TextAsset>(resourceFileName);

        if (geoJsonAsset == null)
        {
            Debug.LogError($"【エラー】Resources フォルダ内に '{resourceFileName}.json' が見つかりません。");
            return;
        }

        try
        {
            string jsonText = geoJsonAsset.text;
            JObject geoJson = JObject.Parse(jsonText);
            JArray features = geoJson["features"] as JArray;

            if (features != null)
            {
                foreach (var feature in features)
                {
                    string geomType = feature["geometry"]?["type"]?.ToString();
                    if (geomType == "Point")
                    {
                        JArray coordinates = feature["geometry"]?["coordinates"] as JArray;
                        if (coordinates != null && coordinates.Count >= 2)
                        {
                            double lng = (double)coordinates[0];
                            double lat = (double)coordinates[1];
                            string name = feature["properties"]?["name"]?.ToString() ?? "名称未設定";

                            LoadedPoints.Add(new LocationPoint
                            {
                                Name = name,
                                Latitude = lat,
                                Longitude = lng
                            });
                        }
                    }
                }
            }

            Debug.Log($"<color=green>【GeoJSON成功】 {LoadedPoints.Count} 件のPointデータを正常に読み込みました！</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"【GeoJSON解析エラー】: {e.Message}");
        }
    }
}