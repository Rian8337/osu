// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyCalculator : DifficultyCalculator
    {
        private const double difficulty_multiplier = 0.18;
        private double hitWindowGreat;

        public override int Version => 20220902;

        public OsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
        {
            if (beatmap.HitObjects.Count == 0)
                return new OsuDifficultyAttributes { Mods = mods };

            double aimRating = Math.Sqrt(skills[0].DifficultyValue()) * difficulty_multiplier;
            double aimRatingNoSliders = Math.Sqrt(skills[1].DifficultyValue()) * difficulty_multiplier;
            double tapRating = Math.Sqrt(skills[2].DifficultyValue()) * difficulty_multiplier;
            double speedNotes = ((Tap)skills[2]).RelevantNoteCount();
            double rhythmRating = Math.Sqrt(skills[3].DifficultyValue()) * difficulty_multiplier;
            double flashlightRating = Math.Sqrt(skills[4].DifficultyValue()) * difficulty_multiplier;
            double visualRating = Math.Sqrt(skills[5].DifficultyValue()) * difficulty_multiplier;

            double sliderFactor = aimRating > 0 ? aimRatingNoSliders / aimRating : 1;

            if (mods.Any(h => h is OsuModRelax))
            {
                aimRating *= 0.9;
                tapRating = 0.0;
                rhythmRating = 0.0;
                flashlightRating *= 0.7;
                visualRating = 0.0;
            }

            double baseAimPerformance = Math.Pow(5 * Math.Max(1, Math.Pow(aimRating, 0.8) / 0.0675) - 4, 3) / 100000;
            double baseTapPerformance = Math.Pow(5 * Math.Max(1, tapRating / 0.0675) - 4, 3) / 100000;
            double baseFlashlightPerformance = 0.0;
            double baseVisualPerformance = Math.Pow(visualRating, 1.6) * 22.5;

            if (mods.Any(h => h is OsuModFlashlight))
                baseFlashlightPerformance = Math.Pow(flashlightRating, 1.6) * 25.0;

            double basePerformance =
                Math.Pow(
                    Math.Pow(baseAimPerformance, 1.1) +
                    Math.Pow(baseTapPerformance, 1.1) +
                    Math.Pow(baseFlashlightPerformance, 1.1) +
                    Math.Pow(baseVisualPerformance, 1.1), 1.0 / 1.1
                );

            double starRating = basePerformance > 0.00001 ? 0.027 * (Math.Cbrt(100000 / Math.Pow(2, 1 / 1.1) * basePerformance) + 4) : 0;

            double preempt = IBeatmapDifficultyInfo.DifficultyRange(beatmap.Difficulty.ApproachRate, 1800, 1200, 450) / clockRate;
            double drainRate = beatmap.Difficulty.DrainRate;
            int maxCombo = beatmap.GetMaxCombo();

            int hitCirclesCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);

            HitWindows hitWindows;

            if (mods.Any(m => m is OsuModPrecise))
                hitWindows = new OsuPreciseHitWindows();
            else
                hitWindows = new OsuHitWindows();

            hitWindows.SetDifficulty(beatmap.Difficulty.OverallDifficulty);

            double hitWindowGreat = hitWindows.WindowFor(HitResult.Great) / clockRate;

            OsuDifficultyAttributes attributes = new OsuDifficultyAttributes
            {
                StarRating = starRating,
                Mods = mods,
                AimDifficulty = aimRating,
                TapDifficulty = tapRating,
                SpeedNoteCount = speedNotes,
                RhythmDifficulty = rhythmRating,
                FlashlightDifficulty = flashlightRating,
                VisualDifficulty = visualRating,
                SliderFactor = sliderFactor,
                ApproachRate = preempt > 1200 ? (1800 - preempt) / 120 : (1200 - preempt) / 150 + 5,
                OverallDifficulty = (80 - hitWindowGreat) / 6,
                DrainRate = drainRate,
                MaxCombo = maxCombo,
                HitCircleCount = hitCirclesCount,
                SliderCount = sliderCount,
                SpinnerCount = spinnerCount,
            };

            return attributes;
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
        {
            List<DifficultyHitObject> objects = new List<DifficultyHitObject>();

            // The first jump is formed by the first two hitobjects of the map.
            // If the map has less than two OsuHitObjects, the enumerator will not return anything.
            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                var lastLast = i > 1 ? beatmap.HitObjects[i - 2] : null;
                objects.Add(new OsuDifficultyHitObject(beatmap.HitObjects[i], beatmap.HitObjects[i - 1], lastLast, clockRate, objects, objects.Count));
            }

            return objects;
        }

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate)
        {
            HitWindows hitWindows;

            if (mods.Any(m => m is OsuModPrecise))
                hitWindows = new OsuPreciseHitWindows();
            else
                hitWindows = new OsuHitWindows();

            hitWindows.SetDifficulty(beatmap.Difficulty.OverallDifficulty);

            hitWindowGreat = hitWindows.WindowFor(HitResult.Great) / clockRate;

            return new Skill[]
            {
                new Aim(mods, true),
                new Aim(mods, false),
                new Tap(mods, hitWindowGreat),
                new Rhythm(mods, hitWindowGreat),
                new Flashlight(mods),
                new Visual(mods, hitWindowGreat)
            };
        }

        protected override Mod[] DifficultyAdjustmentMods => new Mod[]
        {
            new OsuModDoubleTime(),
            new OsuModHalfTime(),
            new OsuModEasy(),
            new OsuModHardRock(),
            new OsuModFlashlight(),
            new MultiMod(new OsuModFlashlight(), new OsuModHidden()),
            new OsuModPrecise(),
            new OsuModHidden()
        };
    }
}
