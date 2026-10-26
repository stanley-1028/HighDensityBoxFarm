using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace HDBF.Integration.Reflection
{
    /// <summary>
    /// Single, cached gateway for every reflection lookup HDBF performs (chapter 8.2).
    ///
    /// Rules that make this the only safe way to touch third-party types (REQ-COMPAT-05,
    /// R-04):
    /// * lookups never throw: a missing assembly, type or member returns <c>null</c>;
    /// * both hits and misses are cached per session, so a broken external mod costs one probe
    ///   instead of one per call (REQ-SAFE-20 forbids uncached reflection on hot paths);
    /// * <see cref="ClearCache"/> is called after a hot reload or an assembly unload, because a
    ///   cached <see cref="Type"/> from an unloaded assembly is a dangling handle.
    ///
    /// The type is static per the frozen INTERFACES.md contract; its mutable state is exactly the
    /// two lookup caches and is cleared by <see cref="ClearCache"/>.
    /// </summary>
    public static class ExternalProxy
    {
        private static readonly object Gate = new object();

        private static readonly Dictionary<string, Type?> TypeCache =
            new Dictionary<string, Type?>(StringComparer.Ordinal);

        private static readonly Dictionary<string, MethodInfo?> MethodCache =
            new Dictionary<string, MethodInfo?>(StringComparer.Ordinal);

        /// <summary>
        /// Finds a type by assembly simple name and full type name. A null/empty assembly name
        /// scans every loaded assembly (used when the calling mod is already known to be loaded).
        /// Returns null when the type cannot be resolved for any reason.
        /// </summary>
        public static Type? FindType(string? assemblyName, string? typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            var key = (assemblyName ?? string.Empty) + "|" + typeName;
            lock (Gate)
            {
                if (TypeCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            Type? resolved = null;
            try
            {
                resolved = ResolveType(assemblyName, typeName!);
            }
            catch (Exception)
            {
                resolved = null;
            }

            lock (Gate)
            {
                TypeCache[key] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Finds a public or non-public instance/static method by name and exact signature.
        /// Returns null when the type is null, the method is missing or the signature differs —
        /// an external API change degrades the adapter instead of throwing (R-04).
        /// </summary>
        public static MethodInfo? FindMethod(Type? type, string? methodName, params Type[] args)
        {
            if (type is null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            var parameters = args ?? Array.Empty<Type>();
            var key = type.AssemblyQualifiedName + "::" + methodName + "(" + JoinSignature(parameters) + ")";
            lock (Gate)
            {
                if (MethodCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            MethodInfo? resolved = null;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static | BindingFlags.FlattenHierarchy;
                resolved = type.GetMethod(methodName!, flags, null, parameters, null);
            }
            catch (AmbiguousMatchException)
            {
                // Overloads that differ only by generic arity: prefer the non-generic match,
                // which is what the probe anchors describe. A miss is safe.
                resolved = null;
            }
            catch (Exception)
            {
                resolved = null;
            }

            lock (Gate)
            {
                MethodCache[key] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Resolves a type by its simple (namespace-less) name across all loaded assemblies.
        /// Used as a last-chance probe when an external mod renames its namespace but not its types.
        /// </summary>
        internal static Type? FindTypeBySimpleName(string? simpleName)
        {
            if (string.IsNullOrEmpty(simpleName))
            {
                return null;
            }

            var key = "*|simple:" + simpleName;
            lock (Gate)
            {
                if (TypeCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            Type? resolved = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length && resolved is null; i++)
            {
                try
                {
                    var types = assemblies[i].GetTypes();
                    for (var t = 0; t < types.Length; t++)
                    {
                        if (string.Equals(types[t].Name, simpleName, StringComparison.Ordinal))
                        {
                            resolved = types[t];
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // ReflectionTypeLoadException and friends on one assembly: keep scanning the
                    // rest instead of losing the whole probe (REQ-SAFE-01).
                }
            }

            lock (Gate)
            {
                TypeCache[key] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Finds a public or non-public member by name and an expected signature expressed as
        /// full type-name strings — the "type full name + member name + signature" cache required
        /// by chapter 8.2, without ever referencing a third-party type at compile time.
        ///
        /// Parameter names are element names: an <c>out</c>/<c>ref</c> parameter is compared by
        /// its element type (e.g. <c>out PipeGraph</c> is matched with <c>PipeGraph</c>). A null or
        /// empty <paramref name="returnTypeName"/> means "do not check the return type". Generic
        /// method definitions are skipped because the anchors describe concrete members only.
        /// Returns null on any mismatch or failure; the result (including a miss) is cached.
        /// </summary>
        public static MethodInfo? FindMethodBySignature(Type? type, string? memberName,
            IReadOnlyList<string>? parameterTypeNames, string? returnTypeName)
        {
            if (type is null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var parameters = parameterTypeNames ?? Array.Empty<string>();
            var key = type.AssemblyQualifiedName + "::" + BuildSignatureKey(memberName, parameters, returnTypeName);
            lock (Gate)
            {
                if (MethodCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            MethodInfo? resolved = null;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static | BindingFlags.FlattenHierarchy;
                var methods = type.GetMethods(flags);
                for (var i = 0; i < methods.Length && resolved is null; i++)
                {
                    var method = methods[i];
                    if (!string.Equals(method.Name, memberName, StringComparison.Ordinal) ||
                        method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    var declared = method.GetParameters();
                    if (declared.Length != parameters.Count || !ParametersMatch(declared, parameters))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(returnTypeName) &&
                        !string.Equals(TypeDisplayName(method.ReturnType), NormalizeSignatureType(returnTypeName!),
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    resolved = method;
                }
            }
            catch (Exception)
            {
                // A signature probe is allowed to miss (R-04); callers fall back to the degraded path.
                resolved = null;
            }

            lock (Gate)
            {
                MethodCache[key] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Drops every cached lookup; call after a hot reload or assembly unload (chapter 8.2).
        /// </summary>
        public static void ClearCache()
        {
            lock (Gate)
            {
                TypeCache.Clear();
                MethodCache.Clear();
            }
        }

        private static Type? ResolveType(string? assemblyName, string typeName)
        {
            if (!string.IsNullOrEmpty(assemblyName))
            {
                var assembly = FindAssembly(assemblyName!);
                return assembly?.GetType(typeName, false, false);
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var type = assemblies[i].GetType(typeName, false, false);
                if (type is not null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Assembly? FindAssembly(string assemblyName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var candidate = assemblies[i];
                if (string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.GetName().FullName, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool ParametersMatch(ParameterInfo[] declared, IReadOnlyList<string> expected)
        {
            for (var i = 0; i < declared.Length; i++)
            {
                var parameterType = declared[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    parameterType = parameterType.GetElementType() ?? parameterType;
                }

                if (!string.Equals(TypeDisplayName(parameterType), NormalizeSignatureType(expected[i]),
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string BuildSignatureKey(string memberName, IReadOnlyList<string> parameterTypeNames,
            string? returnTypeName)
        {
            var builder = new StringBuilder("sig:");
            builder.Append(memberName).Append('(');
            for (var i = 0; i < parameterTypeNames.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(NormalizeSignatureType(parameterTypeNames[i]));
            }

            builder.Append(")->");
            builder.Append(string.IsNullOrEmpty(returnTypeName) ? "*" : NormalizeSignatureType(returnTypeName!));
            return builder.ToString();
        }

        /// <summary>
        /// Normalises a stored signature type name: trims, and treats a trailing ByRef marker as
        /// already stripped. Kept deliberately simple because anchors use concrete names only.
        /// </summary>
        private static string NormalizeSignatureType(string typeName)
        {
            var trimmed = typeName.Trim();
            return trimmed.EndsWith("&", StringComparison.Ordinal)
                ? trimmed.Substring(0, trimmed.Length - 1)
                : trimmed;
        }

        /// <summary>Full name of a runtime type, with generic arguments rendered recursively.</summary>
        private static string TypeDisplayName(Type type)
        {
            if (type.IsArray)
            {
                return TypeDisplayName(type.GetElementType() ?? typeof(object)) + "[]";
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var definition = type.GetGenericTypeDefinition();
                var arguments = type.GetGenericArguments();
                var builder = new StringBuilder(definition.FullName ?? definition.Name);
                builder.Append('<');
                for (var i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(TypeDisplayName(arguments[i]));
                }

                builder.Append('>');
                return builder.ToString();
            }

            return type.FullName ?? type.Name;
        }

        private static string JoinSignature(Type[] parameters)
        {
            if (parameters.Length == 0)
            {
                return string.Empty;
            }

            var parts = new string[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                parts[i] = parameters[i]?.FullName ?? "?";
            }

            return string.Join(",", parts);
        }
    }
}
