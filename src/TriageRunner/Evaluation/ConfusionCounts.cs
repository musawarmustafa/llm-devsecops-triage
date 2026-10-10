namespace TriageRunner.Evaluation;

/// <summary>
/// Confusion-matrix counts for one group of findings, with the derived metrics.
///
/// The positive class is "TP" (a genuine security issue):
///   TruePos  = model said TP, ground truth TP
///   FalseNeg = model said FP (or anything else), ground truth TP   (a real issue was missed)
///   FalsePos = model said TP, ground truth FP                     (noise was escalated)
///   TrueNeg  = model said FP (or anything else), ground truth FP
///
/// Metrics with a zero denominator are reported as 0.
/// </summary>
public sealed class ConfusionCounts
{
    public int TruePos { get; private set; }
    public int FalseNeg { get; private set; }
    public int FalsePos { get; private set; }
    public int TrueNeg { get; private set; }

    public int Total => TruePos + FalseNeg + FalsePos + TrueNeg;

    public double Precision => Ratio(TruePos, TruePos + FalsePos);
    public double Recall => Ratio(TruePos, TruePos + FalseNeg);
    public double F1 => Ratio(2 * TruePos, 2 * TruePos + FalsePos + FalseNeg);
    public double Accuracy => Ratio(TruePos + TrueNeg, Total);

    public void Add(bool predictedPositive, bool actuallyPositive)
    {
        switch (predictedPositive, actuallyPositive)
        {
            case (true, true): TruePos++; break;
            case (false, true): FalseNeg++; break;
            case (true, false): FalsePos++; break;
            case (false, false): TrueNeg++; break;
        }
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : (double)numerator / denominator;
}
