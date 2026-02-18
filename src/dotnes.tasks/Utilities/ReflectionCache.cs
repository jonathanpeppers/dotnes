using System.Reflection;

namespace dotnes;

class ReflectionCache
{
    readonly Dictionary<string, MethodInfo> _cache = new(StringComparer.Ordinal);
    readonly Dictionary<string, (int argCount, bool hasReturnValue)> _userMethods = new(StringComparer.Ordinal);

    public void RegisterUserMethod(string name, int argCount, bool hasReturnValue)
    {
        _userMethods[name] = (argCount, hasReturnValue);
    }

    public bool IsUserMethod(string name) => _userMethods.ContainsKey(name);

    public MethodInfo GetMethod(string name)
    {
        if (!_cache.TryGetValue(name, out var method))
        {
            if (name != nameof(NESLib.vram_write) && name != nameof(NESLib.set_vram_update))
            {
                _cache[name] = method = typeof(NESLib).GetMethod(name) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
            else if (name == nameof(NESLib.vram_write))
            {
                //TODO: this isn't great, vram_write() has overloads for string and byte[], just using string here
                _cache[name] = method = typeof(NESLib).GetMethod(name, [typeof(string)]) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
            else // set_vram_update
            {
                // set_vram_update has overloads for byte[] and ushort; use byte[] as default
                _cache[name] = method = typeof(NESLib).GetMethod(name, [typeof(byte[])]) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
        }
        return method;
    }

    public int GetNumberOfArguments(string name)
    {
        if (_userMethods.TryGetValue(name, out var info))
            return info.argCount;
        return GetMethod(name).GetParameters().Length;
    }

    public bool HasReturnValue(string name)
    {
        if (_userMethods.TryGetValue(name, out var info))
            return info.hasReturnValue;
        return GetMethod(name).ReturnType != typeof(void);
    }
}
