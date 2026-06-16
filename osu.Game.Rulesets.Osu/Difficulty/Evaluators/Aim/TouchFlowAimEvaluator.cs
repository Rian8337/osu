// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class TouchFlowAimEvaluator
    {
        private const double hand_coordination_bonus = 1.475;
        private const double transition_to_drag_bonus = 0.9;
        private const double flow_obstruction_max_bonus = 4.35;

        /// <summary>
        /// Evaluates the difficulty of flow aiming the current object with touch device, based on:
        /// <list type="bullet">
        /// <item><description>the standard flow aim difficulty from the previous object hit with the same hand,</description></item>
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

            double flowMultiplier = 1.0;

            // Reward the hand coordination required to switch aim hands across consecutive objects.
            if (isHandSwitch)
                flowMultiplier += hand_coordination_bonus;

            // Slightly reward the difficulty of suddenly transitioning from tapping to dragging on flow aim.
            if (isTransitionToDrag)
                flowMultiplier += transition_to_drag_bonus;

            // Heavily reward physical obstruction created by overlapping hands during flow aim.
            flowMultiplier += touchData.ObstructionFactor * flow_obstruction_max_bonus;

            double flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(touchData.PerHandObject, includeSliders);

            // The previous difficulties only apply to the sliderhead and not the rest of the slider.
            // Thus, apply the reward to only the non-slider component of flow aim.
            if (includeSliders)
            {
                double flowNoSliders = FlowAimEvaluator.EvaluateDifficultyOf(touchData.PerHandObject, false);
                flowDifficulty = flowNoSliders * flowMultiplier + (flowDifficulty - flowNoSliders);
            }
            else
            {
                flowDifficulty *= flowMultiplier;
            }

            return flowDifficulty;
        }
    }
}
