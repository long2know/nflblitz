using System;
using System.Reflection;
using Blitz.Editor;

internal static class Program
{
    private const int ExpectedAssertions = 3568;

    private static int Main()
    {
        try
        {
            FieldInfo counter = typeof(NormalCameraChecks).GetField("assertions", BindingFlags.Static | BindingFlags.NonPublic);
            if (counter == null || counter.FieldType != typeof(int))
                throw new InvalidOperationException("The actual camera suite assertion counter is required.");
            NormalCameraChecks.Verify();
            int actual = (int)counter.GetValue(null);
            if (actual != ExpectedAssertions)
                throw new InvalidOperationException("Assertion inventory changed: expected " + ExpectedAssertions + ", actual " + actual);
            Console.WriteLine("PASS inventory: " + actual + " pure camera assertions; native Unity-only projection/HUD checks are not run.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
