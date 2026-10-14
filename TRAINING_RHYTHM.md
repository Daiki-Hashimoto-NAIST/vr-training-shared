> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# トレーニング回数・リズム採点

## チュートリアルの成功条件

練習の残り回数は `TutorialPracticeJudge` がUP・DOWNと必要な左右の腕を
同じ往復単位で集約し、本番の `TrainingSuccessFeedbackRules.IsRhythmClear`
に成功した場合だけ減る。角度・往復動作の条件は本番と同じ `BodyRepCounter`、
タイミング精度は `TrainingRhythmScorer` の Perfect / Maximum Window Seconds
と `TrainingRhythmMath` を共有する。ガイドの全ボーンとの一致率を別途採点する
処理は本番にもチュートリアルにもなく、ここでいうポーズ判定はRepの角度条件を指す。

練習の拍は `TutorialMusicController` の音声再生位置から取り、音声ループを
またいでも連続する。ガイドと判定は同じ `TrainingRhythmPattern` の最初の
該当種目ブロックを使い、UPを周期の0拍、DOWNをUp To Down Beatsに合わせる。
必要な左右もそのブロックから読むため、Bothなら両腕が必要。

早い・遅い動作には `はやい！` / `おそい！`、指定の動作が判定窓内に
認識されない場合には `ポーズをガイドに合わせて！` を表示する。
動作未検出の場合は速度の原因を断定せず、ガイドへ戻る案内を出す。
成功しなかった試行は回数を減らさず、次の音楽ループで再挑戦できる。
練習は時間やAボタンでは完了せず、各種目3回の成功を待つ。
追跡が失われたら途中の判定を破棄し、復帰後の新しい往復で再開する。
練習の結果は本番のスコア・Accuracy・筋肉成長には加算しない。

## 現在の入力と対象フェーズ

トレーニングでは、Meta Movement SDKが推定してHumanoidへ反映した骨格から
`BodyPoseMetrics` が次の4値を計算する。

- 左右の肘曲げ角度: 腕を伸ばした状態が0度
- 左右の腕上げ角度: 腕を下ろした状態が0度、T-poseが90度、頭上が180度

`BodyRepCounter` は左右・肘曲げ・腕上げを独立した4チャンネルとして処理する。
シーンの `Count Only During Training` は有効で、回数とUP/DOWNイベントを進める
のは `Training` フェーズだけである。Setup、Ready、Countdown、
Transformation、StageTransition、Posing、Resultsでは判定途中の状態を破棄し、
回数を増やさない。

Training開始時に回数とリズム結果を0へ戻し、終了時に回数、平均精度、
タイミング点数、成長量を `GameFlowController` へ保存する。それ以降のHUDと
最終スコアは保存値を使うため、ポージング中の動きで結果は変わらない。

## 初見用の未採点見本

最初の4拍Lead-inと各種目Block間のTransitionを、長い説明画面ではなく
実際の線画をまねる練習に使う。各Blockの最初の採点UPが始まる前に、
線画を次の3拍で一往復だけ表示する。

1. `UP - 3拍`: 下げた姿勢
2. `UP - 2拍`: 上げた姿勢
3. `UP - 1拍`: 下げた姿勢
4. `UP`: 最初の採点対象

HUDはこの間だけ、種目名と `下ろす → 上げる → 下ろす`、
`次の「上げる」から採点`を表示する。`足はその場`と、痛みがあれば腕を下ろして
止まるという安全案内も同じパネルに残す。採点開始後は説明を消し、
`アームカール　今：上げる`または`サイドレイズ　今：下ろす`のように、種目と
次の身体動作を一行で常時示す。線画、現在動作、短い確定結果へ情報を絞る。

見本中は `BodyRepCounter` の種目フィルターを停止し、まねた一往復を結果の
運動回数へ含めない。最後の見本DOWNの拍で対象種目を再び有効にし、
低い開始姿勢を取り込んでから、次の採点UPへ向かう動作を検出する。Chartの拍、
BPM、採点窓、成長量は変更しない。

## 1回とUP/DOWNの判定

