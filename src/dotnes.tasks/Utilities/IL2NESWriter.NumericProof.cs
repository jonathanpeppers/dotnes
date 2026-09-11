using System.Reflection.Metadata;

namespace dotnes;

partial class IL2NESWriter
{
    // This proves only the existing word-load +/- nonnegative-constant lowering,
    // not an arbitrary expression that happens to leave A:X marked as a word.
    internal bool HasVerifiedNumericProducer(int producer)
    {
        if (Instructions == null || producer < 2 || producer >= Index
            || Instructions[producer].OpCode is not (ILOpCode.Add or ILOpCode.Sub)
            || Instructions[producer - 1].GetLdcValue() is not (>= 0 and <= ushort.MaxValue)
            || !_blockCountAtILOffset.ContainsKey(Instructions[producer].Offset)
            || !_blockCountAtILOffset.ContainsKey(Instructions[producer - 2].Offset))
            return false;
        if (ILBranchTargets.HasEntryAfter(Instructions, producer - 2, Index))
            return false;
        var source = Instructions[producer - 2];
        if (DeclaredScalarType(source) != PrimitiveTypeCode.UInt16)
            return false;
        if (source.GetLdlocIndex() is int local)
            return Locals.TryGetValue(local, out var value) && value.IsWord
                && value.Address != null && value.ArraySize == 0 && value.LabelName == null;
        return source.OpCode == ILOpCode.Ldsfld && source.String is string field
            && WordStaticFields.Contains(field) && StaticFieldAddresses.ContainsKey(field);
    }
}
