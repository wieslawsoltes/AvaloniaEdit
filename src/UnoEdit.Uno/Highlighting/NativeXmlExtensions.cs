using System.Collections.Generic;
using System.Xml;

namespace UnoEdit.Highlighting;

internal static class NativeXmlExtensions
{
    internal static bool? GetBoolAttribute(this XmlReader reader, string attributeName)
    {
        var value = reader.GetAttribute(attributeName);
        return value == null ? null : XmlConvert.ToBoolean(value);
    }

    internal static string GetAttributeOrNull(this XmlElement element, string attributeName) =>
        element.GetAttributeNode(attributeName)?.Value;

    internal static bool? GetBoolAttribute(this XmlElement element, string attributeName)
    {
        var value = element.GetAttributeOrNull(attributeName);
        return value == null ? null : XmlConvert.ToBoolean(value);
    }

    internal static IEnumerable<T> Sequence<T>(T value)
    {
        yield return value;
    }
}
