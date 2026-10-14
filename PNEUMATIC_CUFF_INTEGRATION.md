> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# 空気圧カフ連携と安全境界

## 結論

Unity側には、既存ESP32ファームウェアの `T`（排気）/ `F`（加圧）を
変更せずに使う通信経路を用意している。ただし、現在のファームウェアは
500 msの無通信を `F` 相当として扱い、ACKや圧力テレメトリも返さない。
したがってUnity、Quest、Wi-Fiの改善だけで人体装着時の安全は保証できない。

実機の `F` は、独立した常時排気側の機構、手動排気/E-stop、独立圧力上限が
確認され、承認済み時間がInspectorへ明示入力されるまでコード上で禁止する。
既定設定は通信も加圧も無効であり、従来どおり `SafeMockPneumaticCuffOutput`
だけを使用する。

## 指令経路

```text
TrainingRhythmScorer（左右をまとめた1回の運動結果）
  -> PneumaticCuffController（Phase、追跡、Arm、Heartbeat、Latch）
  -> 選択されたIPneumaticCuffOutput 1個
  -> UdpPneumaticCuffOutput
  -> QuestLocalWifiUdpBridge（AndroidのESP32 Wi-Fi経路へsocketを固定）
  -> ESP32 192.168.4.1:5000
```

`DemoCuffRepDriver` は製品シーンの指令経路に使わない。安全Controllerを迂回し、
左右イベントを重複処理し、Training中の長時間加圧を生むためである。

## ゲームとの対応

- 通常時は必ず `T`。
- `TrainingRhythmScorer.RepTimingResultReady` が1つのチャート運動の完了を
  確定した時だけ、有限時間の `F` パルス候補を生成する。
- カールとレイズは同じ共有カフ回路へ同じルールで通知する。
- 得点や `GrowthNormalized` は使わない。これらは単調増加するため、二値の
  ESP32へ渡すと最初の得点後に連続 `F` になってしまう。
- 重複イベントや、既に進行中のパルスで終了時刻を延長しない。
- パルス終了、Phase変更、Tracking停止、入力系停止、Link変更、Heartbeat切れ、
  Pause、Focus喪失、Controller無効化では直ちにパルスを破棄して `T` を要求する。
- 復旧後に古いパルスを再開せず、次の新しい完了運動を必要とする。
- `_repBuildPulseSeconds` の既定値は0。これは未調整ではなく、未承認のため
  意図的に加圧を禁止している値である。
- `_maximumNormalizedRequest` は現在の二値ファームウェアでは強度にならない。
  0は加圧禁止、0より大きい値はすべて `F` 候補である。

## Controllerの出力選択

- `_useLiveOutput == false`：既存の `_mockOutputs` を使用する。
- `_useLiveOutput == true`：`_outputOverride` の1出力だけを使用する。
- Overrideがnull、または `IPneumaticCuffOutput` でない場合は構成エラーとし、
  Mockへ黙ってフォールバックしない。
- `_mockOutputs`、`_autoArmMockOutputsInReady`、`_activePhases`、
  `_maximumNormalizedRequest` は既存Scene/Prefab参照を保つため改名・型変更しない。
- Ready時の自動ArmはMock専用。実出力では常に無効であり、Ready中に公開メソッド
  `TryArm()` を承認済みのオペレーター操作から明示的に呼ぶ必要がある。
- 実出力は `IPneumaticCuffBuildAuthorization` を実装し、承認されていない場合は
  `RealOutputBlocked` となる。排気要求はこの状態でも許可する。

## QuestのWi-Fi/UDP

Androidでは `WifiNetworkSpecifier` で既存ファームウェアのSSID
`InstantMuscle`（既存パスフレーズを使用）をローカル専用Wi-Fiとして要求する。
返された `Network` にESP32用 `DatagramSocket` だけを `bindSocket` し、Unityや
Metaの他通信をプロセス全体でESP32側へ切り替えない。

必要なAndroid権限はManifestに宣言し、Android 13以降では
`NEARBY_WIFI_DEVICES`、Android 12では `ACCESS_COARSE_LOCATION` と
`ACCESS_FINE_LOCATION` を同時に実行時要求する。
最初の接続ではOSのWi-Fi同意画面が表示される可能性があるため、Armより前に
許可と経路確立を完了させる。

従来どおり100 ms周期で同じ意図を再送する。さらにJava側はUnityからの更新が
250 ms途絶えた場合、`F` を再送せず `T` へラッチして100 ms周期で排気を試みる。
復帰後はUnityから新しい `T` が届くまで `F` を拒否する。これはUnity main
threadの一時停止には有効だが、アプリ終了、Quest電源断、Wi-Fi断では送信できない。

一時的な経路消失（`Lost` / `Faulted`）だけを1、2、4、8秒間隔で再要求する。
OSのWi-Fi選択が拒否・取消された `Unavailable` では、同意画面を繰り返さないよう
自動再試行を停止する。この場合はcomponentまたはアプリを明示的に再起動する。
再接続後は以前の `F` を引き継がず、必ず `T` と後述の解除ドウェルから始める。