1チャンネルの1回は、次の低角度→高角度→低角度で成立する。

1. Start Angle以下を維持して開始準備
2. Target Angle以上を維持した時点で `UP`
3. Start Angle以下へ戻った時点で `DOWN` と回数を1増加

UPとDOWNは別々のタイミング点として採点する。上げる時だけ拍に合わせても
満点にはならず、戻す時も拍に合わせる必要がある。短いノイズで境界を
またいでも数えないよう、各境界を一定時間維持してから確定する。

プレイヤー向け表示では、各タイミング点と1回全体を分けて扱う。

- `上げる / 下ろす + PERFECT / GOOD / OK / MISS`: 各点の確定結果
- `種目名 + 今：上げる / 今：下ろす`: まだ確定していない次の身体動作
- `動きOK + 二の腕 / 肩が少し成長 + 次は拍に合わせる`: 往復は完了したが
  リズム成功ではない時の結果と次の一手
- `上げてから、下ろし切る`: 往復が完了しなかった時の次の身体動作
- `リズム成功 + 二の腕 / 肩が成長`: 同じ譜面NoteのUPとDOWNが両方ともOK以上。
  既存の短いパルス、成功音、振動、実際のBlendShape変化を一つの結果として伝える

`TrainingRhythmScorer.RepTimingResultReady` が、必要な左右を含むUPとDOWNを
同じClock Session・Chart Note単位で集約し、一度だけ通知する。UPを外しても
DOWNまで完了していれば、実際に加算された動作分を「少し成長」と表示しつつ
次の改善を示すが、強い成功演出と「リズム成功」は出さない。動作未完了と
Tracking停止中は成長表示を出さず、Noteも復帰後に継続する。

現在のシーン保存値は次のとおり。

| 判定 | 左 Start / Target | 右 Start / Target |
| --- | --- | --- |
| 肘曲げ | 35 / 105度 | 35 / 105度 |
| 腕上げ | 25 / 75度 | 25 / 75度 |

- Threshold Hold Seconds: `0.08`秒
- Resume Delay Seconds: `0.35`秒
- Minimum Rep Interval Seconds: `0.35`秒
- Require Calibration Ready: 有効
- Count Only During Training: 有効

追跡喪失、A/Xキャリブレーション中、またはキャリブレーション未完了では
判定を停止する。完了済みの回数は残すが、途中だった低→高→低の状態は
リセットする。追跡復帰後は0.35秒安定してから再開し、その間はTrainingの
残り時間と拍も停止する。復帰UIにも `痛みがあれば腕を下ろして止まる` を残し、
追跡を戻すために無理な姿勢を取らせない。

これらは個人差調整前の共通仮値である。左右それぞれを変更できるが、
TargetはStartより最低5度大きくなるよう自動補正される。利用者別の
自動閾値キャリブレーションはまだ実装していない。

## 拍、精度、成長倍率

`RhythmClock` はDSP時刻を使い、現在は120 BPM、4拍子、開始遅延0.1秒で動く。
UPまたはDOWNが発生した時刻と最寄りの拍との差から、タイミング精度を
0〜1で求める。

- Perfect Window Seconds `0.08`: 拍との差が80 ms以内なら精度100%
- Maximum Window Seconds `0.35`: 350 ms以上ずれると精度0%
- その間: 距離に応じて線形に減少
- 表示ラベル: 90%以上 `PERFECT`、65%以上 `GOOD`、35%以上 `OK`、
  それ未満 `MISS`

倍率は精度0%の `0.25` 倍から精度100%の `1.00` 倍まで補間する。
`Growth Per Rep` は `0.04` で、UPとDOWNはそれぞれ半分を受け取るため、
1タイミング点の加算は次の式になる。

`0.04 × 0.5 × タイミング倍率`

UPとDOWNがともにPERFECTなら、1回で成長量が4%増える。動作自体は検出されたが
精度がMISS域だった場合は最小倍率の成長を受け取り、期限切れで動作を検出
できなかった点は成長しない。累積成長量は0〜100%に制限される。

## コントローラー振動

