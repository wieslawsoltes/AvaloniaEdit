using System.Collections.Generic;

namespace UnoEdit.Utils;

// The original collection tests use the UI assembly's public AddRange helper.
// Their collection operations stay identical when run without a UI assembly.
internal static class CoreTestExtensions
{
    internal static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
            collection.Add(item);
    }
}
