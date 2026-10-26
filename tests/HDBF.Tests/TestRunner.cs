using System;
using System.Collections.Generic;
using System.Globalization;

namespace HDBF.Tests
{
    /// <summary>
    /// Minimal dependency-free assertion + discovery harness.
    ///
    /// Discovery: every public static class in this assembly with public static parameterless
    /// methods whose names start with "Test" is executed. Each method is an independent test;
    /// an exception means failure, and the runner reports the failing method plus its message.
    /// This keeps L1 tests runnable with <c>dotnet run --project tests/HDBF.Tests</c> on a
    /// machine with no NuGet access, which is a hard constraint here.
    /// </summary>
    public static class TestAssert
    {
        public static void True(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new TestFailureException(message ?? "expected true");
            }
        }

        public static void False(bool condition, string? message = null)
        {
            if (condition)
            {
                throw new TestFailureException(message ?? "expected false");
            }
        }

        public static void Equal(int expected, int actual, string? message = null)
        {
            if (expected != actual)
            {
                throw new TestFailureException(Format(message, $"expected {expected}, got {actual}"));
            }
        }

        public static void Equal(long expected, long actual, string? message = null)
        {
            if (expected != actual)
            {
                throw new TestFailureException(Format(message, $"expected {expected}, got {actual}"));
            }
        }

        public static void Equal(int? expected, int? actual, string? message = null)
        {
            if (expected != actual)
            {
                throw new TestFailureException(Format(message, $"expected {expected}, got {actual}"));
            }
        }

        public static void Equal(bool expected, bool actual, string? message = null)
        {
            if (expected != actual)
            {
                throw new TestFailureException(Format(message, $"expected {expected}, got {actual}"));
            }
        }

        public static void Equal<TEnum>(TEnum expected, TEnum actual, string? message = null)
            where TEnum : struct, Enum
        {
            if (!EqualityComparer<TEnum>.Default.Equals(expected, actual))
            {
                throw new TestFailureException(Format(message, $"expected {expected}, got {actual}"));
            }
        }

        public static void Equal(Guid expected, Guid actual, string? message = null)
        {
            if (expected != actual)
            {
                throw new TestFailureException(Format(message, $"expected {expected}, got {actual}"));
            }
        }

        public static void Equal(string expected, string? actual, string? message = null)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new TestFailureException(Format(message, $"expected \"{expected}\", got \"{actual}\""));
            }
        }

        public static void Equal(float expected, float actual, string? message)
        {
            Equal(expected, actual, 1e-4f, message);
        }

        public static void Equal(double expected, double actual, double tolerance = 1e-9d, string? message = null)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new TestFailureException(Format(message,
                    string.Format(CultureInfo.InvariantCulture, "expected {0} (±{1}), got {2}", expected, tolerance, actual)));
            }
        }

        public static void Equal(float expected, float actual, float tolerance = 1e-4f, string? message = null)
        {
            if (float.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new TestFailureException(Format(message,
                    string.Format(CultureInfo.InvariantCulture, "expected {0} (±{1}), got {2}", expected, tolerance, actual)));
            }
        }

        public static void NotNull(object? value, string? message = null)
        {
            if (value is null)
            {
                throw new TestFailureException(message ?? "expected non-null");
            }
        }

        public static void Null(object? value, string? message = null)
        {
            if (value is not null)
            {
                throw new TestFailureException(message ?? "expected null");
            }
        }

        public static void Throws<TException>(Action action, string? message = null)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception other)
            {
                throw new TestFailureException(Format(message,
                    $"expected {typeof(TException).Name}, got {other.GetType().Name}"));
            }

            throw new TestFailureException(Format(message, $"expected {typeof(TException).Name}, nothing thrown"));
        }

        private static string Format(string? message, string detail)
            => string.IsNullOrEmpty(message) ? detail : $"{message}: {detail}";
    }

    public sealed class TestFailureException : Exception
    {
        public TestFailureException(string message)
            : base(message)
        {
        }
    }

    public static class TestRunner
    {
        public static int RunAll()
        {
            var passed = 0;
            var failures = new List<string>();

            foreach (var type in typeof(TestRunner).Assembly.GetTypes())
            {
                if (!type.IsAbstract || !type.IsSealed || type.IsGenericTypeDefinition)
                {
                    // abstract+sealed == static class; only those are test containers
                    continue;
                }

                if (type == typeof(TestRunner) || type == typeof(TestAssert))
                {
                    continue;
                }

                var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Array.Sort(methods, (left, right) => string.CompareOrdinal(left.Name, right.Name));
                foreach (var method in methods)
                {
                    if (!method.Name.StartsWith("Test", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (method.GetParameters().Length != 0 || method.ReturnType != typeof(void))
                    {
                        failures.Add($"{type.Name}.{method.Name}: test method must be 'public static void Test*()'");
                        continue;
                    }

                    var label = $"{type.Name}.{method.Name}";
                    try
                    {
                        method.Invoke(null, null);
                        passed++;
                    }
                    catch (System.Reflection.TargetInvocationException ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        failures.Add($"{label}: {inner.GetType().Name}: {inner.Message}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"PASSED {passed}  FAILED {failures.Count}");
            foreach (var failure in failures)
            {
                Console.WriteLine("  FAIL " + failure);
            }

            if (failures.Count == 0 && passed == 0)
            {
                Console.WriteLine("  (no tests discovered - expected at least the balance suite)");
                return 2;
            }

            return failures.Count == 0 ? 0 : 1;
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine($"HDBF L1 test run - {DateTime.UtcNow:O}");
            return TestRunner.RunAll();
        }
    }
}