`BeatHaptics` は拍ごとに、接続中の左右Touchコントローラーへ短い振動を送る。

- 通常拍の振幅: `0.35`
- 4拍ごとの先頭拍の振幅: `0.65`
- Frequency: `1.0`
- Duration Seconds: `0.08`
- 動作完了: 振幅 `0.45`、`0.09`秒
- リズム成功: 精度に応じて振幅 `0.55〜0.90`、`0.14`秒

拍、動作完了、リズム成功の全Durationは、安全上`0.01〜0.20`秒へ制限する。
Inspectorや実行時に範囲外・NaN・Infinityが入っても、長時間振動へ変化させない。

先頭拍を強くして小節位置を区別する。RhythmClockは追跡準備が整った
Training中だけ動くため、他フェーズでは新しい振動を出さない。追跡喪失時は
拍を止め、すでに開始済みの短い振動だけが終了する。拍とリズム成功が同時に
発生した場合は成功側を優先し、振動を加算しない。

## 運動を見失った時の適応ヒント

`TrainingRhythmLaneHud` は、追跡が有効なTraining中に同じ種目Blockで
UP / DOWNを2点連続して取り逃した時だけ、既存の線画ガイドを短時間強調する。
主表示は `線画を見て再開`、補助表示は `光る線画に腕を重ねる / 次の動きから、
そのまま続ける` とする。

- 新しいボタン操作や別Tutorialを追加しない
- 採点、譜面時刻、成長量、MISSの確定規則を変更しない
- 既存線画の透明度と発光だけを約2.5秒上げる
- 正しいUPまたはDOWNを検出した時は表示時間を待たず通常表示へ戻す
- 同じBlockでは一度だけ表示し、次の種目Blockで再び有効にする
- 追跡停止、Training終了、Session Resetでは直ちに消し、未入力の連続数も破棄する

補助SEは必須ではない。初見Questテストで視線が線画へ戻らない場合だけ、
`Training_Retry_Hint.wav`（0.12〜0.20秒、柔らかい二音、声・ブザー・鋭い高音なし）
を候補とし、通常MISSへ罰音は加えない。

## Training音フィードバック

`TrainingAudioFeedbackController` は `Systems/ExerciseSystem` の2D AudioSourceを
所有し、次の意味だけを短い一回音へ変換する。

- Countdownの3、2、1: 同じ数字を複数フレーム表示しても各1回
- Training開始: GOを1回
- 1回の動作完了: UPとDOWNが確定した後にneutralな完了音を1回
- リズム成功: 完了音と重ねず、より明るい成功音だけを1回
- 種目切替: 最初のブロックでは鳴らさず、以降の切替ごとに1回

通常拍は既存のコントローラー振動へ任せ、連続するクリック音は追加しない。
MISSと追跡喪失には罰音を出さない。追跡停止、フェーズ終了、Retry、Disableでは
再生中と保留中のTraining音を止める。最後の反復成功と種目切替が重なった場合は、
反復結果を先に鳴らし、切替音を短く遅らせる。

本番AudioClipが未設定のslotには、静かな生成音をPlay開始時だけ作る。これは
動作確認用であり、正式素材は次の短い48 kHz WAVへ差し替える。

| Inspector slot | 推奨ファイル名 | 目安 |
| --- | --- | --- |
| Countdown Tick | `Training_Countdown_Tick.wav` | 0.07〜0.10秒、柔らかい単音 |
| Go | `Training_Go.wav` | 0.15〜0.25秒、上向きの開始合図 |
| Motion Complete | `Training_Motion_Complete.wav` | 0.08〜0.15秒、neutralな確定音 |
| Rhythm Clear | `Training_Rhythm_Clear.wav` | 0.15〜0.25秒、明るい上昇音 |
| Exercise Switch | `Training_Exercise_Switch.wav` | 0.20〜0.35秒、二音程度の切替合図 |

音声素材はCC0、自作、または再配布可能なライセンスに限定し、入手元と条件を
記録する。鋭い高音、大きな低域、長い残響は、HMD内で繰り返す用途には使わない。

## Training BGM

