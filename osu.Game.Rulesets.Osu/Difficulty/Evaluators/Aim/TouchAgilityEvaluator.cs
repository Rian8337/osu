// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class TouchAgilityEvaluator
    {
        private const double hand_coordination_bonus = 0.7;
        private const double transition_to_drag_bonus = 0.3;
        private const double agility_obstruction_max_bonus = 1.5;

        /// <summary>
        /// Evaluates the difficulty of agility on the current object with touch device, based on:
        /// <list type="bullet">
        /// <item><description>the standard agility difficulty from the previous object hit with the same hand,</description></item>
        /// <item><description>difficulty of coordinating between two hands,</description></item>
        /// <item><description>physical obstruction from the other hand,</description></item>
        /// <item><description>and difficulty of transitioning from tapping to dragging.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrent = (OsuDifficultyHitObject)current;

            if (osuCurrent.TouchData?.PerHandObject == null)
                return 0;

            var touchData = osuCurrent.TouchData.Value;

            bool isHandSwitch = touchData.AimingHand != touchData.PrevAimingHand;

            bool isTransitionToDrag = touchData.Action is OsuTouchAction.OsuDragAction
                                      && touchData.PrevAction is not OsuTouchAction.OsuDragAction;

            double agilityMultiplier = 1.0;

            // Reward the hand coordination required to switch aim hands across consecutive objects.
            if (isHandSwitch)
                agilityMultiplier += hand_coordination_bonus;

            // Slightly reward the difficulty of suddenly transitioning from tapping to dragging on agility.
            if (isTransitionToDrag)
                agilityMultiplier += transition_to_drag_bonus;

            // Reward physical obstruction created by overlapping hands during agility.
            agilityMultiplier += touchData.ObstructionFactor * agility_obstruction_max_bonus;

            return AgilityEvaluator.EvaluateDifficultyOf(touchData.PerHandObject) * agilityMultiplier;
        }
    }
}
