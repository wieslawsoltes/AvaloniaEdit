using System;
using System.Collections.Generic;
namespace UnoEdit.Utils;
internal sealed class NativeServiceContainer : IServiceContainer
{
 private readonly Dictionary<Type, object> _services = new();
 public object GetService(Type type) => _services.TryGetValue(type, out var value) ? value : null;
 public void AddService(Type type, object service) { if (type == null || service == null) throw new ArgumentNullException(); _services.Add(type, service); }
 public void RemoveService(Type type) => _services.Remove(type);
}
