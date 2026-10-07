// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    /// <summary>
    /// Finds a sequence of <see cref="OsuTouchAction"/>s (left hand, right hand, or drag) that approximately minimizes SR/PP across the beatmap using beam search.
    /// </summary>
    public static class OsuTouchActionSequenceOptimizer
    {
        /// <summary>
        /// Controls the maximum number of sequences considered at once in each pass.
        /// </summary>
        private static readonly int[] beam_widths = [5, 15];

        /// <summary>
        /// Number of previous per-hand objects kept during beam search, since keeping the full history is computationally expensive.
        /// </summary>
        private const int max_per_hand_history = 6;

        /// <summary>
        /// Swapping left and right hands gives the same difficulty, so every candidate starts with the right hand.
        /// </summary>
        private const OsuTouchHand first_hand = OsuTouchHand.Right;

        private static readonly OsuTouchAction[] actions = [OsuTouchAction.Left, OsuTouchAction.Right, OsuTouchAction.Drag];

        /// <summary>
        /// Finds a touch action sequence that approximately minimizes difficulty and returns the resulting <see cref="OsuDifficultyHitObjectTouchData"/> for each <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public static List<OsuDifficultyHitObjectTouchData> FindTouchDataOfOptimalSequence(List<OsuDifficultyHitObject> objects, Mod[] mods)
        {
            if (objects.Count == 0) return [];

            // Rhythm difficulty is independent of touch action sequence.
            // Since rhythm calc is computationally expensive, we compute it here instead of inside WithNextObjectHit() so that the result can be reused.
            double[] rhythms = new double[objects.Count];
            for (int i = 0; i < objects.Count; i++)
                rhythms[i] = RhythmEvaluator.EvaluateDifficultyOf(objects[i]);

            // Start with dragging every object, which is the same as playing with a mouse.
            var best = OsuTouchSequenceCandidate.CreateInitial((OsuHitObject)objects[0].LastObject, first_hand);
            for (int i = 0; i < objects.Count; i++)
                best = best.WithNextObjectHit(objects[i], OsuTouchAction.Drag, mods, rhythms[i]);

            var bestTouchData = rebuildPerHandObjects(objects, best.GetTouchDataList(), first_hand);
            var bestDifficulty = calculateDifficulty(objects, bestTouchData, mods, rhythms);

            // Use the best sequence found so far as the reference for estimating performance.
            foreach (int beamWidth in beam_widths)
            {
                var candidate = search(objects, mods, rhythms, new PerformanceEstimator(best, bestDifficulty), beamWidth);
                var touchData = rebuildPerHandObjects(objects, candidate.GetTouchDataList(), first_hand);
                var difficulty = calculateDifficulty(objects, touchData, mods, rhythms);

                if (difficulty.Performance >= bestDifficulty.Performance)
                    continue;

                best = candidate;
                bestTouchData = touchData;
                bestDifficulty = difficulty;
            }

            return bestTouchData;
        }

        /// <summary>
        /// Finds the touch action sequence with the lowest estimated performance using beam search.
        /// </summary>
        private static OsuTouchSequenceCandidate search(List<OsuDifficultyHitObject> objects, Mod[] mods, double[] rhythms, PerformanceEstimator estimator, int beamWidth)
        {
            // The first OsuDifficultyHitObject actually corresponds to the second object in the map, since they are constructed using two objects to compute jump distance.
            var firstHitObject = (OsuHitObject)objects[0].LastObject;

            // The first hit object must be tapped with either your left or right hand.
            // It cannot be dragged, since objects hit with drag are still assigned a hand corresponding to the most recent non-dragged object.
            var currentCandidates = new List<OsuTouchSequenceCandidate>
            {
                OsuTouchSequenceCandidate.CreateInitial(firstHitObject, first_hand)
            };

            foreach (var current in objects)
            {
                var nextCandidates = new List<OsuTouchSequenceCandidate>(currentCandidates.Count * actions.Length);

                foreach (var candidate in currentCandidates)
                {
                    foreach (var action in actions)
                    {
                        // Construct a new action sequence from the previous candidate, assuming that the current object was hit using action.
                        var nextCandidate = candidate.WithNextObjectHit(current, action, mods, rhythms[current.Index]);
                        nextCandidates.Add(nextCandidate);
                    }
                }

                // Only keep the candidates with the lowest estimated performance.
                var bestCandidate = currentCandidates[0];
                currentCandidates = nextCandidates.OrderBy(c => estimator.EstimatePerformance(c, bestCandidate, current.Index)).Take(beamWidth).ToList();
            }

            return currentCandidates[0];
        }

        /// <summary>
        /// Calculates the difficulty values of <paramref name="objects"/> when hit using <paramref name="touchData"/>.
        /// </summary>
        private static SequenceDifficulty calculateDifficulty(List<OsuDifficultyHitObject> objects, List<OsuDifficultyHitObjectTouchData> touchData, Mod[] mods, double[] rhythms)
        {
            var aim = new Aim(mods, true);
            var speed = new Speed(mods, rhythms);
            var reading = new Reading(mods);

            // The first hit object of the beatmap does not have a corresponding OsuDifficultyHitObject.
            var flashlight = mods.Any(m => m is OsuModFlashlight) ? new Flashlight(mods, objects.Count + 1) : null;

            for (int i = 0; i < objects.Count; i++)
                objects[i].TouchData = touchData[i];

            foreach (var obj in objects)
            {
                aim.Process(obj);
                speed.Process(obj);
                reading.Process(obj);
                flashlight?.Process(obj);
            }

            foreach (var obj in objects)
                obj.TouchData = null;

            return new SequenceDifficulty(aim.DifficultyValue(), speed.DifficultyValue(), reading.DifficultyValue(), flashlight?.DifficultyValue() ?? 0);
        }

        private readonly record struct SequenceDifficulty(double Aim, double Speed, double Reading, double Flashlight)
        {
            public double Performance => OsuDifficultyCalculator.CalculateBasePerformance(
                OsuDifficultyCalculator.CalculateAimDifficultyRating(Aim),
                OsuDifficultyCalculator.CalculateDifficultyRating(Speed),
                OsuDifficultyCalculator.CalculateDifficultyRating(Reading),
                OsuDifficultyCalculator.CalculateDifficultyRating(Flashlight));
        }

        /// <summary>
        /// Estimates the final performance of a partial sequence using a reference sequence.
        /// </summary>
        private sealed class PerformanceEstimator
        {
            private readonly List<OsuTouchSequenceCandidate> reference;
            private readonly SequenceDifficulty referenceDifficulty;
            private readonly double aimScale;
            private readonly double speedScale;

            public PerformanceEstimator(OsuTouchSequenceCandidate reference, SequenceDifficulty referenceDifficulty)
            {
                this.reference = reference.GetHistory();
                this.referenceDifficulty = referenceDifficulty;

                aimScale = reference.AimPowerSum > 0 ? referenceDifficulty.Aim / DiffUtils.Pow(reference.AimPowerSum, 1 / OsuTouchSequenceCandidate.AIM_EXPONENT) : 0;
                speedScale = reference.SpeedPowerSum > 0 ? referenceDifficulty.Speed / DiffUtils.Pow(reference.SpeedPowerSum, 1 / OsuTouchSequenceCandidate.SPEED_EXPONENT) : 0;
            }

            public double EstimatePerformance(OsuTouchSequenceCandidate candidate, OsuTouchSequenceCandidate bestCandidate, int index)
            {
                var referenceEnd = reference[^1];
                var referenceCurrent = reference[index];
                var referencePrevious = index > 0 ? reference[index - 1] : null;

                // Since we cannot know the rest of the sequence yet, assume the remaining objects improve on the reference by the same ratio as the best candidate has so far.
                double aimRatio = referencePrevious?.AimPowerSum > 0 ? bestCandidate.AimPowerSum / referencePrevious.AimPowerSum : 1;
                double speedRatio = referencePrevious?.SpeedPowerSum > 0 ? bestCandidate.SpeedPowerSum / referencePrevious.SpeedPowerSum : 1;

                double aimPowerSum = candidate.AimPowerSum + aimRatio * (referenceEnd.AimPowerSum - referenceCurrent.AimPowerSum);
                double speedPowerSum = candidate.SpeedPowerSum + speedRatio * (referenceEnd.SpeedPowerSum - referenceCurrent.SpeedPowerSum);

                double aim = aimScale * DiffUtils.Pow(aimPowerSum, 1 / OsuTouchSequenceCandidate.AIM_EXPONENT);
                double speed = speedScale * DiffUtils.Pow(speedPowerSum, 1 / OsuTouchSequenceCandidate.SPEED_EXPONENT);

                return new SequenceDifficulty(aim, speed, referenceDifficulty.Reading, referenceDifficulty.Flashlight).Performance;
            }
        }

        /// <summary>
        /// Rebuilds the per-hand objects of the chosen sequence, since they only keep a limited history during beam search.
        /// </summary>
        private static List<OsuDifficultyHitObjectTouchData> rebuildPerHandObjects(List<OsuDifficultyHitObject> objects, List<OsuDifficultyHitObjectTouchData> touchDataList, OsuTouchHand firstHand)
        {
            var firstHitObject = (OsuHitObject)objects[0].LastObject;

            var leftObjects = new List<DifficultyHitObject>();
            var rightObjects = new List<DifficultyHitObject>();
            OsuHitObject? lastLeftHit = firstHand == OsuTouchHand.Left ? firstHitObject : null;
            OsuHitObject? lastRightHit = firstHand == OsuTouchHand.Right ? firstHitObject : null;

            var rebuiltTouchDataList = new List<OsuDifficultyHitObjectTouchData>(touchDataList.Count);

            for (int i = 0; i < objects.Count; i++)
            {
                var current = objects[i];
                var touchData = touchDataList[i];

                bool isLeft = touchData.AimingHand == OsuTouchHand.Left;
                var handObjects = isLeft ? leftObjects : rightObjects;
                OsuHitObject? lastHit = isLeft ? lastLeftHit : lastRightHit;

                OsuDifficultyHitObject? perHandObject = null;

                if (lastHit != null)
                {
                    perHandObject = new OsuDifficultyHitObject(current.BaseObject, lastHit, current.ClockRate, handObjects, handObjects.Count);
                    handObjects.Add(perHandObject);
                }

                if (isLeft)
                    lastLeftHit = (OsuHitObject)current.BaseObject;
                else
                    lastRightHit = (OsuHitObject)current.BaseObject;

                rebuiltTouchDataList.Add(touchData with { PerHandObject = perHandObject });
            }

            return rebuiltTouchDataList;
        }

        /// <summary>
        /// Immutable snapshot of one possible touch action sequence up to some point in the beatmap.
        /// <see cref="WithNextObjectHit"/> produces a new candidate extended by one object and action, leaving the original unchanged.
        /// </summary>
        private sealed class OsuTouchSequenceCandidate
        {
            private const double winding_decay_base = 0.8;

            public const double AIM_EXPONENT = 6;
            public const double SPEED_EXPONENT = 4;

            private readonly HandHistory leftHistory;
            private readonly HandHistory rightHistory;

            private readonly OsuTouchAction lastAction;

            /// <summary>
            /// Most recent hand used for a non-drag action.
            /// </summary>
            private readonly OsuTouchHand lastAimingHand;

            /// <summary>
            /// Angle of the vector from the previous left hand position to the right hand position.
            /// </summary>
            private readonly double? previousHandSeparationAngle;

            /// <summary>
            /// Accumulated rotation of <see cref="previousHandSeparationAngle"/>, exponentially decayed over time.
            /// Used to determine if arms end up intertwining over each other.
            /// </summary>
            private readonly double accumulatedWinding;

            private readonly double aimStrain;
            private readonly double speedStrain;

            /// <summary>
            /// The previous candidate in the sequence.
            /// Used to reconstruct a list of <see cref="OsuDifficultyHitObjectTouchData"/>  per object once the optimal sequence has been determined.
            /// </summary>
            private readonly OsuTouchSequenceCandidate? previous;

            /// <summary>
            /// The <see cref="OsuDifficultyHitObjectTouchData"/> of the most recently hit object.
            /// </summary>
            private readonly OsuDifficultyHitObjectTouchData lastTouchData;

            /// <summary>
            /// Sum of aim strains raised to <see cref="AIM_EXPONENT"/>, weighted by the time between objects.
            /// </summary>
            public readonly double AimPowerSum;

            /// <summary>
            /// Sum of speed strains raised to <see cref="SPEED_EXPONENT"/>.
            /// </summary>
            public readonly double SpeedPowerSum;

            private OsuTouchSequenceCandidate(
                HandHistory leftHistory,
                HandHistory rightHistory,
                OsuTouchAction lastAction,
                OsuTouchHand lastAimingHand,
                double? previousHandSeparationAngle,
                double accumulatedWinding,
                double aimStrain,
                double speedStrain,
                double aimPowerSum,
                double speedPowerSum,
                OsuTouchSequenceCandidate? previous,
                OsuDifficultyHitObjectTouchData lastTouchData)
            {
                this.leftHistory = leftHistory;
                this.rightHistory = rightHistory;
                this.lastAction = lastAction;
                this.lastAimingHand = lastAimingHand;
                this.previousHandSeparationAngle = previousHandSeparationAngle;
                this.accumulatedWinding = accumulatedWinding;
                this.aimStrain = aimStrain;
                this.speedStrain = speedStrain;
                AimPowerSum = aimPowerSum;
                SpeedPowerSum = speedPowerSum;
                this.previous = previous;
                this.lastTouchData = lastTouchData;
            }

            /// <summary>
            /// Creates the initial candidate assuming the first <see cref="OsuHitObject"/> of the beatmap was hit by <paramref name="hand"/>.
            /// </summary>
            public static OsuTouchSequenceCandidate CreateInitial(OsuHitObject first, OsuTouchHand hand)
            {
                HandHistory historyWithFirstObject = new HandHistory(lastHit: first, lastPerHandDifficultyHitObject: null);
                HandHistory left = hand == OsuTouchHand.Left ? historyWithFirstObject : default;
                HandHistory right = hand == OsuTouchHand.Right ? historyWithFirstObject : default;
                OsuTouchAction action = new OsuTouchAction.OsuHandAction(hand);

                // The strain for the first object is always zero.
                return new OsuTouchSequenceCandidate(
                    leftHistory: left,
                    rightHistory: right,
                    lastAction: action,
                    lastAimingHand: hand,
                    previousHandSeparationAngle: null,
                    accumulatedWinding: 0,
                    aimStrain: 0,
                    speedStrain: 0,
                    aimPowerSum: 0,
                    speedPowerSum: 0,
                    previous: null,
                    lastTouchData: default);
            }

            /// <summary>
            /// Returns a new candidate representing this <see cref="OsuTouchSequenceCandidate"/> extended to cover <paramref name="current"/> hit with <paramref name="action"/>.
            /// </summary>
            public OsuTouchSequenceCandidate WithNextObjectHit(OsuDifficultyHitObject current, OsuTouchAction action, Mod[] mods, double rhythm)
            {
                // Determine which hand is aiming the current object.
                OsuTouchHand aimingHand = action is OsuTouchAction.OsuHandAction handAction ? handAction.Hand : lastAimingHand;

                // Create a synthetic difficulty hit object with only hit objects that were hit by aimingHand.
                HandHistory aimingHandHistory = aimingHand == OsuTouchHand.Left ? leftHistory : rightHistory;
                OsuDifficultyHitObject? currentPerHandDifficultyHitObject = buildPerHandObject(current, aimingHandHistory);

                // Update histories to reflect that the current difficulty hit object was hit with aimingHand.
                HandHistory newHistory = new HandHistory(lastHit: (OsuHitObject)current.BaseObject, lastPerHandDifficultyHitObject: currentPerHandDifficultyHitObject);
                HandHistory newLeft = aimingHand == OsuTouchHand.Left ? newHistory : leftHistory;
                HandHistory newRight = aimingHand == OsuTouchHand.Right ? newHistory : rightHistory;

                // Compute the vector between hand positions to see how much hands have winded around each other.
                double? newSeparationAngle = computeHandSeparationAngle(newLeft, newRight);
                double separationAngleDelta = newSeparationAngle.HasValue && previousHandSeparationAngle.HasValue
                    ? Math.IEEERemainder(newSeparationAngle.Value - previousHandSeparationAngle.Value, 2 * Math.PI)
                    : 0;
                double? nextHandSeparationAngle = newSeparationAngle ?? previousHandSeparationAngle;
                double nextAccumulatedWinding = accumulatedWinding * windingDecay(current.AdjustedDeltaTime) + separationAngleDelta;

                double obstruction = getObstructionFactor(current, action, separationAngleDelta, aimingHand);

                OsuDifficultyHitObjectTouchData touchData = new OsuDifficultyHitObjectTouchData(
                    action, aimingHand, lastAction, lastAimingHand, currentPerHandDifficultyHitObject, obstruction);

                // Keep track of previous touch data so we can restore it later.
                // This should always be null if the pattern solver is only invoked once, but we keep track of it for good measure.
                var previousTouchData = current.TouchData;

                // Temporarily evaluate the strains of the current object as if it were completed with the given action.
                // IMPORTANT NOTE: strain evaluation should only depend on the current object's TouchData and not previous objects' touch data.
                // We do not set TouchData for the previous objects since doing so would require expensive deep clones and rewriting of object histories.
                current.TouchData = touchData;

                double newAimStrain = Aim.AdvanceStrainState(aimStrain, mods, current, includeSliders: true);
                double newSpeedStrain = Speed.AdvanceStrainState(speedStrain, mods, current);
                double currentSpeedStrain = Speed.ComputeOverallStrain(newSpeedStrain, rhythm);

                current.TouchData = previousTouchData;

                double newAimPowerSum = AimPowerSum + DiffUtils.Pow(newAimStrain, AIM_EXPONENT) * current.AdjustedDeltaTime;
                double newSpeedPowerSum = SpeedPowerSum + DiffUtils.Pow(currentSpeedStrain, SPEED_EXPONENT);

                return new OsuTouchSequenceCandidate(
                    newLeft, newRight, action, aimingHand,
                    nextHandSeparationAngle, nextAccumulatedWinding,
                    newAimStrain, newSpeedStrain, newAimPowerSum, newSpeedPowerSum,
                    this, touchData);
            }

            /// <summary>
            /// Returns the candidates in this sequence in chronological order.
            /// </summary>
            public List<OsuTouchSequenceCandidate> GetHistory()
            {
                var list = new List<OsuTouchSequenceCandidate>();
                for (var candidate = this; candidate.previous != null; candidate = candidate.previous)
                    list.Add(candidate);
                list.Reverse();
                return list;
            }

            /// <summary>
            /// Returns the <see cref="OsuDifficultyHitObjectTouchData"/> for each object in chronological order.
            /// </summary>
            public List<OsuDifficultyHitObjectTouchData> GetTouchDataList()
            {
                var list = new List<OsuDifficultyHitObjectTouchData>();
                for (var candidate = this; candidate.previous != null; candidate = candidate.previous)
                    list.Add(candidate.lastTouchData);
                list.Reverse();
                return list;
            }

            private static double windingDecay(double deltaTimeMs) =>
                Math.Pow(winding_decay_base, deltaTimeMs / 1000);

            private static double? computeHandSeparationAngle(HandHistory left, HandHistory right)
            {
                if (left.LastHit == null || right.LastHit == null)
                    return null;

                Vector2 leftPos = left.GetLastCursorPosition();
                Vector2 rightPos = right.GetLastCursorPosition();
                return Math.Atan2(rightPos.Y - leftPos.Y, rightPos.X - leftPos.X);
            }

            private static OsuDifficultyHitObject? buildPerHandObject(OsuDifficultyHitObject current, HandHistory handHistory)
            {
                if (handHistory.LastHit == null) return null;

                var previousObjects = new List<DifficultyHitObject>(max_per_hand_history);
                var lastPerHandDifficultyHitObject = handHistory.LastPerHandDifficultyHitObject;

                if (lastPerHandDifficultyHitObject != null)
                {
                    // Only keep the most recent per-hand objects.
                    for (int i = Math.Min(lastPerHandDifficultyHitObject.Index, max_per_hand_history - 1) - 1; i >= 0; i--)
                        previousObjects.Add(lastPerHandDifficultyHitObject.Previous(i));

                    previousObjects.Add(lastPerHandDifficultyHitObject);
                }

                return new OsuDifficultyHitObject(current.BaseObject, handHistory.LastHit, current.ClockRate, previousObjects, previousObjects.Count);
            }

            private double getObstructionFactor(OsuDifficultyHitObject target, OsuTouchAction action, double separationAngleDelta, OsuTouchHand aimingHand)
            {
                // Obstruction is only relevant during hand switches.
                bool isHandSwitch = !action.IsDrag && aimingHand != lastAimingHand;

                HandHistory aimingHistory = aimingHand == OsuTouchHand.Left ? leftHistory : rightHistory;
                HandHistory otherHistory = aimingHand == OsuTouchHand.Left ? rightHistory : leftHistory;

                if (!isHandSwitch || aimingHistory.LastHit == null || otherHistory.LastHit == null) return 0;

                Vector2 handPos = aimingHistory.GetLastCursorPosition(), otherPos = otherHistory.GetLastCursorPosition();
                Vector2 targetPos = ((OsuHitObject)target.BaseObject).StackedPosition;

                // Obstruction should consider two factors:
                // 1. How much the other hand is in the way of the path of the current hand
                // 2. How much the other arm has tangled around the current arm
                double scalingFactor = OsuDifficultyHitObject.NORMALISED_RADIUS / ((OsuHitObject)target.BaseObject).Radius;
                double crossing = computePathCrossing(handPos, otherPos, targetPos, scalingFactor);
                double tanglingRisk = computeArmTangling(accumulatedWinding, separationAngleDelta);

                const double tangling_weight = 0.6;
                return Math.Pow(crossing + tangling_weight * tanglingRisk * (1.0 - crossing), 0.6);
            }

            /// <summary>
            /// Returns how much the other hand lies directly in the path to the target, as a value in [0, 1].
            /// </summary>
            private static double computePathCrossing(Vector2 handPos, Vector2 otherPos, Vector2 targetPos, double scalingFactor)
            {
                Vector2 movement = targetPos - handPos;
                float movementLengthSq = movement.LengthSquared;

                if (movementLengthSq <= 1e-6f)
                    return 0;

                float t = Math.Clamp(Vector2.Dot(otherPos - handPos, movement) / movementLengthSq, 0f, 1f);
                Vector2 closestOnSegment = handPos + t * movement;
                double distance = (otherPos - closestOnSegment).Length * scalingFactor;

                // Roughly 100px at CS4, since the distance is scaled by circle size.
                const double proximity_sigma = 135;
                double proximity = Math.Exp(-distance * distance / (2.0 * proximity_sigma * proximity_sigma));
                double betweenness = 4.0 * t * (1.0 - t);
                return proximity * betweenness;
            }

            /// <summary>
            /// Returns how much the arms have been winding around each other, as a value in [0, 1].
            /// </summary>
            private static double computeArmTangling(double accumulatedWinding, double separationAngleDelta)
            {
                double absAccum = Math.Abs(accumulatedWinding);
                double absDelta = Math.Abs(separationAngleDelta);
                double windingAmount = Math.Min(absAccum / Math.PI, 1.0);

                const double delta_scale = Math.PI / 6;
                double deltaMag = Math.Min(absDelta / delta_scale, 1.0);

                const double eps = 1e-6;
                double denom = absAccum * absDelta;
                double align = denom < eps
                    ? 0.5
                    : 0.5 * (1.0 + (accumulatedWinding * separationAngleDelta) / denom);

                return windingAmount * deltaMag * align;
            }
        }

        /// <summary>
        /// Tracks a minimal historical state for objects hit with the same hand, used to construct synthetic <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        private readonly struct HandHistory
        {
            /// <summary>
            /// The most recently hit note on this hand.
            /// </summary>
            public readonly OsuHitObject? LastHit;

            /// <summary>
            /// Synthetic <see cref="OsuDifficultyHitObject"/> for the most recently hit note, or null on the first hit.
            /// We store this separately from <see cref="LastHit"/>, because <see cref="OsuDifficultyHitObject"/> cannot be constructed until two objects have been hit with the same hand.
            /// </summary>
            public readonly OsuDifficultyHitObject? LastPerHandDifficultyHitObject;

            public HandHistory(OsuHitObject? lastHit, OsuDifficultyHitObject? lastPerHandDifficultyHitObject)
            {
                LastHit = lastHit;
                LastPerHandDifficultyHitObject = lastPerHandDifficultyHitObject;
            }

            /// <summary>
            /// Returns the end cursor position of the last note hit on this hand.
            /// </summary>
            public Vector2 GetLastCursorPosition() => LastPerHandDifficultyHitObject?.LazyEndPosition ?? LastHit!.StackedPosition;
        }
    }
}
