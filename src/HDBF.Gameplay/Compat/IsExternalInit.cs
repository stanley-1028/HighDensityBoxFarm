// Polyfill required to compile C# 9+ records and init-only setters against netstandard2.1,
// which does not ship System.Runtime.CompilerServices.IsExternalInit.
// Compiled into HDBF.Gameplay because HDBF.Core's polyfill is internal to HDBF.Core.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
