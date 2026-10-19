using System.Collections;

namespace PBDMP.Plugin;

// Original values of native components that PBDMP temporarily overrides, keyed by native
// address. Il2Cpp wrappers returned by Cast/TryCast are new managed objects each time, so a
// wrapper-keyed dictionary gains a duplicate entry per call for the same component.
internal sealed class NativeOriginals<T> : IEnumerable<KeyValuePair<T, bool>> where T : UnityEngine.Object
{
    private readonly Dictionary<IntPtr, KeyValuePair<T, bool>> _values = new();
    public int Count => _values.Count;
    public bool ContainsKey(T key) => _values.ContainsKey(key.Pointer);
    public bool this[T key] { set => _values[key.Pointer] = new(key, value); }
    public bool Remove(T key) => _values.Remove(key.Pointer);
    public void Clear() => _values.Clear();
    public IEnumerator<KeyValuePair<T, bool>> GetEnumerator() => _values.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
