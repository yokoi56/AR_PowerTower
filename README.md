# AR_PowerTower

---

# ロケーションベースARアプリ（Phase 1 プロトタイプ）開発仕様書

## 1. プロジェクト概要・成果
OpenStreetMap（OSM）の地点（Point）データを活用し、スマートフォンのカメラ映像（AR空間）内の実際の地理位置（緯度・経度・方角）に対応する場所へ3Dピンを動的に生成・描画するAndroid用ARアプリケーション。

* **達成成果**: OSMのGeoJSONデータの読み込み、リアルタイムGPS/電子コンパス連携、平面座標変換、AR Foundationによるカメラ背景描画、カメラ位置補正付き3Dピン配置、リアルタイムデバッグUI表示の一連のパイプラインを完全実装。

---

## 2. 要件定義（確定版）

### 2.1. 機能要件
1. **ローカルGeoJSON解析**: `Resources` フォルダ内のGeoJSONファイルを読み込み、地点名称・緯度・経度をメモリ上にロードする。
2. **位置・方位情報取得**: AndroidのGPSから現在地（緯度・経度）を取得し、電子コンパスから真北に対する端末の向き（Heading）を取得する。
3. **動的フィルタリング**: 現在地から指定半径（例: 半径300m〜500m以内）に存在するPointのみを検索・抽出する。
4. **平面座標変換（極座標 ➔ 3D直交座標）**: 緯度経度の差分から、メートル単位の相対距離 (X: 東西, Z: 南北) を算出する。
5. **コンパス連動回転**: 端末の方角に合わせて相対座標をY軸回転させ、カメラから見た正しい方位に変換する。
6. **AR空間への3Dピン描画**: 現在のARカメラ位置を基準に、目線の高さ（Y軸）へ3Dピンオブジェクトを生成・表示する。
7. **リアルタイム更新**: 1.5秒周期の自動更新および画面タップ時の手動更新に対応。
8. **デバッグ画面（OnGUI Overlay）**: GPS測位状態、現在地、方位、DB保持件数、最寄りスポット名・距離、描画ピン数を黄色のテキストでオーバーレイ表示する。
9. **PCエディタ用モック機能**: Unity Editor上ではダミー座標・ダミー方角を使って室内でテスト可能。

### 2.2. 非機能要件
* **動作環境**: Android 10.0 (API Level 29) 以上、Google ARCore対応端末。
* **精度**: 平面近似モデル（数メートル〜十数メートルのGPS誤差は許容）。

### 2.3. スコープ外（やらないこと・今後の課題）
* Web API（Overpass API等）からのリアルタイム通信取得（現在はオフラインGeoJSONのみ）。
* オクルージョン処理（現実の建物・遮蔽物の裏にピンを隠す処理）。
* 高度・標高（DEM）データへの追従（現在はカメラ視界の目線高さ一律配置）。
* VPS（Visual Positioning System）等によるセンチメートル級の精密位置合わせ。
* ピンタップ時の詳細UI表示。

---

## 3. 基本設計（アーキテクチャ）

### 3.1. クラス・コンポーネント構成図

```text
[ AR Session / AR Camera ] (AR Foundation)
            │
            ▼
┌────────────────────────────────────────────────────────┐
│ LocationManager (位置情報・方位管理)                    │
│  - Android実行時権限(FineLocation)の要求                │
│  - GPS/コンパスの起動・値保持                            │
│  - PCエディタ用モック位置機能                            │
└───────────────────┬────────────────────────────────────┘
                    │ 現在地 (Lat, Lon, Heading)
                    ▼
┌────────────────────────────────────────────────────────┐
│ GeoDataManager (GeoJSONデータ管理)                      │
│  - Resources.Load による json データパース               │
│  - Newtonsoft.Json による Point リスト保持               │
└───────────────────┬────────────────────────────────────┘
                    │ 抽出された LocationPoint リスト
                    ▼
┌────────────────────────────────────────────────────────┐
│ CoordinateConverter (座標変換ユーティリティ)             │
│  - 緯度経度差分 ➔ メートル単位 (X, Z) への平面近似変換    │
│  - 2点間直線距離（ハバーサイン/平面近似）の計算            │
└───────────────────┬────────────────────────────────────┘
                    │ 相対メートル座標 (X, 0, Z)
                    ▼
┌────────────────────────────────────────────────────────┐
│ ARPinManager (3Dピン描画・ライフサイクル制御)             │
│  - コンパス回転適用 (Quaternion * relativePos)           │
│  - ARCamera位置基準のワールド座標決定 (cameraPos + pos)   │
│  - 3Dピン(PinPrefab)の Instantiate / Destroy            │
│  - OnGUI によるデバッグテキスト表示                       │
└────────────────────────────────────────────────────────┘
```

