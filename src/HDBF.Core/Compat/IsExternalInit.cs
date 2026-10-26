// Polyfill for compiling C# 9+ records and init-only setters against netstandard2.1,
// which does not ship System.Runtime.CompilerServices.IsExternalInit.
// This file is compiled into every HDBF assembly that declares records or init setters.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
