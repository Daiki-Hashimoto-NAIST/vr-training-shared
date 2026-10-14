using System;

namespace IVRC
{
    [Flags]
    public enum RhythmGuideDisplayMode
    {
        None = 0,
        Ring = 1 << 0,
        GhostAvatar = 1 << 1,

        /// <summary>
        /// Draws the target pose as two thin contours instead of a filled
        /// ghost: the goal outline held on the body, and a larger outline
        /// shrinking onto it as the beat arrives.
        /// </summary>
        LineArt = 1 << 2,

        Both = Ring | GhostAvatar
    }
}
