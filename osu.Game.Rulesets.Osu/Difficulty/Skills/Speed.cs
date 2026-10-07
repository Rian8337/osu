// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Aggregation;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : Skill
    {
        private readonly List<double> sliderStrains = new List<double>();
        private readonly IReadOnlyList<double>? rhythms;

        private double currentStrain;
        private double harmonicWeightSum;

        public Speed(Mod[] mods, IReadOnlyList<double>? rhythms = null)
            : base(mods)
        {
            this.rhythms = rhythms;
        }

        private static double strainDecay(double ms) => DiffUtils.Pow(0.3, ms / 1000);

        /// <summary>
        /// Computes the next speed strain by applying decay and evaluating the current object's speed difficulty.
        /// </summary>
        public static double AdvanceStrainState(double currentStrain, IReadOnlyList<Mod> mods, DifficultyHitObject current)
        {
            const double skill_multiplier = 66.2;

            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            return currentStrain * decay + calculateAdjustedDifficulty(current, mods) * (1 - decay) * skill_multiplier;
        }

        /// <summary>
        /// Scales the current speed strain by a rhythm multiplier to produce the final strain value.
        /// </summary>
        public static double ComputeOverallStrain(double currentStrain, double rhythm) => currentStrain * rhythm;

        protected override double ProcessInternal(DifficultyHitObject current)
        {
            if (Mods.Any(m => m is OsuModRelax))
                return 0;

            currentStrain = AdvanceStrainState(currentStrain, Mods, current);

            double currentRhythm = rhythms?[current.Index] ?? RhythmEvaluator.EvaluateDifficultyOf(current);

            double totalStrain = ComputeOverallStrain(currentStrain, currentRhythm);

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalStrain);

            return totalStrain;
        }

        private static double calculateAdjustedDifficulty(DifficultyHitObject current, IReadOnlyList<Mod> mods)
        {
            double difficulty = mods.Any(m => m is OsuModTouchDevice)
                ? TouchSpeedEvaluator.EvaluateDifficultyOf(current)
                : SpeedEvaluator.EvaluateDifficultyOf(current);

            if (mods.Any(m => m is OsuModAutopilot))
                difficulty *= 0.5;

            return difficulty;
        }

        public override double DifficultyValue()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            (double difficulty, harmonicWeightSum) = HarmonicSeries.Aggregate(ObjectDifficulties, harmonicScale: 20);

            return difficulty;
        }

        public double RelevantObjectCount()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            double maxStrain = ObjectDifficulties.Max();

            if (maxStrain == 0)
                return 0;

            return ObjectDifficulties.Sum(strain => DiffUtils.Logistic(strain / maxStrain, 0.5, 12.0));
        }

        public virtual double CountTopWeightedObjectDifficulties(double difficultyValue)
        {
            if (ObjectDifficulties.Count == 0)
                return 0.0;

            if (harmonicWeightSum == 0)
                return 0.0;

            double consistentTopObject = difficultyValue / harmonicWeightSum; // What would the top difficulty be if all object difficulties were identical

            if (consistentTopObject == 0)
                return 0;

            return ObjectDifficulties.Sum(d => DiffUtils.Logistic(d / consistentTopObject, 0.88, 10, 1.1));
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            if (harmonicWeightSum == 0)
                return 0.0;

            double consistentTopObject = difficultyValue / harmonicWeightSum; // What would the top note be if all note values were identical

            if (consistentTopObject == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DiffUtils.Logistic(s / consistentTopObject, 0.88, 10, 1.1));
        }
    }
}
