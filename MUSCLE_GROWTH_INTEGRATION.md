> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# Muscle growth integration

The gameplay growth gauge now feeds one shared `MuscleGrowthController`.
The current scene drives both a real candidate avatar BlendShape output and a
safe mock fallback. The training, scoring, flow, and HUD remain independent
from the avatar-specific binding.

## Current scene status

`Systems/MuscleGrowthSystem` is always active and contains:

- `MuscleGrowthController`
- `MockMuscleGrowthOutput`
- `BlendShapeMuscleGrowthOutput`

Both the mirror HUD and stage HUD can read the value actually applied by this
controller. Player-facing mode hides the technical output line by default;
debug details can show, for example, `MUSCLE 75% BLENDSHAPE`. Results uses the
same applied-output path instead of displaying a value that may not have
reached the avatar.

The avatar registry contains the male
`Assets/IVRC/Avatar/2_UniversalBaseCharacters.fbx` and the female
`Assets/IVRC/Avatar/UniversalBaseCharacters_famale.fbx`. Both import as valid
Humanoids and use their accompanying Movement configs. Each avatar output has
explicit tracked and mirrored renderer bindings for `Muscle`,
`UpperArm.L/R`, `Shoulder.L/R`, and `Chest.L/R`.

The current FBXs use a configured baseline weight of 100 and target weight of
0 for those shapes. The gameplay meaning remains 0 = resting body and
1 = earned muscular body; each explicit binding owns the conversion to the
FBX's weight direction. `MirrorTransforms` copies bones but not renderer
BlendShape weights, so tracked and mirrored renderers remain explicitly bound.

## Data flow

- Setup, Ready, Countdown: growth output is exactly 0.
- Training: follows `TrainingRhythmScorer.GrowthNormalized`.
- Transformation, both StageTransition directions, Posing, Results: follows
  the immutable `GameFlowController.CompletedMuscleGrowth` snapshot captured
  at the Training boundary. `CompletedGrowthNormalized` remains its Overall
  compatibility view.
- Retry keeps the transformed look while returning behind the fade, then Setup
  resets every output to its baseline.

Transformation presents this frozen value without adding growth. The existing
training lights and four mirror-frame renderers follow the same Lock / Reveal /
Hero Hold envelope. Mirror-frame colour is applied through a
`MaterialPropertyBlock`, retains a softer tint during the Hero Hold, and restores
the exact prior block state on exit, retry, or disable; the shared material is
never mutated.

Overall, upper-arm, shoulder, and derived chest channels are captured together
as one sanitized `MuscleGrowthSnapshot`. After Training,
`MuscleGrowthController` no longer reads mutable per-part scorer values. This
keeps the tracked avatar, mirror, entrance copy, pose presentation, and Results
on the same earned body even if the scorer later resets or changes.

The controller stores scene references as `MonoBehaviour[]` and validates
`IMuscleGrowthOutput` at runtime, because Unity does not reliably serialize
interface fields directly. It detects output activation swaps by component
identity, not only by count, and resets removed outputs before rebuilding the
list.

All input and tuning values reject NaN and Infinity. The controller also keeps
the original `MonoBehaviour` reference so Unity's destroyed-object null
semantics remain effective.

## Current candidate binding

- Male and female definitions each provide tracked, mirrored, cinematic, and
  pose-guide presentation sources.
- Each `BlendShapeMuscleGrowthOutput` binds both tracked and mirrored skin
  renderers explicitly.
- Overall: `Muscle`
- Elbow flexion: `UpperArm.L` / `UpperArm.R`
- Arm raise: `Shoulder.L` / `Shoulder.R`
- Chest support: `Chest.L` / `Chest.R`
- Configured baseline: 100
- Target weight: 0
- `Use Current Weight As Baseline`: disabled
- Mock output: retained as a no-hardware fallback

