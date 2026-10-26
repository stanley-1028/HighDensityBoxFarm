// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-SAVE-01
// Purpose: Total (never-throwing) readers of the keyed record: each key is read behind a Has plus
//          try/catch guard, so a missing/mistyped value degrades to a default and marks damage.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-06, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using Timberborn.Persistence;

namespace HDBF.Persistence.Indoor
{
    /// <summary>
    /// Tolerance layer of the keyed bridge: every persisted key is read through one of these
    /// helpers, and every helper returns a usable default instead of propagating the game
    /// serializer's exceptions. The <c>damaged</c> flag feeds the load's <c>degraded</c> result.
    /// </summary>
    public static partial class IndoorEntitySaveBridge
    {
        /// <summary>Reads an int property; absent keys and wrong stored types degrade to a default.</summary>
        private static int ReadInt(IObjectLoader loader, PropertyKey<int> key, int fallback, ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return fallback;
                }

                return loader.Get(key);
            }
            catch (Exception)
            {
                damaged = true;
                return fallback;
            }
        }

        /// <summary>Reads a float property; absent keys and wrong stored types degrade to a default.</summary>
        private static float ReadFloat(IObjectLoader loader, PropertyKey<float> key, float fallback, ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return fallback;
                }

                return loader.Get(key);
            }
            catch (Exception)
            {
                damaged = true;
                return fallback;
            }
        }

        /// <summary>Reads a bool property; absent keys and wrong stored types degrade to false.</summary>
        private static bool ReadBool(IObjectLoader loader, PropertyKey<bool> key, ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return false;
                }

                return loader.Get(key);
            }
            catch (Exception)
            {
                damaged = true;
                return false;
            }
        }

        /// <summary>Reads a string property; absent keys, wrong types and null values degrade to a default.</summary>
        private static string ReadString(IObjectLoader loader, PropertyKey<string> key, string fallback,
            ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return fallback;
                }

                var value = loader.Get(key);
                return value ?? fallback;
            }
            catch (Exception)
            {
                damaged = true;
                return fallback;
            }
        }

        /// <summary>Reads an int list; absent keys and wrong stored element types degrade to an empty list.</summary>
        private static List<int> ReadInts(IObjectLoader loader, ListKey<int> key, ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return new List<int>();
                }

                return loader.Get(key) ?? new List<int>();
            }
            catch (Exception)
            {
                damaged = true;
                return new List<int>();
            }
        }

        /// <summary>Reads a float list; absent keys and wrong stored element types degrade to an empty list.</summary>
        private static List<float> ReadFloats(IObjectLoader loader, ListKey<float> key, ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return new List<float>();
                }

                return loader.Get(key) ?? new List<float>();
            }
            catch (Exception)
            {
                damaged = true;
                return new List<float>();
            }
        }

        /// <summary>Reads a string list; absent keys and wrong types degrade to an empty list, null entries to "".</summary>
        private static List<string> ReadStrings(IObjectLoader loader, ListKey<string> key, ref bool damaged)
        {
            try
            {
                if (!loader.Has(key))
                {
                    damaged = true;
                    return new List<string>();
                }

                var values = loader.Get(key);
                if (values is null)
                {
                    damaged = true;
                    return new List<string>();
                }

                for (var index = 0; index < values.Count; index++)
                {
                    if (values[index] is null)
                    {
                        values[index] = string.Empty;
                        damaged = true;
                    }
                }

                return values;
            }
            catch (Exception)
            {
                damaged = true;
                return new List<string>();
            }
        }
    }
}