表示上の `ROUTE READY` は、Androidが経路とローカルsocketを用意した意味だけで
ある。ESP32は応答しないため、`DEVICE UNVERIFIED` は常に残り、接続済み・受信済み・
物理解放済みとは表示しない。

## UDP出力の二重安全ゲート

`UdpPneumaticCuffOutput` は実出力なので `_transmitEnabled` がfalseでも
`IsMock == false` である。

`F` を送るには少なくとも次の全条件が必要である。

1. `_transmitEnabled == true`
2. Wi-Fi経路とUDP socketがReady
3. `_independentHardwareSafetyConfirmed == true`
4. `_approvedMaximumContinuousBuildSeconds > 0` かつ有限
5. ControllerがReady中に明示Arm済み
6. Controllerで新しいHeartbeatを観測済み
7. Training中で追跡・得点・Rep入力が有効
8. 完了運動から生成された未期限切れパルス中
9. 直前の起動・再接続・`F` 後に、ローカル送信成功した `T` を複数回かつ
   ファームウェア無通信判定と同じ500 ms以上継続済み

UDP層はControllerのパルスとは別に連続 `F` 開始時刻を保持する。承認済み連続
加圧上限へ達すると `T` へ変更し、Release後の解除ドウェルが終わるまで `F` を
拒否する。Controller設定ミスでパルスが長すぎても、この上限を越えてUnityから
`F` を継続しない。

解除ドウェルは1回のローカル送信成功だけでロックを解除せず、100 ms周期の `T` を
複数回、500 ms以上継続してから次の `F` 候補を許可する。ただしESP32からACKが
返らないため、これはパケット受信や物理排気を証明するものではない。連続加圧上限と
解除ドウェルはいずれもbest-effortのソフトウェア制限であり、独立ハードウェアを
置き換えない。

## 安全なScene既定値

`Assets/Scenes/InstantMuscle_proto.unity` の `Systems/PressureSystem` には、
Mock、Controller、UDP adapterを同居させられる。ただし既定値は次を維持する。

- Controller `_useLiveOutput = false`
- Controller `_repBuildPulseSeconds = 0`
- UDP `_transmitEnabled = false`
- UDP `_independentHardwareSafetyConfirmed = false`
- UDP `_approvedMaximumContinuousBuildSeconds = 0`
- 製品Sceneに `DemoCuffRepDriver` を置かない

この状態では従来どおりNo-Air Mockだけが動き、ソケットも `F` も発生しない。

## 実機ベンチ試験を有効にする順序

人体へ装着せず、排気を目視・計測できるベンチでのみ行う。

1. 独立排気、手動E-stop、独立圧力上限をハードウェア責任者が確認する。
2. `PressureSystem > UdpPneumaticCuffOutput` で `_transmitEnabled` を有効にし、
   まず他の安全値をfalse/0のままにして `T` だけが100 ms周期で届くことを確認する。
3. Wi-Fi拒否、Wi-Fi断、アプリPause、Unity main thread停止を試し、可能な状態では
   Java watchdogが `T` へ倒れること、拒否後にOS同意画面が自動で反復しないことを
   パケットと実回路の両方で確認する。
4. 承認値だけを `_approvedMaximumContinuousBuildSeconds` と
   Controller `_repBuildPulseSeconds` へ入力する。値をゲーム上の感覚で決めない。
5. `_independentHardwareSafetyConfirmed` を有効にする。
6. Controller `_outputOverride` に同じUDP componentを指定し、
   `_useLiveOutput` を有効にする。
7. Ready中に、参加者のゲーム開始入力とは分離された承認済みオペレーター操作から
   `PneumaticCuffController.TryArm()` を1回だけ呼ぶ。
8. 最初はカフを人体から外し、1回の完了運動につき1パルス、期限後の `T`、
   重複非延長、Phase/Tracking/Link異常時のLatchを確認する。

非zero時間、独立ハードウェア、HMD上のオペレーターArm経路が承認されるまでは、
人体装着した加圧試験へ進めない。

## 検証で区別するもの

- `HeartbeatSequence`：ローカル送信呼び出しが成功した回数。ESP32のACKではない。
- `LinkState.Ready`：OSの経路/socketがReady。ESP32受信の証明ではない。
- `LastIntent`：Unityが最後に要求した抽象意図。測定圧力ではない。
- `T`送信成功：OSがdatagramを受理した事実。物理的な排気完了ではない。
- `解除ドウェル完了`：複数の `T` を500 ms以上ローカル送信できた事実。
  ESP32受信や圧力低下の証明ではない。

実機受信・圧力・物理解放をUnityだけで確認するには、ESP32側ACK/telemetryが必要だが、
今回はマイコン処理を変更しないため対象外とする。
