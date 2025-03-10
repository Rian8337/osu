﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class RhythmEvaluator
    {
        private const int history_time_max = 5000; // 5 seconds of calculatingRhythmBonus max.

        /// <summary>
        /// Calculates a rhythm multiplier for the difficulty of the tap associated with historic data of the current <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, double greatWindow)
        {
            if (current.BaseObject is Spinner)
                return 1;

            var osuCurrObj = (OsuDifficultyHitObject)current;

            if (osuCurrObj.IsOverlapping(false))
            {
                return 1;
            }

            int previousIslandSize = 0;

            double rhythmComplexitySum = 0;
            int islandSize = 1;
            double startRatio = 0; // store the ratio of the current start of an island to buff for tighter rhythms

            bool firstDeltaSwitch = false;

            int historicalNoteCount = Math.Min(current.Index, 32);

            // Exclude overlapping objects that can be tapped at once.
            var validPrevious = new List<OsuDifficultyHitObject>();

            for (int i = 0; i < historicalNoteCount; i++)
            {
                var obj = (OsuDifficultyHitObject)current.Previous(i);

                if (obj == null)
                {
                    break;
                }

                if (!obj.IsOverlapping(false))
                {
                    validPrevious.Add(obj);
                }
            }

            int rhythmStart = 0;

            while (rhythmStart < validPrevious.Count - 2 && current.StartTime - validPrevious[rhythmStart].StartTime < history_time_max)
                rhythmStart++;

            for (int i = rhythmStart; i > 0; i--)
            {
                var currObj = validPrevious[i - 1];
                var prevObj = validPrevious[i];
                var lastObj = validPrevious[i + 1];

                double currHistoricalDecay = (history_time_max - (current.StartTime - currObj.StartTime)) / history_time_max; // scales note 0 to 1 from history to now

                currHistoricalDecay = Math.Min((double)(validPrevious.Count - i) / validPrevious.Count, currHistoricalDecay); // either we're limited by time or limited by object count.

                double currDelta = currObj.StrainTime;
                double prevDelta = prevObj.StrainTime;
                double lastDelta = lastObj.StrainTime;
                double currRatio = 1.0 + 6.0 * Math.Min(0.5, Math.Pow(Math.Sin(Math.PI / (Math.Min(prevDelta, currDelta) / Math.Max(prevDelta, currDelta))), 2)); // fancy function to calculate rhythmbonuses.

                double windowPenalty = Math.Min(1, Math.Max(0, Math.Abs(prevDelta - currDelta) - greatWindow * 0.4) / (greatWindow * 0.4));

                windowPenalty = Math.Min(1, windowPenalty);

                double effectiveRatio = windowPenalty * currRatio;

                if (firstDeltaSwitch)
                {
                    if (!(prevDelta > 1.25 * currDelta || prevDelta * 1.25 < currDelta))
                    {
                        if (islandSize < 7)
                            islandSize++; // island is still progressing, count size.
                    }
                    else
                    {
                        if (current.Previous(i - 1).BaseObject is Slider) // bpm change is into slider, this is easy acc window
                            effectiveRatio *= 0.125;

                        if (current.Previous(i).BaseObject is Slider) // bpm change was from a slider, this is easier typically than circle -> circle
                            effectiveRatio *= 0.25;

                        if (previousIslandSize == islandSize) // repeated island size (ex: triplet -> triplet)
                            effectiveRatio *= 0.25;

                        if (previousIslandSize % 2 == islandSize % 2) // repeated island polartiy (2 -> 4, 3 -> 5)
                            effectiveRatio *= 0.50;

                        if (lastDelta > prevDelta + 10 && prevDelta > currDelta + 10) // previous increase happened a note ago, 1/1->1/2-1/4, dont want to buff this.
                            effectiveRatio *= 0.125;

                        rhythmComplexitySum += Math.Sqrt(effectiveRatio * startRatio) * currHistoricalDecay * Math.Sqrt(4 + islandSize) / 2 * Math.Sqrt(4 + previousIslandSize) / 2;

                        startRatio = effectiveRatio;

                        previousIslandSize = islandSize; // log the last island size.

                        if (prevDelta * 1.25 < currDelta) // we're slowing down, stop counting
                            firstDeltaSwitch = false; // if we're speeding up, this stays true and  we keep counting island size.

                        islandSize = 1;
                    }
                }
                else if (prevDelta > 1.25 * currDelta) // we want to be speeding up.
                {
                    // Begin counting island until we change speed again.
                    firstDeltaSwitch = true;
                    startRatio = effectiveRatio;
                    islandSize = 1;
                }
            }

            double doubletapness = 1;
            var osuNextObj = (OsuDifficultyHitObject)current.Next(0);

            // Nerf doubles that can be tapped at the same time to get Great hit results.
            if (osuNextObj != null)
            {
                double currDeltaTime = Math.Max(1, osuCurrObj.DeltaTime);
                double nextDeltaTime = Math.Max(1, osuNextObj.DeltaTime);
                double deltaDifference = Math.Abs(nextDeltaTime - currDeltaTime);
                double speedRatio = currDeltaTime / Math.Max(currDeltaTime, deltaDifference);
                double windowRatio = Math.Pow(Math.Min(1, currDeltaTime / osuCurrObj.HitWindowGreat), 2);
                doubletapness = Math.Pow(speedRatio, 1 - windowRatio);
            }

            return Math.Sqrt(4 + rhythmComplexitySum * calculateRhythmMultiplier(greatWindow) * doubletapness) / 2; //produces multiplier that can be applied to strain. range [1, infinity) (not really though)
        }

        private static double calculateRhythmMultiplier(double greatWindow)
        {
            double od = (80 - greatWindow) / 6;

            double odScaling = Math.Pow(od, 2) / 400;

            return 0.75 + (od >= 0 ? odScaling : -odScaling);
        }
    }
}
