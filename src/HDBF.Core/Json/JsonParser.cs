using System;
using System.Collections.Generic;
using System.Globalization;

namespace HDBF.Core.Json
{

    /// <summary>
    /// Hand-written non-throwing JSON parser (RFC 8259 subset) for untrusted player config.
    ///
    /// Why hand-written: HDBF.Core may not reference Newtonsoft/System.Text.Json (REQ-ARCH-01,
    /// no NuGet on the build machine), and the config loader needs a total function that returns
    /// defaults instead of throwing on garbage (chapter 10.3, REQ-SAFE-10).
    /// </summary>
    internal static partial class JsonParser
    {
        /// <summary>Nesting limit that keeps malicious input from exhausting the stack.</summary>
        private const int MaxDepth = 64;

        /// <summary>
        /// Parses <paramref name="text"/>; returns false for any malformed or over-deep input.
        /// Never throws.
        /// </summary>
        public static bool TryParse(string? text, out JsonValue? value)
        {
            value = null;
            if (text is null)
            {
                return false;
            }

            try
            {
                var parser = new Parser(text);
                var parsed = parser.ParseValue(0);
                parser.SkipWhitespace();
                if (parser.AtEnd && parsed is not null)
                {
                    value = parsed;
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                // Total function by contract: bad input is reported, never thrown (REQ-SAFE-10).
                value = null;
                return false;
            }
        }

    }
}