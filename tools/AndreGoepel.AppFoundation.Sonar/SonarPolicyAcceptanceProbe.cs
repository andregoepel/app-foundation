namespace AndreGoepel.AppFoundation.Sonar;

internal static class SonarPolicyAcceptanceProbe
{
    internal static int Calculate(int value)
    {
        if (value < 0)
            return -1;

        if (value == 0)
            return 0;

        var total = 0;
        for (var current = 1; current <= Math.Min(value, 10); current++)
            total += current;

        return total;
    }
}
