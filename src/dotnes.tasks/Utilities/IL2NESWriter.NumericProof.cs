using System.Reflection.Metadata;

namespace dotnes;

partial class IL2NESWriter
{
    readonly HashSet<int> _verifiedWordResults = new();
    readonly HashSet<int> _verifiedPromotedResults = new();

    internal bool HasVerifiedPromotedNumericProducer(int producer) =>
        _verifiedPromotedResults.Contains(producer) && Instructions != null && producer < Index
        && !ILBranchTargets.HasEntryAfter(Instructions, producer, Index);

    // This proves only the existing word-load +/- nonnegative-constant lowering,
    // not an arbitrary expression that happens to leave A:X marked as a word.
    internal bool HasVerifiedNumericProducer(int producer)
    {
        if (Instructions != null && producer >= 0 && producer < Index && _verifiedWordResults.Contains(producer))
            return !ILBranchTargets.HasEntryAfter(Instructions, producer, Index);
        if (Instructions == null || producer < 2 || producer >= Index
            || Instructions[producer].OpCode is not (ILOpCode.Add or ILOpCode.Sub)
            || Instructions[producer - 1].GetLdcValue() is not (>= 0 and <= ushort.MaxValue)
            || !_blockCountAtILOffset.ContainsKey(Instructions[producer].Offset)
            || !_blockCountAtILOffset.ContainsKey(Instructions[producer - 2].Offset))
            return false;
        if (ILBranchTargets.HasEntryAfter(Instructions, producer - 2, Index))
            return false;
        var source = Instructions[producer - 2];
        if (source.GetLdlocIndex() is int local)
            return Locals.TryGetValue(local, out var value) && value.IsWord
                && value.Address != null && value.ArraySize == 0 && value.LabelName == null;
        return source.OpCode == ILOpCode.Ldsfld && source.String is string field
            && WordStaticFields.Contains(field) && StaticFieldAddresses.ContainsKey(field);
    }
}
