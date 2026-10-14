> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# Pose stage feedback

`Systems/PoseSystem` now turns the live three-point pose match into restrained,
Quest-friendly stage feedback. It only runs during Posing and Results.

## What reacts

- `StageLight_Cyan` and `StageLight_Magenta` keep their original colors and
  shadow settings. Their intensity rises smoothly from the saved value of 4
  to a maximum of 6.
- The 55 `Audience_R*` placeholder people bob by at most 6 cm and tilt by at
  most 3 degrees. `AudienceFloor` is never moved.
- A short generated placeholder cheer plays when the live spatial match first
  reaches 60, 75, and 90. If several tiers are crossed during the cooldown,
  the highest pending tier still plays once.
- Each non-final successful hold adds one 0.65-second light/audience pulse and
  one short, restrained pulse on each connected controller. A timed-out pose
  quietly advances without negative sound or haptics.
- The final routine success keeps the larger Results cheer. The final pose does
  not add another medium haptic or audio cue immediately before it.

The live reaction uses `PoseScoreResult.SpatialScore`, not the hold-weighted
total. Holding adds a small bonus and makes the audience move more uniformly.
Countdown, invalid tracking, and tracking loss target zero reaction.

## Safety and reset behavior

- No flashing, strobe, added realtime shadows, or material instances are used.
- Light and audience base values are cached once. Every animation frame is
  calculated from those base values, so repeated rounds cannot drift.
- Setup, Training, Transformation, StageTransition, disable, and retry
  immediately restore both lights and all audience transforms and stop audio
  and posing haptics.
- The generated mono clip is created once at runtime and destroyed with the
  controller. It can be replaced by assigning `Cheer Clip Override`.
- Outside Posing and Results, the controller does no per-frame visual work.

## Main tuning fields

Select `Systems/PoseSystem` and edit `Pose Stage Feedback Controller`:

- Reaction Start / Full Reaction Score: 45 / 90
- Maximum Light Intensity Multiplier: 1.5
- Reaction Response: 0.25 seconds
- Audience Bob Height / Frequency / Tilt: 0.06 m / 1.4 Hz / 3 degrees
- Pose success pulse: 0.8 strength / 0.65 seconds
- Pose success haptic: 0.8 frequency / 0.42 amplitude / 0.09 seconds
- Pose success haptic duration is always clamped to a finite 0.01–0.20 seconds;
  invalid Inspector or runtime values cannot become continuous vibration.
- Cheer thresholds: 60 / 75 / 90
- Success cheer volume: 0.35
- Cheer cooldown: 0.8 seconds

The scene contains an `AudioSource` on `Phase_Posing/Audience`. The generated
sound is intentionally only a temporary functional cue; replace the override
with the final crowd asset later without changing code.

`Systems/StageMusic` owns a separate 2D looping source from the judges
transition through Posing. It ducks during tracking recovery and fades before
Results, so it never stops or masks the pose-success source or Audience cheer.
It also ducks briefly while either foreground source is playing, preserving a
single clear primary response instead of stacking all audio at equal weight.
The generated stage bed is only a quiet prototype fallback; replace it with the
licensed `Stage_Bed.wav` specified in `STAGE_TRANSITION.md`.

## Verification

- Unity compilation: 0 errors and 0 warnings.
- Full EditMode suite: 382 tests passed. Pose-completion event ordering,
  timeout behavior, player copy, pulse math, and scene wiring are included.
- Scene wiring: 2 intended lights, 55 intended audience members, and 1
  non-looping Audience audio source.

Quest Link or device verification still required:

- move from below 45 to above 90 and confirm the light increase is smooth;
- confirm 60/75/90 tier cues never overlap excessively or repeat while the
  score oscillates around a threshold;
- lose one controller and confirm the reaction fades down;
- complete a non-final pose and confirm one pulse and one short haptic, then
  time out a second pose and confirm no negative cue;
- complete the final pose and confirm the Results payoff does not double-fire;
- retry five times and confirm both lights return to intensity 4 and every
  audience member returns to its original position.
