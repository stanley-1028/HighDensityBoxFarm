using System;

namespace HDBF.Core.Formulas
{
    /// <summary>
    /// Smoke-test type for the build pipeline. Replaced by the real balance
    /// formulas; kept tiny so a broken toolchain fails loudly and early.
    /// </summary>
    public static class BuildProbe
    {
        public static float Clamp01(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
