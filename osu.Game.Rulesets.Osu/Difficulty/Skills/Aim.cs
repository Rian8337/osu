// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : VariableLengthStrainSkill
    {
        private const double skill_multiplier_snap = 70.9;
        private const double skill_multiplier_agility = 2.35;
        private const double skill_multiplier_flow = 242.0;
        private const double skill_multiplier_total = 1.12;
        private const double combined_snap_norm_exponent = 1.2;
        private const double strain_decay_base = 0.2;

        private const int reduced_section_time = 4000;
        private const double reduced_strain_baseline = 0.727;

        public readonly bool IncludeSliders;

        private readonly List<double> sliderStrains = new List<double>();

        private double currentStrain;

        public Aim(Mod[] mods, bool includeSliders)
            : base(mods)
        {
            IncludeSliders = includeSliders;
        }

        private static double strainDecay(double ms) => Math.Pow(strain_decay_base, ms / 1000);

        /// <summary>
        /// Computes the next aim strain by applying strain decay and evaluating the current object's snap, agility, and flow difficulty.
        /// </summary>
        public static double AdvanceStrainState(double currentStrain, IReadOnlyList<Mod> mods, DifficultyHitObject current, bool includeSliders)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            double snapDifficulty;
            double agilityDifficulty;
            double flowDifficulty;

            if (mods.Any(m => m is OsuModTouchDevice))
            {
                snapDifficulty = TouchSnapAimEvaluator.EvaluateDifficultyOf(current, includeSliders) * skill_multiplier_snap;
                agilityDifficulty = TouchAgilityEvaluator.EvaluateDifficultyOf(current) * skill_multiplier_agility;
                flowDifficulty = TouchFlowAimEvaluator.EvaluateDifficultyOf(current, includeSliders) * skill_multiplier_flow;
            }
            else
            {
                snapDifficulty = SnapAimEvaluator.EvaluateDifficultyOf(current, includeSliders) * skill_multiplier_snap;
                agilityDifficulty = AgilityEvaluator.EvaluateDifficultyOf(current) * skill_multiplier_agility;
                flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(current, includeSliders) * skill_multiplier_flow;
            }

            double totalDifficulty = ComputeOverallStrain(snapDifficulty, agilityDifficulty, flowDifficulty, mods);

            return currentStrain * decay + totalDifficulty * (1 - decay);
        }

        /// <summary>
        /// Combines the snap, agility, and flow components into a single overall strain value.
        /// </summary>
        public static double ComputeOverallStrain(double snapDifficulty, double agilityDifficulty, double flowDifficulty, IReadOnlyList<Mod> mods)
        {
            // We compare flow to combined snap and agility because snap by itself doesn't have enough difficulty to be above flow on streams
            // Agility on the other hand is supposed to measure the rate of cursor velocity changes while snapping
            // So snapping every circle on a stream requires an enormous amount of agility at which point it's easier to flow
            double combinedSnapDifficulty = DifficultyCalculationUtils.Norm(combined_snap_norm_exponent, snapDifficulty, agilityDifficulty);

            double pSnap = calculateSnapFlowProbability(flowDifficulty / combinedSnapDifficulty);
            double pFlow = 1 - pSnap;

            if (mods.Any(m => m is OsuModRelax))
            {
                combinedSnapDifficulty *= 0.75;
                flowDifficulty *= 0.6;
            }

            double totalDifficulty = combinedSnapDifficulty * pSnap + flowDifficulty * pFlow;

            return totalDifficulty * skill_multiplier_total;
        }

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) =>
            currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain = AdvanceStrainState(currentStrain, Mods, current, IncludeSliders);

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentStrain);

            return currentStrain;
        }

        // A function that turns the ratio of snap : flow into the probability of snapping/flowing
        // It has the constraints:
        // P(snap) + P(flow) = 1 (the object is always either snapped or flowed)
        // P(snap) = f(snap/flow), P(flow) = f(flow/snap) (ie snap and flow are symmetric and reversible)
        // Therefore: f(x) + f(1/x) = 1
        // 0 <= f(x) <= 1 (cannot have negative or greater than 100% probability of snapping or flowing)
        // This logistic function is a solution, which fits nicely with the general idea of interpolation and provides a tuneable constant
        private static double calculateSnapFlowProbability(double ratio)
        {
            const double k = 7.27;

            if (ratio == 0)
                return 0;

            if (double.IsNaN(ratio))
                return 1;

            return DifficultyCalculationUtils.Logistic(-k * Math.Log(ratio));
        }

        public double GetDifficultSliders()
        {
            if (sliderStrains.Count == 0)
                return 0;

            double maxSliderStrain = sliderStrains.Max();

            if (maxSliderStrain == 0)
                return 0;

            return sliderStrains.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxSliderStrain * 12.0 - 6.0))));
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            double consistentTopStrain = difficultyValue * (1 - DecayWeight); // What would the top strain be if all strain values were identical

            if (consistentTopStrain == 0)
                return 0;

            // Use a weighted sum of all strains. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopStrain, 0.88, 10, 1.1));
        }

        public override double DifficultyValue()
        {
            double difficulty = 0;
            double time = 0;

            var strains = getReducedStrainPeaks();

            // Difficulty is a continuous weighted sum of the sorted strains
            foreach (StrainPeak strain in strains)
            {
                /* Weighting function can be thought of as:
                        b
                        ∫ DecayWeight^x dx
                        a
                    where a = startTime and b = endTime

                    Technically, the function below has been slightly modified from the equation above.
                    The real function would be
                        double weight = Math.Pow(DecayWeight, startTime) - Math.Pow(DecayWeight, endTime);
                        ...
                        return difficulty / Math.Log(1 / DecayWeight);
                    E.g. for a DecayWeight of 0.9, we're multiplying by 10 instead of 9.49122...

                    This change makes it so that a map composed solely of MaxSectionLength chunks will have the exact same value when summed in this class and StrainSkill.
                    Doing this ensures the relationship between strain values and difficulty values remains the same between the two classes.
                */
                double startTime = time;
                double endTime = time + strain.SectionLength / MaxSectionLength;

                double weight = Math.Pow(DecayWeight, startTime) - Math.Pow(DecayWeight, endTime);

                difficulty += strain.Value * weight;
                time = endTime;
            }

            return difficulty / (1 - DecayWeight);
        }

        /// <summary>
        /// Returns a sorted enumerable of strain peaks with the highest values reduced.
        /// </summary>
        /// <returns></returns>
        private IEnumerable<StrainPeak> getReducedStrainPeaks()
        {
            // Sections with 0 strain are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These sections will not contribute to the difficulty.
            var peaks = GetCurrentStrainPeaks().Where(p => p.Value > 0);

            List<StrainPeak> strains = peaks.OrderByDescending(p => p.Value).ToList();

            const int chunk_size = 20;
            double time = 0;
            int strainsToRemove = 0; // All strains are removed at the end for optimization purposes

            // We are reducing the highest strains first to account for extreme difficulty spikes
            // Strains are split into 20ms chunks to try to mitigate inconsistencies caused by reducing strains
            while (strains.Count > strainsToRemove && time < reduced_section_time)
            {
                StrainPeak strain = strains[strainsToRemove];

                for (double addedTime = 0; addedTime < strain.SectionLength; addedTime += chunk_size)
                {
                    double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((time + addedTime) / reduced_section_time, 0, 1)));

                    strains.Add(new StrainPeak(
                        strain.Value * Interpolation.Lerp(reduced_strain_baseline, 1.0, scale),
                        Math.Min(chunk_size, strain.SectionLength - addedTime)
                    ));
                }

                time += strain.SectionLength;
                strainsToRemove++;
            }

            strains.RemoveRange(0, strainsToRemove);

            return strains.OrderByDescending(p => p.Value);
        }
    }
}