`TrainingMusicController` は `Systems/ExerciseSystem` のBGM専用2D AudioSourceを
所有する。短い意味音を鳴らす `TrainingAudioFeedbackController` とはSourceを共有せず、
`RhythmClock` の最初の拍と同じDSP時間軸へ `PlayScheduled` で合わせる。

- Training開始時だけ再生し、同じClock Sessionで二重に開始しない
- Body Tracking待ちでは拍と一緒に一時停止し、復帰後は同じ曲位置から再開する
- Training終了時は0.2秒でフェードアウトし、Transformationへ持ち越さない
- 通常拍のクリック音は重ねず、既存のコントローラー振動を拍の主提示とする
- GO、動作完了、リズム成功、種目切替の短音中だけBGMを42%へ下げ、短音終了後に戻す
- 正式Clip未設定時は、4小節の静かな生成パルスをSetup中に一度だけ作る

正式素材は次の1曲を最優先で用意する。

| Inspector slot | 推奨ファイル名 | 仕様 |
| --- | --- | --- |
| Training Music Clip | `Training_Bed_120BPM.wav` | 120 BPM、4/4、16小節・32.000秒の完全ループ |

48 kHz PCM WAV、16または24 bit、モノラルまたはステレオ。ファイル先頭を第1拍にし、
カウントイン、先頭無音、末尾残響を含めない。ループ境界でクリックや音量差がなく、
声、強すぎる低域、鋭い高域を避ける。短い成功音、HMD内の案内、スタッフの声を
邪魔しない密度にする。CC0、自作、または再配布可能なライセンスに限定し、出典、
作者、ライセンスをThirdParty資料へ記録する。

## GameFlowの現在時間

主な時間は `Systems/GameFlow` の `Game Flow Controller` で調整する。

- Setup / Ready: 時間制限なし。A、X、B入力を待つ
- Countdown: `3`秒
- Training: 現在は譜面の148拍と完了猶予から約`74.75`秒。追跡待ち中は
  減らない。Game Flow Controllerの`20`秒は譜面を利用できない場合だけのFallback
- Transformation: `3`秒
- StageTransition Fallback: `1.2`秒
- Posing: 3ポーズそれぞれ採点時間 `10`秒 + 準備時間 `3`秒。ルーチン自体は
  最大約`39`秒で終了し、GameFlow側には監視用の`2`秒Marginがある
- Results: 時間制限なし。B入力を待つ

実際のステージ遷移には `Systems/StageTransition` の
Fade Out `0.4`秒、Black Hold `0.15`秒、Fade In `0.6`秒を使う。
Fallbackは遷移コンポーネントを利用できない場合の代替値である。
Posingは必要なHoldを先に達成すると、10秒を待たずResultsへ進む。

## Inspector調整場所

- `Player/BodyRepCounterSystem`
  - 左右のElbow Flexion / Arm Raise Start・Target角度
  - Threshold Hold、Resume Delay、Minimum Rep Interval
  - `Count Only During Training` とキャリブレーション必須設定
- `Systems/GameFlow`
  - `Rhythm Clock`: BPM、拍子、開始遅延
  - `Game Flow Controller`: Countdown、Training、Transformation、
    StageTransition Fallback、Posingの時間
- `Systems/ExerciseSystem`
  - `Training Rhythm Chart`: 見本後に回数検出を再開する
    `Block Preview Release Lead Beats`
  - `Training Rhythm Scorer`: Perfect / Maximum Window、Growth Per Rep、
    Minimum / Perfect Multiplier
  - `Beat Haptics`: 通常拍・先頭拍の振幅、周波数、振動時間
  - `Training Audio Feedback Controller`: 5種のAudioClip、音量、切替音の遅延
  - `Training Music Controller`: 正式BGM、音量、終了Fade、生成仮BGMの有無
- `Phase_Training/TrainingHUD/RhythmPoseGuide`
  - `Rhythm Pose Guide Controller`: `Show Preparation Preview`、線画のLead時間
- `Assets/IVRC/Config/DefaultExperienceScoreProfile.asset`
  - 最終結果で100%扱いにする肘曲げ・腕上げの左右合計目標回数。現在は各32回

