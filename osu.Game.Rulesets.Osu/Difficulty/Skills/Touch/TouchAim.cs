// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills.Touch
{
    public class TouchAim : TouchSkill
    {
        public readonly bool IncludeSliders;
        private readonly double clockRate;

        private double strainDecayBase => 0.15;

        private double currentStrain;
        private readonly List<double> sliderStrains = new List<double>();

        public TouchAim(Mod[] mods, double clockRate, bool includeSliders)
            : base(mods)
        {
            this.clockRate = clockRate;
            IncludeSliders = includeSliders;
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain = CalculateCurrentStrain(current);

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentStrain);

            return currentStrain;
        }

        protected override double GetProbabilityStrain(TouchHandSequenceProbability probability) => probability.Skills[0].CurrentStrain;

        protected override double GetProbabilityTotalStrain(TouchHandSequenceProbability probability) => CalculateTotalStrain(GetProbabilityStrain(probability), probability.Skills[1].CurrentStrain);

        protected override TouchHandSequenceSkill[] GetHandSequenceSkills() => new TouchHandSequenceSkill[]
        {
            new TouchHandSequenceAim(Mods, clockRate, IncludeSliders),
            new TouchHandSequenceSpeed(Mods, clockRate),
        };

        public double GetDifficultSliders()
        {
            if (sliderStrains.Count == 0)
                return 0;

            double maxSliderStrain = sliderStrains.Max();
            if (maxSliderStrain == 0)
                return 0;

            return sliderStrains.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxSliderStrain * 12.0 - 6.0))));
        }
    }
}
