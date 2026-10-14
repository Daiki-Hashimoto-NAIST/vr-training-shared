> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# Result scoring

`VRTrainingPrototype` freezes the completed training and posing values before
building one final result. The result does not keep reading the live counters,
so it stays stable while the Results screen is visible and during the retry
transition.

## Runtime flow

1. `GameFlowController` freezes reps, rhythm accuracy, rhythm sample count, and
   growth when Training ends.
2. `GameFlowController` freezes the best pose result and whether the required
   hold was completed when Posing ends.
3. The always-active `Systems/ResultSystem` calculates the final result when
   the flow enters Results.
4. Both flow HUDs read the same frozen result. The player-facing HUD counts the
   final score up for 0.6 seconds and reveals the rank at 75% progress. Repeated
   result notifications do not restart this presentation.
   It also compares the frozen Training and Pose pillars, names one strength,
   and gives one concrete next-run goal. It does not recalculate or alter score.
5. Entering Setup clears both the frozen result and the one-shot presentation
   state for the next run.

The reveal adds no audio, haptics, lighting, camera, or tracking-space motion;
the final-pose response remains the audiovisual peak.

The Training and Posing timers each leave one final `LateUpdate` at their time
boundary. This lets a rep, timing point, or pose hold completed on the final
frame be included in the snapshot.

## Default calculation

The adjustable asset is:

`Assets/IVRC/Config/DefaultExperienceScoreProfile.asset`

- Rep score: average completion percentage of the elbow and arm-raise targets,
  capped at 100.
- Rhythm score: average accuracy across both UP and DOWN timing points.
- Growth score: final normalized muscle gauge.
- Training score: Reps 35%, Rhythm 45%, Growth 20%.
- Pose score: frozen three-point pose score. A pose that was not held for the
  required duration receives 50% credit by default.
- Final score: Training 55%, Pose 45%.
- Rank thresholds: S 90, A 80, B 65, C 50, otherwise D.

All weights are normalized, so designers can tune relative values without
making them add up to one. Rep targets, category weights, failed-pose credit,
and rank thresholds are Inspector-editable.

## Automated checks

- `ExperienceScoreCalculatorTests`: 14 EditMode tests passed.
- `PosingScoreCalculatorTests`: 13 EditMode tests passed.
- Full EditMode suite: 403/403 tests passed, including one-shot result reveal,
  retry reset, frozen values, and the authored Japanese TMP layout.
- Unity compilation: 0 errors. A full rebuild can report the known unused
  Desktop Simulation transition field warning; the incremental final compile
  reports 0 warnings.
- Invalid numeric inputs, missing active weights, zero targets, failed pose
  credit, over-target reps, and inclusive rank boundaries are covered.

## Quest check

On Quest Link or a device build, complete one full run and verify:

- the final UP/DOWN timing point is included when it lands exactly at timeout;
- Results counts the final score once, then reveals one stable rank without
  restarting or changing its frozen component values;
- an incomplete pose is framed positively as a next-attempt opportunity and
  receives partial rather than full pose credit;
- the one-line summary identifies the stronger pillar (or a balanced run) and
  gives a concrete next action without hiding the safe headset-removal line;
- B returns to the initial Setup state, clears the old result, and allows a
  second complete run;
- the HMD-removal/staff instruction reads before the optional B retry hint;
- Results remains readable at the physical standing position.
