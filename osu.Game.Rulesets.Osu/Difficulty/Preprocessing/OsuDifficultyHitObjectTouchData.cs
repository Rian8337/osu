// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    /// <summary>
    /// Touch data produced by <see cref="OsuTouchActionSequenceOptimizer.FindTouchDataOfOptimalSequence"/>, used to evaluate aim and speed difficulty of an <see cref="OsuDifficultyHitObject"/> hit with touch device.
    /// </summary>
    /// <param name="Action">The touch action used to hit this <see cref="OsuDifficultyHitObject"/>.</param>
    /// <param name="AimingHand">The hand used to aim this <see cref="OsuDifficultyHitObject"/>.</param>
    /// <param name="PrevAction">The touch action used on the previous <see cref="OsuDifficultyHitObject"/>. Null for the first object.</param>
    /// <param name="PrevAimingHand">The hand that aimed the previous <see cref="OsuDifficultyHitObject"/>. Null for the first object.</param>
    /// <param name="PerHandObject">A synthetic <see cref="OsuDifficultyHitObject"/> built from the objects hit using the same <see cref="AimingHand"/>. Null when there is no prior object hit with this <see cref="AimingHand"/>.</param>
    /// <param name="ObstructionFactor">A factor in [0, 1] representing how much the <see cref="PrevAimingHand"/> physically obstructs the <see cref="AimingHand"/>'s path to this <see cref="OsuDifficultyHitObject"/>.</param>
    public readonly record struct OsuDifficultyHitObjectTouchData(
        OsuTouchAction Action,
        OsuTouchHand AimingHand,
        OsuTouchAction? PrevAction,
        OsuTouchHand? PrevAimingHand,
        OsuDifficultyHitObject? PerHandObject,
        double ObstructionFactor);
}