---

## 4. 詳細設計（実装仕様）

### 4.1. 技術スタック・使用パッケージ
* **Engine**: Unity 6 (6000.3.x LTS) / Universal Render Pipeline (URP)
* **IDE**: Visual Studio Code (Microsoft Unity Extension)
* **Target OS**: Android (Minimum API Level: 29 / Android 10.0)
* **Packages**:
  * `com.unity.xr.arfoundation` (AR基本機能)
  * `com.unity.xr.arcore` (Android用ARエンジン)
  * `com.unity.nuget.newtonsoft-json` (GeoJSONパース)
* **Settings**:
  * Active Input Handling: `Both`
  * Graphics API: `OpenGLES3` (Vulkan除外)

### 4.2. アルゴリズム（座標・方位変換）

1. **緯度経度からメートルへの変換（平面近似）**:
   * 南北方向 (Z軸): $Z = (\text{TargetLat} - \text{UserLat}) \times 111,320\text{m}$
   * 東西方向 (X軸): $X = (\text{TargetLon} - \text{UserLon}) \times 111,320\text{m} \times \cos(\text{UserLat} \times \frac{\pi}{180})$
2. **コンパス方位の回転適用**:
   * 回転クォータニオン: $Q = \text{Quaternion.Euler}(0, -\text{Heading}, 0)$
   * 回転後相対座標: $V_{\text{rotated}} = Q \times (X, 0, Z)$
3. **ARカメラ相対の位置計算**:
   * ワールド目標座標: $P_{\text{world}} = P_{\text{Camera}} + V_{\text{rotated}}$
   * ※高さ $Y$ は視界に入るよう $P_{\text{world}}.y = P_{\text{Camera}}.y$ と補正。

---

## 5. 開発時のハマりポイント ＆ 実装上の注意点（知見の共有）



### ① Androidでのファイル読み込み（`Resources` 必須）
* **問題**: PC上で動いていた `File.ReadAllText("Assets/Data/...")` は、Androidのビルド後（APK化後）にはファイル参照エラー（非存在扱い）となり、データが0件になる。
* **対策**: GeoJSONファイルは必ず **`Assets/Resources/` フォルダ配下に置き、拡張子を `.json` に変更** した上で、`Resources.Load<TextAsset>("ファイル名")` を使用して読み込むこと。

### ② Android 実行時権限（Runtime Permissions）
* **問題**: Manifestファイルの設定だけでは、Android 10以降でGPS機能がブロックされ「測位中...（0, 0）」のままフリーズする。
* **対策**: C#コード側で `UnityEngine.Android.Permission.RequestUserPermission(Permission.FineLocation)` を明示的に呼んでOSのアクセス許可ダイアログを発生させること。

### ③ ARCoreでの画面真っ暗現象（Vulkan競合 ＆ Renderer Feature）
* **問題**: スマホ実機でアプリを起動するとカメラ映像が反映されず画面が黒く塗りつぶされる。
* **対策**:
  1. `Project Settings` ＞ `Player` ＞ `Graphics APIs` から **`Vulkan` を削除し、`OpenGLES3` のみに固定** する。
  2. 使用中の `UniversalRendererData` に **`AR Background Renderer Feature`** を必ず追加する。

### ④ AR空間でのピン可視性（カメラ位置追従・スケール・Clipping）
* **問題**: ピンは生成されている（`Count > 0`）のに、画面上に映らない。
* **対策**:
  1. AR Foundationではユーザーの歩行に伴いカメラのワールド座標（$P_{\text{Camera}}$）が移動するため、ピンの配置位置は必ず **`Camera.main.transform.position + 相対座標`** で算出すること。
  2. 10m〜100m以上離れたオブジェクトは画面上で数ピクセル以下になるため、ピンの `localScale` を **3m〜5m規模に巨大化** させ、配置高さ（Y軸）を **カメラの目線高さ** に浮かせると劇的に見やすくなる。
  3. `Main Camera` の **`Clipping Planes -> Far` を `2000m`** 等に拡張し、描画カリング（消去）を防止する。



---

