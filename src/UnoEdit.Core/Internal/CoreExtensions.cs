using System;
using System.Collections.Immutable;

namespace UnoEdit.Internal;

// Private helpers live outside UnoEdit.Utils, preserving the UI assembly's
// public ExtensionMethods API without ambiguous extension resolution.
internal static class CoreExtensions
{
    internal static bool IsClose(this double left, double right) =>
        left == right || Math.Abs(left - right) < 0.01;

    internal static T PeekOrDefault<T>(this ImmutableStack<T> stack) =>
        stack.IsEmpty ? default : stack.Peek();
}
