using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    bool CanUseDisplacedByteRead(ILInstruction[] body, ILValueAnalysis analysis, int read,
        string method, ArrayStorageAnalysis storage)
    {
        if (!ByteIndexDisplacement.TryMatch(body, analysis, read, out _, out int variable, out _)
            || !NumericTypes.TryGetValue(method, out var numeric)
            || body[variable].GetLdlocIndex() is not int local || local >= numeric.Locals.Length
            || numeric.Locals[local] != PrimitiveTypeCode.Byte)
            return false;
        var allocations = new HashSet<(ILInstruction[] Method, int Producer)>();
        return storage.GetStorage(body, read - 4, allocations) == ArrayStorage.Ram
            && allocations.Count == 1 && allocations.Single().Method == body;
    }
}
