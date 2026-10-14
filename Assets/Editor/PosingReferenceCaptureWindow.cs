using System.Linq;
using IVRC;
using UnityEditor;
using UnityEngine;

namespace IVRC.EditorTools
{
    /// <summary>
    /// Records a reference pose from a real body into a
    /// <see cref="PosingScoreProfile"/>.
    ///
    /// The targets in the profiles are typed-in round numbers, so a
    /// participant holding a pose correctly was scored against hand positions
    /// nobody had ever stood in. Tracking is three-point, and the elbows that
    /// actually define a double biceps are inferred from those same three
    /// points, so no amount of extra scoring terms can recover the pose —
    /// measuring the target off a body is what makes the score honest.
    ///
    /// Deliberately outside the experience: it does not touch the flow, and
    /// nothing here ships. Capture runs on a countdown because both hands are
    /// busy being the pose.
    /// </summary>
    public sealed class PosingReferenceCaptureWindow : EditorWindow
    {
        private const float DefaultCountdownSeconds = 5f;

        private PosingScoreProfile _profile;
        private float _countdownSeconds = DefaultCountdownSeconds;
        private double _captureAtEditorTime = -1d;
        private string _status = string.Empty;
        private MessageType _statusType = MessageType.Info;

        [MenuItem("IVRC/Capture Posing Reference", priority = 20)]
        private static void Open()
        {
            GetWindow<PosingReferenceCaptureWindow>(
                    "Posing Reference")
                .minSize = new Vector2(360f, 260f);
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Stand in the pose, start the countdown, hold still until it " +
                "reaches zero. What is tracked at that moment becomes the " +
                "profile's target, so the pose you just held is what scores " +
                "100 from then on.",
                MessageType.None);

            _profile = (PosingScoreProfile)EditorGUILayout.ObjectField(
                "Profile",
                _profile,
                typeof(PosingScoreProfile),
                false);
            _countdownSeconds = Mathf.Clamp(
                EditorGUILayout.FloatField(
                    "Countdown (s)",
                    _countdownSeconds),
                0f,
                30f);

            var scorer = FindScorer();
            using (new EditorGUI.DisabledScope(
                       !Application.isPlaying ||
                       scorer == null ||
                       _profile == null))
            {
                if (IsCounting)
                {
                    if (GUILayout.Button(
                            $"Cancel ({RemainingSeconds:F1}s)"))
                    {
                        _captureAtEditorTime = -1d;
                        SetStatus("Cancelled.", MessageType.Info);
                    }
                }
                else if (GUILayout.Button("Capture after countdown"))
                {
                    _captureAtEditorTime =
                        EditorApplication.timeSinceStartup +
                        _countdownSeconds;
                    SetStatus(
                        "Get into the pose and hold.",
                        MessageType.Info);
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to capture. Use IVRC / Desktop Mode to " +
                    "pick how this machine runs.",
                    MessageType.Warning);
            }
            else if (scorer == null)
            {
                EditorGUILayout.HelpBox(
                    "No ThreePointPosingScorer in the scene.",
                    MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, _statusType);
            }

            if (_profile == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Current target",
                EditorStyles.boldLabel);
            var target = _profile.ToSettings().Target;
            EditorGUILayout.LabelField(
                "Left hand",
                target.LeftHandPositionFromHeadNormalized
                    .ToString("F3"));
            EditorGUILayout.LabelField(
                "Right hand",
                target.RightHandPositionFromHeadNormalized
                    .ToString("F3"));
            EditorGUILayout.LabelField(
                "Head rotation",
                target.HeadEulerDegreesInStageFrame.ToString("F1"));
        }

        private bool IsCounting => _captureAtEditorTime > 0d;

        private float RemainingSeconds =>
            Mathf.Max(
                0f,
                (float)(_captureAtEditorTime -
                        EditorApplication.timeSinceStartup));

        private void Tick()
        {
            if (!IsCounting)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                _captureAtEditorTime = -1d;
                SetStatus(
                    "Play mode ended before the capture.",
                    MessageType.Warning);
                return;
            }

            Repaint();
            if (RemainingSeconds > 0f)
            {
                return;
            }

            _captureAtEditorTime = -1d;
            Capture();
        }

        private void Capture()
        {
            var scorer = FindScorer();
            if (scorer == null || _profile == null)
            {
                SetStatus(
                    "Nothing to capture into.",
                    MessageType.Error);
                return;
            }

            if (!scorer.TryCaptureTarget(
                    out var captured,
                    out var reason))
            {
                SetStatus(
                    $"Could not capture: {reason}",
                    MessageType.Error);
                return;
            }

            // Written through SerializedObject so it survives the Play mode
            // exit that would discard a plain field assignment.
            var so = new SerializedObject(_profile);
            var targetProperty = so.FindProperty("_target");
            Write(
                targetProperty,
                "LeftHandPositionFromHeadNormalized",
                captured.LeftHandPositionFromHeadNormalized);
            Write(
                targetProperty,
                "RightHandPositionFromHeadNormalized",
                captured.RightHandPositionFromHeadNormalized);
            Write(
                targetProperty,
                "HeadEulerDegreesInStageFrame",
                captured.HeadEulerDegreesInStageFrame);
            Write(
                targetProperty,
                "LeftHandEulerDegreesInStageFrame",
                captured.LeftHandEulerDegreesInStageFrame);
            Write(
                targetProperty,
                "RightHandEulerDegreesInStageFrame",
                captured.RightHandEulerDegreesInStageFrame);
            // The guide bends the arms towards these. Capturing hands without
            // them leaves the guide pointing the elbows wherever the old
            // hand-authored hint said, which no longer relates to the hands
            // just recorded, and the guide then shows a pose nobody asked for.
            var elbowNote = "elbows unchanged";
            if (scorer.TryCaptureElbowHints(
                    out var leftElbow,
                    out var rightElbow,
                    out var elbowReason))
            {
                var elbows = so.FindProperty("_guideElbows");
                elbows.FindPropertyRelative(
                        "LeftPoleFromShoulderNormalized")
                    .vector3Value = leftElbow;
                elbows.FindPropertyRelative(
                        "RightPoleFromShoulderNormalized")
                    .vector3Value = rightElbow;
                elbowNote =
                    $"elbows {leftElbow:F2} / {rightElbow:F2}";
            }
            else
            {
                elbowNote = $"elbows not captured ({elbowReason})";
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_profile);
            AssetDatabase.SaveAssets();

            SetStatus(
                $"Captured into {_profile.name}. Hands at " +
                $"{captured.LeftHandPositionFromHeadNormalized:F3} / " +
                $"{captured.RightHandPositionFromHeadNormalized:F3}, " +
                elbowNote + ".",
                MessageType.Info);
        }

        private static void Write(
            SerializedProperty target,
            string field,
            Vector3 value)
        {
            var property = target.FindPropertyRelative(field);
            if (property != null)
            {
                property.vector3Value = value;
            }
        }

        private static ThreePointPosingScorer FindScorer()
        {
            return Object.FindObjectsByType<ThreePointPosingScorer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault();
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }
    }
}
