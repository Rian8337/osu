// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public class Speed : HarmonicSkill
    {
        private const double skill_multiplier = 1.16;
        private const double strain_decay_base = 0.3;

        private readonly List<double> sliderStrains = new List<double>();

        private double currentStrain;

        protected override double HarmonicScale => 20;
        protected override double DecayExponent => 0.9;

        public Speed(Mod[] mods)
            : base(mods)
        {
        }

        private static double strainDecay(double ms) => Math.Pow(strain_decay_base, ms / 1000);

        /// <summary>
        /// Computes the next speed strain by applying decay and evaluating the current object's speed difficulty.
        /// </summary>
        public static double AdvanceStrainState(double currentStrain, IReadOnlyList<Mod> mods, DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            double speedDifficulty = mods.Any(m => m is OsuModTouchDevice)
                ? TouchSpeedEvaluator.EvaluateDifficultyOf(current)
                : SpeedEvaluator.EvaluateDifficultyOf(current);

            return currentStrain * decay + speedDifficulty * (1 - decay) * skill_multiplier;
        }

        /// <summary>
        /// Scales the current speed strain by a rhythm multiplier to produce the final strain value.
        /// </summary>
        public static double ComputeOverallStrain(double currentStrain, double rhythm) => currentStrain * rhythm;

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            currentStrain = AdvanceStrainState(currentStrain, Mods, current);

            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            double totalStrain = ComputeOverallStrain(currentStrain, currentRhythm);

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalStrain);

            return totalStrain;
        }

        public double RelevantNoteCount()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            double maxStrain = ObjectDifficulties.Max();

            if (maxStrain == 0)
                return 0;

            return ObjectDifficulties.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxStrain * 12.0 - 6.0))));
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            if (NoteWeightSum == 0)
                return 0.0;

            double consistentTopNote = difficultyValue / NoteWeightSum; // What would the top note be if all note values were identical

            if (consistentTopNote == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopNote, 0.88, 10, 1.1));
        }
    }
}
