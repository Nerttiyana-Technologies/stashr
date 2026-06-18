// Polyfill so that `init`-only setters and `record` types compile on netstandard2.0
// (the type exists in net5.0+). Compiled only for the down-level target.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
#endif
