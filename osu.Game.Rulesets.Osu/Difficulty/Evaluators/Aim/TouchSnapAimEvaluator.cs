// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class TouchSnapAimEvaluator
    {
        private const double hand_coordination_bonus = 1.05;
        private const double transition_to_drag_bonus = 1.95;
        private const double snap_obstruction_max_bonus = 3.2;

        /// <summary>
        /// Evaluates the difficulty of snap aiming the current object with touch device, based on:
        /// <list type="bullet">
        /// <item><description>the standard snap aim difficulty from the previous object hit with the same hand,</description></item>
        /// <item><description>difficulty of coordinating between two hands,</description></item>
        /// <item><description>physical obstruction from the other hand,</description></item>
        /// <item><description>and difficulty of transitioning from tapping to dragging.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool includeSliders)
        {
            var osuCurrent = (OsuDifficultyHitObject)current;

            if (osuCurrent.TouchData?.PerHandObject == null)
                return 0;

            var touchData = osuCurrent.TouchData.Value;

            bool isHandSwitch = touchData.AimingHand != touchData.PrevAimingHand;

            bool isTransitionToDrag = touchData.Action is OsuTouchAction.OsuDragAction
                                      && touchData.PrevAction is not OsuTouchAction.OsuDragAction;

            double snapMultiplier = 1.0;

            // Reward the hand coordination required to switch aim hands across consecutive objects.
            if (isHandSwitch)
                snapMultiplier += hand_coordination_bonus;

            // Reward the difficulty of suddenly transitioning from tapping to dragging on jumps.
            if (isTransitionToDrag)
                snapMultiplier += transition_to_drag_bonus;

            // Reward physical obstruction created by overlapping hands during jumps.
            snapMultiplier += touchData.ObstructionFactor * snap_obstruction_max_bonus;

            double snapDifficulty = SnapAimEvaluator.EvaluateDifficultyOf(touchData.PerHandObject, includeSliders);

            // The previous difficulties only apply to the sliderhead and not the rest of the slider.
            // Thus, apply the reward to only the non-slider component of snap aim.
            if (includeSliders)
            {
                double snapNoSliders = SnapAimEvaluator.EvaluateDifficultyOf(touchData.PerHandObject, false);
                snapDifficulty = snapNoSliders * snapMultiplier + (snapDifficulty - snapNoSliders);
            }
            else
            {
                snapDifficulty *= snapMultiplier;
            }

            return snapDifficulty;
        }
    }
}
