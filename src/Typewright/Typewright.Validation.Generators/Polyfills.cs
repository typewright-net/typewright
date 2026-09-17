// Polyfill so records with init accessors compile on netstandard2.0.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;
}
