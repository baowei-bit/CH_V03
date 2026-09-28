namespace CH_V03.Core.Airtightness;

public sealed record PressureSample(double Seconds, double Channel1, double Channel2, double Leak1, double Leak2, string Stage);

public static class AirtightnessSimulation
{
    public static double Duration(AirtightnessConfiguration c) => (double)c.FillSeconds + (double)c.BalanceSeconds + (double)c.TestSeconds + (double)c.ExhaustSeconds;

    public static PressureSample Sample(AirtightnessConfiguration c, double seconds, bool failChannel2)
    {
        var fill = (double)c.FillSeconds;
        var balanceEnd = fill + (double)c.BalanceSeconds;
        var testEnd = balanceEnd + (double)c.TestSeconds;
        var total = Duration(c);
        var target = ((double)c.MinimumPressureKpa + (double)c.MaximumPressureKpa) / 2;
        var leak1 = (double)c.MaximumLeakPa * 0.55;
        var leak2 = failChannel2 ? Math.Max((double)c.MaximumLeakPa * 1.3, (double)c.MaximumLeakPa + 1) : (double)c.MaximumLeakPa * 0.75;
        var t = Math.Clamp(seconds, 0, total);
        var progress = Math.Clamp((t - balanceEnd) / (double)c.TestSeconds, 0, 1);
        var scale = t >= total ? 0 : t < fill ? t / fill : t > testEnd ? Math.Max(0, 1 - (t - testEnd) / (double)c.ExhaustSeconds) : 1;
        var stage = t < fill ? "充气" : t < balanceEnd ? "平衡" : t < testEnd ? "测试" : t < total ? "泄气" : "完成";
        return new(t, Math.Max(0, target - leak1 * progress / 1000) * scale,
            Math.Max(0, target - leak2 * progress / 1000) * scale, leak1 * progress, leak2 * progress, stage);
    }
}
