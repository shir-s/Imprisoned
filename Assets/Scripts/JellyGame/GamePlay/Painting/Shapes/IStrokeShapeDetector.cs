// FILEPATH: Assets/Scripts/Painting/Shapes/IStrokeShapeDetector.cs

namespace JellyGame.GamePlay.Painting.Shapes
{
    /// <summary>
    /// Data for a single detected closed loop segment in the stroke history.
    /// Built by StrokeTrailAnalyzer and passed to all IStrokeShapeDetector
    /// instances so they do NOT have to redo basic path analysis.
    /// </summary>
    public struct StrokeLoopSegment
    {
        /// <summary>Full sliding stroke history.</summary>
        public StrokeHistory history;

        /// <summary>Inclusive start sample index of this loop in history.</summary>
        public int startIndex;

        /// <summary>Inclusive end sample index of this loop in history.</summary>
        public int endIndexInclusive;

        /// <summary>
        /// Simplified corner path for this loop (Medium/Sharp turns,
        /// already clustered & filtered by StrokePathBuilder).
        /// May be null or have 0 corners if analysis found nothing robust.
        /// </summary>
        public StrokePathLoop path;
    }

    /// <summary>
    /// A shape detector is a small plugin that checks a single closed stroke
    /// segment and optionally performs some effect (spawn ramp, etc.).
    ///
    /// Called by StrokeTrailAnalyzer when it finds a closed loop segment,
    /// after it has already built a simplified corner path.
    /// </summary>
    public interface IStrokeShapeDetector
    {
        /// <summary>
        /// Try to recognize & handle a shape in the given contiguous loop segment.
        ///
        /// loopSegment.history          = full stroke history
        /// loopSegment.startIndex       = first index of the loop
        /// loopSegment.endIndexInclusive= last index of the loop
        /// loopSegment.path             = precomputed corner path for this loop
        ///
        /// Return true if:
        /// - You recognized your shape, AND
        /// - You performed your effect (spawn, etc.), AND
        /// - You want the analyzer to CONSUME this segment from history.
        ///
        /// Return false if:
        /// - This segment is not your shape, or
        /// - You choose not to handle it (no side effects).
        /// </summary>
        bool TryHandleShape(StrokeLoopSegment loopSegment);
    }
}