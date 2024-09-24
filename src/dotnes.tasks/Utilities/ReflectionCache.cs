using System.Reflection;

namespace dotnes;

class ReflectionCache
{
    readonly Dictionary<string, MethodInfo> _cache = new(StringComparer.Ordinal);

    public MethodInfo GetMethod(string name)
    {
        if (!_cache.TryGetValue(name, out var method))
        {
            if (name != nameof(NESLib.vram_write))
            {
                _cache[name] = method = typeof(NESLib).GetMethod(name) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
            else
            {
                //TODO: this isn't great, vram_write() has overloads for string and byte[], just using string here
                _cache[name] = method = typeof(NESLib).GetMethod(name, [typeof(string)]) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
        }
        return method;
    }

    public int GetNumberOfArguments(string name) => GetMethod(name).GetParameters().Length;

    public bool HasReturnValue(string name) => GetMethod(name).ReturnType != null;
}
