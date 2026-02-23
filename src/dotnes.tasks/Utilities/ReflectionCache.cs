using System.Reflection;

namespace dotnes;

class ReflectionCache
{
    readonly Dictionary<string, MethodInfo> _cache = new(StringComparer.Ordinal);
    readonly Dictionary<string, (int argCount, bool hasReturnValue)> _userMethods = new(StringComparer.Ordinal);
    readonly HashSet<string> _externMethods = new(StringComparer.Ordinal);

    public void RegisterUserMethod(string name, int argCount, bool hasReturnValue)
    {
        _userMethods[name] = (argCount, hasReturnValue);
    }

    public void RegisterExternMethod(string name, int argCount, bool hasReturnValue)
    {
        _userMethods[name] = (argCount, hasReturnValue);
        _externMethods.Add(name);
    }

    public bool IsUserMethod(string name) => _userMethods.ContainsKey(name) && !_externMethods.Contains(name);

    public bool IsExternMethod(string name) => _externMethods.Contains(name);

    public MethodInfo GetMethod(string name)
    {
        if (!_cache.TryGetValue(name, out var method))
        {
            if (name is nameof(NESLib.vram_write))
            {
                //TODO: this isn't great, vram_write() has overloads for string and byte[], just using string here
                _cache[name] = method = typeof(NESLib).GetMethod(name, [typeof(string)]) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
            else if (name is nameof(NESLib.set_vram_update))
            {
                // set_vram_update has overloads for byte[] and ushort; use byte[] as default
                _cache[name] = method = typeof(NESLib).GetMethod(name, [typeof(byte[])]) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
            else if (name is nameof(NESLib.vrambuf_put))
            {
                // vrambuf_put has overloads for string and byte[]; use string as default
                _cache[name] = method = typeof(NESLib).GetMethod(name, [typeof(ushort), typeof(string)]) ??
                    throw new InvalidOperationException($"Unable to find method named '{nameof(NESLib)}.{name}'!");
            }
            else
            {
                _cache[name] = method = typeof(NESLib).GetMethod(name) ??
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

    public bool Returns16Bit(string name)
    {
        if (_userMethods.ContainsKey(name))
            return false; // User methods currently only return byte
        var returnType = GetMethod(name).ReturnType;
        return returnType == typeof(ushort) || returnType == typeof(short) || returnType == typeof(int);
    }
}
