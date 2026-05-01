// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class TouchSpeedEvaluator
    {
        private const double singletap_multiplier = 0.95;
        private const double hand_coordination_bonus = 0.275;

        /// <summary>
        /// Evaluates the difficulty of tapping the current object with touch device, based on:
        /// <list type="bullet">
        /// <item><description>the standard speed difficulty from the previous object hit with the same hand,</description></item>
        /// <item><description>difficulty of coordinating between two hands.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            var osuCurrent = (OsuDifficultyHitObject)current;

            if (osuCurrent.TouchData == null || osuCurrent.TouchData.Value.PerHandObject == null)
                return 0;

            var touchData = osuCurrent.TouchData.Value;

            if (touchData.Action is OsuTouchAction.OsuDragAction)
                return SpeedEvaluator.EvaluateDifficultyOf(current);

            bool isHandSwitch = touchData.AimingHand != touchData.PrevAimingHand;

            bool isSingletapped = touchData.Action is not OsuTouchAction.OsuDragAction
                && touchData.PrevAction is not OsuTouchAction.OsuDragAction
                && touchData.AimingHand == touchData.PrevAimingHand;

            // During a drag action, the hand action assigned to an object is the hand used to aim that object.
            // Thus, the opposite hand must have been used to tap that object. Let us call this opposite hand the drag tapping hand.
            // If the current object is not dragged, and the hand used to tap the current object is the same as the previous object's drag tapping hand,
            // the per-hand object's strain time does not reflect this.
            // Thus, we should calculate the strain of this object using the beatmap object instead of the per hand object.
            bool calculateStrainWithOriginalObject = touchData.Action is not OsuTouchAction.OsuDragAction
                && touchData.PrevAction is OsuTouchAction.OsuDragAction
                && touchData.AimingHand != touchData.PrevAimingHand;

            OsuDifficultyHitObject evaluationObject = calculateStrainWithOriginalObject ? osuCurrent : touchData.PerHandObject;

            double speedMultiplier = 1.0;

            // Very slightly reward the hand coordination required to switch aim hands across consecutive objects.
            if (isHandSwitch)
                speedMultiplier += hand_coordination_bonus;

            // When fully alternating with two hands using touch device, the strain time will be roughly double that of usual.
            // This is because the strain time of an object with touch device is calculated based off of the last object hit with that same hand.
            // Halve the straintime it here to offset this.
            const double strain_time_multiplier = 0.5;
            double speedValue = SpeedEvaluator.EvaluateDifficultyOf(evaluationObject, strain_time_multiplier);

            // Apply a slight reduction if the note was singletapped since singletapping 1/2 beats of a given BPM (jumps)
            // is generally agreed to be easier than alternating 1/4 beats of the same BPM (streams).
            if (isSingletapped)
                speedValue *= singletap_multiplier;

            return speedValue * speedMultiplier;
        }
    }
}
