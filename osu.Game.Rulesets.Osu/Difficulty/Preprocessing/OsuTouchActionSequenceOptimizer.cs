// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
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
        /// Controls the maximum number of sequences considered at once.
        /// As beam_width tends to infinity, the optimizer finds the true optimum.
        /// </summary>
        private const int beam_width = 20;

        private static readonly OsuTouchAction[] actions = [OsuTouchAction.Left, OsuTouchAction.Right, OsuTouchAction.Drag];

        /// <summary>
        /// Finds a touch action sequence that approximately minimizes difficulty and returns the resulting <see cref="OsuDifficultyHitObjectTouchData"/> for each <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public static List<OsuDifficultyHitObjectTouchData> FindTouchDataOfOptimalSequence(List<OsuDifficultyHitObject> objects, Mod[] mods)
        {
            if (objects.Count == 0) return [];

            // The first OsuDifficultyHitObject actually corresponds to the second object in the map, since they are constructed using two objects to compute jump distance.
            var firstHitObject = (OsuHitObject)objects[0].LastObject;

            // The first hit object must be tapped with either your left or right hand.
            // It cannot be dragged, since objects hit with drag are still assigned a hand corresponding to the most recent non-dragged object.
            var currentCandidates = new List<OsuTouchSequenceCandidate>
            {
                OsuTouchSequenceCandidate.CreateInitial(firstHitObject, OsuTouchHand.Right),
                OsuTouchSequenceCandidate.CreateInitial(firstHitObject, OsuTouchHand.Left)
            };

            for (int i = 0; i < objects.Count; i++)
            {
                var current = objects[i];
                var nextCandidates = new List<OsuTouchSequenceCandidate>(currentCandidates.Count * actions.Length);

                // Rhythm difficulty is independent of touch action sequence.
                // Since rhythm calc is computationally expensive, we compute it here instead of inside WithNextObjectHit() so that the result can be reused.
                double rhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

                foreach (var candidate in currentCandidates)
                {
                    foreach (var action in actions)
                    {
                        // Construct a new action sequence from the previous candidate, assuming that the current object was hit using action.
                        var nextCandidate = candidate.WithNextObjectHit(current, action, mods, rhythm);
                        nextCandidates.Add(nextCandidate);
                    }
                }

                // Only keep the top lowest star rating candidates.
                nextCandidates.Sort((a, b) => a.ApproximateSR.CompareTo(b.ApproximateSR));
                if (nextCandidates.Count > beam_width)
                    nextCandidates.RemoveRange(beam_width, nextCandidates.Count - beam_width);

                currentCandidates = nextCandidates;
            }

            return currentCandidates[0].GetTouchDataList();
        }

        /// <summary>
        /// Immutable snapshot of one possible touch action sequence up to some point in the beatmap.
        /// <see cref="WithNextObjectHit"/> produces a new candidate extended by one object and action, leaving the original unchanged.
        /// </summary>
        private sealed class OsuTouchSequenceCandidate
        {
            private const double winding_decay_base = 0.8;
            private const double pp_norm_exponent = 6;

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
            /// Tail of a reverse linked list storing <see cref="OsuDifficultyHitObjectTouchData"/>.
            /// Used to reconstruct a list of <see cref="OsuDifficultyHitObjectTouchData"/>  per object once the optimal sequence has been determined.
            /// </summary>
            private readonly SequenceNode? pathTail;

            /// <summary>
            /// Approximate SR of the sequence so far, used to rank and prune candidates during beam search.
            /// </summary>
            public readonly double ApproximateSR;

            private OsuTouchSequenceCandidate(
                HandHistory leftHistory,
                HandHistory rightHistory,
                OsuTouchAction lastAction,
                OsuTouchHand lastAimingHand,
                double? previousHandSeparationAngle,
                double accumulatedWinding,
                double aimStrain,
                double speedStrain,
                double approximateSR,
                SequenceNode? pathTail)
            {
                this.leftHistory = leftHistory;
                this.rightHistory = rightHistory;
                this.lastAction = lastAction;
                this.lastAimingHand = lastAimingHand;
                this.previousHandSeparationAngle = previousHandSeparationAngle;
                this.accumulatedWinding = accumulatedWinding;
                this.aimStrain = aimStrain;
                this.speedStrain = speedStrain;
                ApproximateSR = approximateSR;
                this.pathTail = pathTail;
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
                    approximateSR: 0,
                    pathTail: null);
            }

            /// <summary>
            /// Returns a new candidate representing this <see cref="OsuTouchSequenceCandidate"/> extended to cover <paramref name="current"/> hit with <paramref name="action"/>.
            /// </summary>
            public OsuTouchSequenceCandidate WithNextObjectHit(OsuDifficultyHitObject current, OsuTouchAction action, Mod[] mods, double rhythm)
            {
                // Determine which hand is aiming the current object.
                OsuTouchHand aimingHand = action.IsDrag ? lastAimingHand : ((OsuTouchAction.OsuHandAction)action).Hand;

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

                // 1.5 is an approximate relation between strain values and PP, since SR ~ sqrt(sum of weighted strains) and PP ~ SR^3
                double totalStrain = DifficultyCalculationUtils.Norm(1.5, newAimStrain, currentSpeedStrain);

                // The true SR is the sum of weighted section peaks, which is computationally expensive to compute.
                // Using a power norm is a reasonable enough approximation for beam search.
                double newApproximateSR = DifficultyCalculationUtils.Norm(pp_norm_exponent, ApproximateSR, totalStrain);

                return new OsuTouchSequenceCandidate(
                    newLeft, newRight, action, aimingHand,
                    nextHandSeparationAngle, nextAccumulatedWinding,
                    newAimStrain, newSpeedStrain, newApproximateSR,
                    new SequenceNode(touchData, pathTail));
            }

            /// <summary>
            /// Returns the <see cref="OsuDifficultyHitObjectTouchData"/> for each object in chronological order.
            /// </summary>
            public List<OsuDifficultyHitObjectTouchData> GetTouchDataList()
            {
                var list = new List<OsuDifficultyHitObjectTouchData>();
                for (SequenceNode? node = pathTail; node != null; node = node.Previous)
                    list.Add(node.TouchData);
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

                // Aim/speed evaluators need up to 2 previous per-hand objects for angle and velocity change calculations.
                var previousObjects = new List<DifficultyHitObject>(2);
                var lastLastPerHandDifficultyHitObject = (OsuDifficultyHitObject?)handHistory.LastPerHandDifficultyHitObject?.Previous(0);
                if (lastLastPerHandDifficultyHitObject != null) previousObjects.Add(lastLastPerHandDifficultyHitObject);
                if (handHistory.LastPerHandDifficultyHitObject != null) previousObjects.Add(handHistory.LastPerHandDifficultyHitObject);
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
                double crossing = computePathCrossing(handPos, otherPos, targetPos);
                double tanglingRisk = computeArmTangling(accumulatedWinding, separationAngleDelta);

                const double tangling_weight = 0.6;
                return Math.Pow(crossing + tangling_weight * tanglingRisk * (1.0 - crossing), 0.6);
            }

            /// <summary>
            /// Returns how much the other hand lies directly in the path to the target, as a value in [0, 1].
            /// </summary>
            private static double computePathCrossing(Vector2 handPos, Vector2 otherPos, Vector2 targetPos)
            {
                Vector2 movement = targetPos - handPos;
                float movementLengthSq = movement.LengthSquared;

                if (movementLengthSq <= 1e-6f)
                    return 0;

                float t = Math.Clamp(Vector2.Dot(otherPos - handPos, movement) / movementLengthSq, 0f, 1f);
                Vector2 closestOnSegment = handPos + t * movement;
                float distance = (otherPos - closestOnSegment).Length;

                const double proximity_sigma = 100;
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
            public Vector2 GetLastCursorPosition() => LastPerHandDifficultyHitObject?.GetEndCursorPosition() ?? LastHit!.StackedPosition;
        }

        private sealed class SequenceNode
        {
            public readonly OsuDifficultyHitObjectTouchData TouchData;
            public readonly SequenceNode? Previous;

            public SequenceNode(OsuDifficultyHitObjectTouchData touchData, SequenceNode? previous)
            {
                TouchData = touchData;
                Previous = previous;
            }
        }
    }
}
