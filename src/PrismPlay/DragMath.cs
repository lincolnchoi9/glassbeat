namespace PrismPlay;
public static class DragMath
{
    // A short, frame-rate-independent response; no overshoot or prolonged trailing.
    public static double Step(double current, double target, double elapsedSeconds, bool reducedMotion) =>
        reducedMotion ? target : current + (target - current) * (1 - Math.Exp(-Math.Clamp(elapsedSeconds, 0, .1) / .024));
    // Critically damped spring for the window region, analytic so a retarget restarts from the sampled position and velocity:
    // x(t) = target + (d0 + (v0 + ω·d0)·t)·e^(−ωt), d0 = x0 − target. The same curve is --spring in frontend/src/style.css.
    public const double SpringOmega = 2 * Math.PI / .42;
    public static (double X, double V) Spring(double x0, double v0, double target, double t)
    {
        double d0 = x0 - target, b = v0 + SpringOmega * d0, e = Math.Exp(-SpringOmega * t);
        return (target + (d0 + b * t) * e, (b - SpringOmega * (d0 + b * t)) * e);
    }
}
