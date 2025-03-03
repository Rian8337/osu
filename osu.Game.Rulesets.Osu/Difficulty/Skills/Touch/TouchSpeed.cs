// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills.Touch
{
    public class TouchSpeed : TouchSkill
    {
        private double strainDecayBase => 0.3;

        private double currentStrain;
        private double currentRhythm;

        protected override int ReducedSectionCount => 5;

        private readonly double clockRate;

        public TouchSpeed(Mod[] mods, double clockRate)
            : base(mods)
        {
            this.clockRate = clockRate;
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => (currentStrain * currentRhythm) * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain = CalculateCurrentStrain(current);
            currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            double totalStrain = currentStrain * currentRhythm;

            return totalStrain;
        }

        protected override double GetProbabilityStrain(TouchHandSequenceProbability probability) => probability.Skills[1].CurrentStrain;

        protected override double GetProbabilityTotalStrain(TouchHandSequenceProbability probability) => CalculateTotalStrain(probability.Skills[0].CurrentStrain, GetProbabilityStrain(probability));

        protected override TouchHandSequenceSkill[] GetHandSequenceSkills() => new TouchHandSequenceSkill[]
        {
            new TouchHandSequenceAim(Mods, clockRate, true),
            new TouchHandSequenceSpeed(Mods, clockRate),
        };

        public double RelevantNoteCount()
        {
            if (ObjectStrains.Count == 0)
                return 0;

            double maxStrain = ObjectStrains.Max();

            if (maxStrain == 0)
                return 0;

            return ObjectStrains.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxStrain * 12.0 - 6.0))));
        }
    }
}
