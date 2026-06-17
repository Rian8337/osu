// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    /// <summary>
    /// Represents a hand in which a touch device player can use to aim an object in a beatmap.
    /// </summary>
    public enum OsuTouchHand
    {
        Left,
        Right
    }

    /// <summary>
    /// Represents an action in which a touch device player can use to hit an object in a beatmap.
    /// </summary>
    public abstract record OsuTouchAction
    {
        public bool IsDrag => this is OsuDragAction;

        /// <summary>
        /// Touch the screen with left hand directly.
        /// </summary>
        public static OsuTouchAction Left { get; } = new OsuHandAction(OsuTouchHand.Left);

        /// <summary>
        /// Touch the screen with right hand directly.
        /// </summary>
        public static OsuTouchAction Right { get; } = new OsuHandAction(OsuTouchHand.Right);

        /// <summary>
        /// Drag to the next object with the same hand used to hit the previous object and use the opposite hand to tap.
        /// </summary>
        public static OsuTouchAction Drag { get; } = new OsuDragAction();

        /// <summary>
        /// Touch the screen with the given hand.
        /// </summary>
        public sealed record OsuHandAction(OsuTouchHand Hand) : OsuTouchAction;

        /// <summary>
        /// Drag to the next object with the same hand used to hit the previous object and use the opposite hand to tap.
        /// </summary>
        public sealed record OsuDragAction : OsuTouchAction;
    }
}
