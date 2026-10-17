namespace AndreGoepel.AppFoundation.Core;

internal static class SonarGateAcceptanceProbe
{
    internal static int Calculate(int value)
    {
        if (value < 0)
            return -1;

        if (value == 0)
            return 0;

        var result = 0;

        for (var index = 1; index <= Math.Min(value, 10); index++)
            result += index;

        return result;
    }
}
