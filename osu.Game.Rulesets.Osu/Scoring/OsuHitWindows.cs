﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Scoring
{
    public class OsuHitWindows : HitWindows
    {
        private static readonly DifficultyRange[] osu_ranges =
        {
            new DifficultyRange(HitResult.Great, 100, 75, 50),
            new DifficultyRange(HitResult.Ok, 200, 150, 100),
            new DifficultyRange(HitResult.Meh, 300, 250, 200),
            new DifficultyRange(HitResult.Miss, 400, 400, 400),
        };

        public override bool IsHitResultAllowed(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great:
                case HitResult.Ok:
                case HitResult.Meh:
                case HitResult.Miss:
                    return true;
            }

            return false;
        }

        protected override DifficultyRange[] GetRanges() => osu_ranges;
    }
}
