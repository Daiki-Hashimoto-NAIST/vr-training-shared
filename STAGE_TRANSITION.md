> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# ステージ遷移プロトタイプ

## 現在の体験フロー

`SETUP → READY → COUNTDOWN → TRAINING → MUSCLE UP → TO JUDGES → STAGE ENTRANCE → TO STAGE → POSING → RESULTS`

- 右コントローラー A: 立ち位置と向きを鏡へ合わせる
- 左コントローラー X: HMDの高さからBody Trackingの身長を調整する
- 右コントローラー B: READYから開始する／RESULTSから控室へ戻る
- AとXはSETUP・READY中だけ受け付ける

Aの立ち位置合わせは、HMDの位置・回転がともに追跡中の場合だけ実行する。
追跡が無効なら`TrackingSpace`を動かさず、未完了のまま`Aでもう一度`を案内する。
明示的なDesktop Simulationだけは、XR機器の代わりにDesktop Cameraの姿勢を
使う。

Aの成功時は右手、Xの成功時は左手へ0.06秒の弱い振動と短い確認音を一度だけ
返す。両方が揃ってReadyになった時は開始可能を示す上昇音を一度だけ返す。
失敗した校正、追跡不良、ボタン連打には成功音や成功振動を出さない。Avatar切替で
校正が無効化された時は状態を戻し、次の成功を新しい一回として扱う。

Xの身長校正はFloor Level基準のHMD眼高へ頭頂までの仮値`0.11 m`を加え、
Body Trackingへ渡す。立位として受け付ける眼高は`1.0～2.15 m`、
最終身長は`1.2～2.3 m`へ制限している。これらは
`HeightCalibrationController`の安全ガードである。頭頂補正`0.11 m`と
HMD計測を使わない場合の身長は`Player/HeightCalibrationSystem`のInspectorで
調整できるが、受付範囲を変える場合は安全性を確認したうえでコード変更する。

Metaサンプル由来の左側校正メニューは視覚子だけを無効にしている。
`UI/BodyTrackingCalibrationMenu`のrootと
`HMDRemountRestartTracking`は有効なままなので、HMD再装着時の
Body Tracking復旧は維持される。

## ステージ構成

- `Phase_Posing/Stage`
  - 床、背景、左右の袖、発光アクセント、2灯のスポットライト
  - `StageArrivalPoint`: ステージ上でHMDを合わせる地点
- `Phase_Posing/Audience`
  - 仮の観客席床、5段55体の低負荷な観客、最前列の審査員
- `Phase_Posing/PoseHUD/StageFlowHUD`
  - ステージ上のポーズ指示と結果表示
- `Phase_Posing/Stage/PrototypeStageVisuals/PoseMonitor`
  - 本人アバターと小型のお手本を同時に確認するステージモニター

ステージ中心は world `(0, 0, 15)`、床上面と到着地点は `y=0`。控室から十分離しつつ、座標は調整しやすい値に固定している。

## 移動の設計

`StageTransitionController` はフェードで全黒にしてから、`OVRCameraRig.TrackingSpace` だけを移動する。

- `Player`
- `TrackedAvatarAnchor`
- `MirrorAvatarAnchor`
- アバター本体
- Camera Rigのroot

上記は動かさない。Movement SDKが毎フレームTrackingSpaceを基準に骨格をworld座標へ変換するため、両方を動かすとアバターへ移動量が二重に加算される可能性がある。

HMDの現在の物理オフセットを残したまま、到着地点へXZ位置と水平Yawを合わせる。Yは変更しない。ステージ移動は身長やT-poseの校正ではないため、`CharacterRetargeter.Calibrate()` は呼ばない。

全黒になった移動フレームでも、HMDの位置・回転追跡をもう一度確認する。
追跡が無効なら目的地への整列を拒否し、画面が黒い間に保存済みの控室姿勢へ
復旧してから通常の失敗処理へ戻す。古いCenter Eye姿勢を使った移動は行わない。

RESULTSでBを押した場合も、同じフェードを使って現在のHMDを `PlayerCalibrationPoint` へ絶対整列する。差分を加算しないため、複数周しても位置ずれが累積しない。

## 入場・ポージングBGM

`Systems/StageMusic` の `StageMusicController` は、審査員席への暗転移動から
Stage Entrance、ステージへの暗転移動、Posingまで、同じ低音量の2D BGMを継続する。
ポーズ成功音と観客歓声のAudioSourceは共有せず、BGMは採点タイミングを決めない。

- 審査員席への `StageTransition` で短くFade Inする
- `StageEntrance → StageTransition → Posing` で曲位置を維持する
- Posing中のTracking待ちは停止や失敗音にせず、30%まで短く音量を下げる
- ポーズ確定音または観客歓声中だけBGMを40%へ下げ、主反応を聞き取りやすくする
- Resultsへ入る時は0.25秒で消し、最終歓声と競合させない
- Resultsから控室へ戻る遷移では鳴らさない
- 正式Clip未設定時は、Setup中に12秒の静かな生成仮音を一度だけ作る

正式素材は `Stage_Bed.wav` を想定する。100 BPM前後、4/4、24小節程度
（100 BPMなら57.6秒）の完全ループ、48 kHz PCM WAV、16または24 bitとする。
声、強いキック、派手なフィル、長い末尾残響を避け、入場の期待感は出しつつ、
Posing中のお手本、成功音、観客反応、スタッフの声を邪魔しない密度にする。
ファイル先頭を第1拍にし、カウントインと先頭無音を含めない。CC0、自作、または
再配布可能なライセンスに限定し、出典、作者、条件をThirdParty資料へ記録する。

遷移の多重開始やタイムアウトが起きた場合も、現在の暗転量からいったん全黒まで進めてから控室へ復旧する。位置を先に戻さないため、異常復旧時にも可視状態での瞬間移動を避ける。

## 実機確認

Quest Linkを接続した状態で次を確認する。

1. A、X、Bの順で開始し、トレーニング終了後に画面が完全に暗転する
2. 暗転中に一度だけ移動し、ステージ上で観客方向を向いて復帰する
3. アバターの頭・手とHMD・コントローラーの相対位置が控室と同じ
4. ステージ右前方のHUDが無理なく視界に入る
5. POSING終了後、Bで暗転して控室へ戻る
6. 2周以上繰り返しても立ち位置とYawがずれない
7. 遷移中にA/Xを押しても再校正されない
8. Metaサンプルの左側メニューは表示されず、HMDを外して再装着した後も
   Body Trackingが復帰する
9. HMD追跡を失った状態でAを押しても位置が動かず、復帰後に再試行できる
10. 暗転中にHMD追跡を失っても目的地へ移動せず、可視テレポや黒画面停止に
    ならない
11. 審査員席への暗転でBGMが自然に始まり、入場から3ポーズまで曲が先頭へ
    戻ったり二重再生されたりしない
12. PosingでTrackingを失うとBGMだけが穏やかに小さくなり、復帰すると元へ戻る
13. ResultsでBGMが消え、ポーズ成功音や最終歓声を聞き分けられる
14. ポーズ確定音と観客歓声の間だけBGMが自然に下がり、音が終わると元へ戻る

PC上の座標テストでは、`控室 → ステージ → 控室` を2周させ、各到着時のXZとYawが一致し、PlayerとAvatarAnchorがworld原点から動かないことを確認済み。両眼フェードと実際のアバター追従はQuest実機で最終確認する。