閾値を変更する時は、まずStart / Targetを利用者の可動域へ合わせ、その後に
Hold時間とMinimum Rep Intervalを調整する。成長速度だけを変えたい場合は
回数閾値ではなく `Growth Per Rep` を変更する。

## 自動テストで確認していること

- 低→高→低で1回だけ数え、UPとDOWNを1回ずつ通知する
- 境界付近の短いノイズ、最小回数間隔、追跡停止で誤カウントしない
- Training以外の全フェーズで回数判定を進めない
- 各Block開始前の3拍へ未採点のDOWN / UP / DOWN見本を配置し、見本中の
  一往復を回数へ含めず、最初の採点UPへ向かう1拍前には検出を再開する
- 最寄り拍の距離、精度、ラベル、倍率、UP/DOWN半分ずつの成長式
- 実際の `BodyRepCounter` イベントを通した1サイクルで、UP/DOWNを
  別々に採点して設定どおりの成長量になる
- 同じChart NoteのUP/DOWNと必要な左右が揃うまで反復結果を出さず、
  動作完了とリズム成功を別々に一度だけ確定する
- Countdown、GO、反復結果、種目切替の音をRun / Session / Note単位で
  二重発火させず、Reset後は同じNoteを新しいRunとして扱う
- Session resetで途中のUPが次回のDOWNへ混ざらない
- Training終了境界の最後のLateUpdateを結果へ含める

## Quest実機で確認・調整する項目

1. AとXのキャリブレーション後、Bで開始すると3秒後にTrainingへ入る
2. 最初と各種目切替で、線画の下げる→上げる→下げるを一度まねた後、
   次の上げるから採点されることを追加説明なしで理解できる
3. 見本を実際に一往復まねてもResultsの運動回数が増えず、直後の採点動作は
   正常に数えられる
4. Training中だけ左右コントローラーが120 BPMで振動し、4拍ごとの先頭が
   明確だが不快でない強さになっている
5. 腕を低位置から高位置へ動かすとUPが表示され、低位置へ戻した時だけ
   DOWNと回数が増え、確定前から`今：上げる / 下ろす`で次の動作が分かる
6. UPだけ、DOWNだけを拍から外すと、それぞれ独立して精度と倍率が下がる
7. 肘曲げと腕上げ、左右の各チャンネルが意図した動きだけを数え、
   小さな揺れや境界付近の静止で連続カウントしない
8. Setup、Countdown、Transformation、Posing、Resultsで同じ動きをしても
   回数と成長量が増えず、新しい拍振動も出ない
9. Training中にBody Trackingを意図的に失わせ、HUDが追跡待ちになった時に
   残り時間、採点、拍が停止し、復帰直後の途中動作で誤カウントしない
10. 譜面終了直前に成立したUP/DOWNが最終結果へ1回だけ含まれる
11. Resultsの肘・腕回数と成長量がTraining終了直前のHUDと一致し、
   タイミング点数が発生したUP/DOWNの総数、精度がその平均になっており、
   ポージングや待機中に変化しない
12. 身長や可動域の異なる複数人で確認し、必要なら左右Start / Targetを先に
    調整してからタイミング窓と成長速度を詰める
13. 3、2、1、GO、動作完了、リズム成功、3回の種目切替が一度ずつ聞こえ、
    1回の運動で完了音と成功音が重ならない
14. Trackingを意図的に失わせても失敗音が鳴らず、復帰時に過去の音が
    再発火しない。音、表示、振動を同時にしても刺激が強すぎない
15. BGMの第1拍が最初の拍振動と一致し、1分以上経っても譜面とずれない
16. Tracking待ちでBGMと拍が同時に止まり、復帰後に曲が最初から再開したり
    二重再生されたりせず、同じ位置から続く
17. Training終了時だけBGMが短く消え、TransformationのReveal音と重ならない
18. GO、動作完了、リズム成功、種目切替がBGMに埋もれず、短音終了後にBGMが
    不自然な段差や二重再生なく元の音量へ戻る
