// Fixes record classes when targeting older framework.
// https://stackoverflow.com/a/64749403
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace System.Collections.Generic
{
    /// <summary>
    /// Fixes KeyValuePair deconstructing for net472.
    /// <para/>
    /// https://www.interact-sw.co.uk/iangblog/2018/04/12/deconstruct-keyvaluepair
    /// </summary>
    public static class KeyValuePairExtensions
    {
        public static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> kvp,
            out TKey key,
            out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }
    }
}