The adapter changes `SetBlendShapeWeight` for large-form deformation and uses
a `MaterialPropertyBlock` for the restrained skin response. Existing normal
maps stay assigned; `_BumpScale` moves from 0.72 at rest to 1.08 at full growth
and `_Smoothness` moves from 0.14 to 0.25. Shape remains the primary result and
the surface response only makes the existing muscle planes catch the light.
Shared materials are never instantiated or modified, and any property block
that existed before this system is restored on disable, refresh, or avatar
swap.

The adapter never changes bones, Transforms, the Animator, the shared mesh, or
Meta Movement retargeting. Weights and surface state are reapplied in
`LateUpdate` so an Animator cannot silently overwrite the presented result.
Missing names are negatively cached and do not perform a name search every
frame. Runtime mesh swaps remember each previously resolved renderer/mesh
baseline. A mesh first seen while growth is nonzero uses the configured
baseline instead of treating a carried-over animated weight as the new
baseline.

Stage entrance and pose-reference clones derive their muscle channels from the
bindings on the authoritative tracked renderer. Before a clone is used, every
configured muscle BlendShape must exist on at least one of its skinned meshes.
An incomplete clone is rejected as a whole instead of silently showing only a
subset of the body state earned during training. Mirror-only and unconfigured
facial shapes are not treated as muscle channels.

Tracked and mirrored skins receive the same overall surface snapshot as their
shape state. The stage-entrance clone captures that snapshot from the active
avatar and applies it only to its configured muscle skin renderer; hair, eyes,
and dedicated translucent pose/training guides are intentionally excluded.
The live body shown by the pose monitor is the tracked avatar, so it already
uses the same surface state.

## Remaining artist and device confirmation

The candidate data is now connected, but 0 and 100 are engineering defaults.
The avatar team still needs to confirm the intended resting body, maximum
muscle amount, and final materials. Both current skins already have their base
normal map connected. The supplied roughness files are deliberately not wired
directly: URP Lit expects smoothness in an appropriate channel, so roughness
must be inverted/packed and its data-texture importer settings made consistent
before use. Detail Normal and a region mask remain artist deliverables.

Quest visual verification must confirm that 0%, 50%, and 100% read as a useful
but comfortable progression, and that body tracking, calibration, mirror
rendering, highlights, and the entrance copy remain aligned throughout the
deformation. Current surface values are conservative engineering defaults, not
final art approval.

## Verification

- Unity compilation: 0 errors; one existing Desktop Simulation unused-field
  warning appears on a forced all-package rebuild.
- `MuscleGrowthTests`: 27 EditMode tests passed.
- Full EditMode suite: 465 tests passed.
- A real `BodyRepCounter` timing event path verifies that one
  low-high-low cycle emits exactly one UP and one DOWN score and awards one
  configured rep of growth at multiplier 1.
- Scene configuration tests verify valid male and female Humanoid avatars,
  their Movement mappings, active mirror mapping, explicit per-channel
  BlendShape bindings, configured weight direction, non-zero deformation,
  normal-mapped tracked/mirrored/entrance skins, and conservative surface
  tuning ranges.
- Tests cover the phase source table, finite-value guards, mock reset, removed
  output reset, explicit BlendShape-name binding, target-weight fallback, and
  exact baseline restoration across re-enable order, binding removal, refresh,
  and shared-mesh swaps.
- Android ARM64/IL2CPP Development Build succeeded. The final validation APK
  is 230,337,247 bytes with SHA-256
  `F2D773D6AA7937ACE71F66637F737F57BA6E578D7B3552085BF1854327A9A9AB`.
  Its manifest requests Body Tracking but not microphone, face, eye, scene,
  anchor, hand-tracking, or passthrough-camera permissions. USB installation
  to a Quest Pro succeeded.

Quest Link or device verification:

- watch the mirror during Training and confirm growth is smooth and monotonic;
- confirm Transformation, stage travel, Posing, and Results hold the frozen
  value;
- retry and confirm the avatar returns to its exact artist-authored baseline;
- repeat five times and check for weight drift;
- temporarily disable and re-enable each avatar output and confirm the active
  instance immediately receives the current value;
- verify body IK, controller alignment, height calibration, and pose scoring
  remain unchanged at 0%, 50%, and 100% muscle weight.
