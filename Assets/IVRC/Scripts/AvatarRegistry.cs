using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Owns the active avatar and guarantees that only one registered tracked
    /// body, mirror, and growth output are enabled at a time.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class AvatarRegistry : MonoBehaviour
    {
        [SerializeField] private List<AvatarDefinition> _avatars =
            new List<AvatarDefinition>();
        [SerializeField, Min(0)] private int _initialIndex;

        public event Action<AvatarDefinition> OnAvatarChanged;

        public AvatarDefinition Active { get; private set; }
        public IReadOnlyList<AvatarDefinition> Avatars =>
            _avatars;
        public int ActiveIndex =>
            Active == null ? -1 : _avatars.IndexOf(Active);

        private void Awake()
        {
            RemoveNullEntries();
            if (_avatars.Count <= 0)
            {
                Debug.LogWarning(
                    "[AvatarRegistry] No avatars are registered.",
                    this);
                return;
            }

            var startIndex = Mathf.Clamp(
                _initialIndex,
                0,
                _avatars.Count - 1);
            for (var index = 0;
                 index < _avatars.Count;
                 index++)
            {
                var avatar = _avatars[index];
                avatar.SetActive(index == startIndex);
                if (!avatar.Validate(out var error))
                {
                    Debug.LogWarning(
                        $"[AvatarRegistry] '{avatar.DisplayName}' " +
                        $"is misconfigured: {error}",
                        avatar);
                }
            }

            Active = _avatars[startIndex];
        }

        private void Start()
        {
            if (Active != null)
            {
                OnAvatarChanged?.Invoke(Active);
            }
        }

        public void SwapTo(AvatarDefinition next)
        {
            if (next == null)
            {
                return;
            }

            if (Application.isPlaying &&
                !next.Validate(out var error))
            {
                Debug.LogWarning(
                    $"[AvatarRegistry] Cannot activate " +
                    $"'{next.DisplayName}': {error}",
                    next);
                return;
            }

            if (!_avatars.Contains(next))
            {
                _avatars.Add(next);
            }

            var changed = Active != next;
            foreach (var avatar in _avatars)
            {
                if (avatar != null && avatar != next)
                {
                    avatar.SetActive(false);
                }
            }

            next.SetActive(true);
            Active = next;
            if (changed)
            {
                OnAvatarChanged?.Invoke(next);
            }
        }

        public void SwapToIndex(int index)
        {
            if (index < 0 || index >= _avatars.Count)
            {
                return;
            }

            SwapTo(_avatars[index]);
        }

        public void SwapNext()
        {
            if (_avatars.Count <= 0)
            {
                return;
            }

            var currentIndex = ActiveIndex;
            SwapToIndex((currentIndex + 1) % _avatars.Count);
        }

        private void RemoveNullEntries()
        {
            _avatars ??= new List<AvatarDefinition>();
            _avatars.RemoveAll(avatar => avatar == null);
        }
    }
}
