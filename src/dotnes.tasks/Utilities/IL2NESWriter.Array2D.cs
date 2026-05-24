using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

/// <summary>
/// Multi-dimensional (rank-2 rectangular) byte array support.
///
/// Roslyn emits <c>byte[,]</c> like this:
/// <code>
///   ldc rows; ldc cols; newobj byte[,]::.ctor(int,int);
///   dup; ldtoken &lt;PrivateImplementationDetails&gt;.field;
///   call RuntimeHelpers::InitializeArray  (filtered by the IL reader)
///   ... ldc/ldloc row; ldc/ldloc col; call byte[,]::Get(int,int)
/// </code>
/// We treat the rectangular array as a row-major ROM byte block (same layout
/// produced by <c>InitializeArray</c>) and lower <c>Get(r,c)</c> to
/// <c>base + r*stride + c</c>. The stride is the number of columns.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>True between the <c>newobj byte[,]::.ctor</c> and the matching <c>ldtoken</c>.</summary>
    bool _pending2DByteArrayCtor;

    /// <summary>Stride (number of columns) for the array being initialised.</summary>
    int _pending2DStride;

    /// <summary>Total size (rows*cols) for the array being initialised.</summary>
    int _pending2DSize;

    /// <summary>
    /// Label of the most recently allocated <c>byte[,]</c> ROM data block. While
    /// non-null, calls to <c>byte[,]::Get</c>/<c>Set</c> index into this label.
    /// </summary>
    string? _active2DArrayLabel;

    /// <summary>Stride associated with <see cref="_active2DArrayLabel"/>.</summary>
    int _active2DArrayStride;

    /// <summary>
    /// Handles <c>newobj byte[,]::.ctor(int rows, int cols)</c>. Removes the two
    /// LDA emissions for the dimension constants, captures the stride, and
    /// records that the next <c>ldtoken</c> belongs to a 2D byte array.
    /// </summary>
    internal void HandleNewobjArray2DByte(ILInstruction instruction)
    {
        if (Instructions == null || Index < 2)
            throw new TranspileException(
                "byte[,] allocation requires two preceding ldc instructions for the rows and columns.",
                MethodName);

        var colsInstr = Instructions[Index - 1];
        var rowsInstr = Instructions[Index - 2];
        int? colsConst = colsInstr.GetLdcValue();
        int? rowsConst = rowsInstr.GetLdcValue();
        if (colsConst == null || rowsConst == null)
            throw new TranspileException(
                "byte[,] dimensions must be compile-time constants.",
                MethodName);

        int stride = colsConst.Value;
        int rows = rowsConst.Value;
        if (stride <= 0 || rows <= 0)
            throw new TranspileException(
                $"byte[,] dimensions must be positive (got [{rows},{stride}]).",
                MethodName);

        // The 6502 can only offset Absolute,X by 0..255 in a single instruction,
        // so a byte[,] backed by a ROM table must fit in 256 bytes total.
        int totalSize = rows * stride;
        if (totalSize > 256)
            throw new TranspileException(
                $"byte[,] total size must be <= 256 bytes (got [{rows},{stride}] = {totalSize} bytes). " +
                "Use multiple smaller arrays or a 1D array with manual indexing.",
                MethodName);

        // Remove the LDA emissions from the two preceding ldc instructions.
        int rowsOffset = rowsInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(rowsOffset, out int blockCountBeforeRows))
        {
            int toRemove = GetBufferedBlockCount() - blockCountBeforeRows;
            if (toRemove > 0)
                RemoveLastInstructions(toRemove);
        }

        // Pop the two dimension values that the ldc handlers pushed.
        if (Stack.Count > 0) Stack.Pop(); // cols
        if (Stack.Count > 0) Stack.Pop(); // rows

        _pending2DByteArrayCtor = true;
        _pending2DStride = stride;
        _pending2DSize = totalSize;
        _pendingArrayType = "Byte";
        // Push a marker representing the array reference that newobj produces.
        // Subsequent dup instructions will duplicate this marker; Get/Set will
        // pop one copy per call.
        Stack.Push(-1);
    }

    /// <summary>
    /// Returns true if the current <c>ldtoken</c> is part of a pending
    /// <c>byte[,]</c> initialization (i.e. follows <c>newobj byte[,]::.ctor</c>
    /// and <c>dup</c>). When true, the caller should record the label data
    /// into the byte-array table and update <see cref="_active2DArrayLabel"/>,
    /// but must <b>not</b> emit any LDA/LDX address loads or push a stloc
    /// marker — the data is referenced only via Get/Set calls.
    /// </summary>
    internal bool ConsumePending2DByteArrayLdtoken(string label, int operandLength)
    {
        if (!_pending2DByteArrayCtor)
            return false;
        if (operandLength != _pending2DSize)
            throw new TranspileException(
                $"byte[,] initializer size mismatch: expected {_pending2DSize} bytes for the declared dimensions, " +
                $"but the <PrivateImplementationDetails> data is {operandLength} bytes.",
                MethodName);
        _active2DArrayLabel = label;
        _active2DArrayStride = _pending2DStride;
        _pending2DByteArrayCtor = false;
        _pending2DStride = 0;
        _pending2DSize = 0;
        _pendingArrayType = null;
        return true;
    }

    /// <summary>
    /// Handles <c>call byte[,]::Get(int row, int col)</c>. Lowers the call to
    /// <c>LDA base + row*stride + col</c>. Power-of-two strides use ASL chains;
    /// other strides use repeated <c>CLC/ADC</c> (the fallback path documented
    /// in the spec).
    /// </summary>
    internal void HandleArray2DByteGet()
    {
        if (_active2DArrayLabel == null)
            throw new TranspileException(
                "byte[,] indexing requires the array to be initialized in the same method.",
                MethodName);
        if (Instructions == null || Index < 2)
            throw new TranspileException(
                "byte[,] Get requires at least 2 preceding instructions (row, col).",
                MethodName);

        // IL for `arr[r,c]` pushes arrayref, then row, then col onto the eval stack
        // before calling Get. Under Optimize=true (dotnes mandates this) Roslyn elides
        // the arrayref ldloc whenever the array local is used exactly once, so we only
        // strip from rowInstr onward. The eval-stack pop loop still pops the marker
        // that newobj parked there.
        var rowInstr = Instructions[Index - 2];
        var colInstr = Instructions[Index - 1];

        // Pop the args + array ref from our approximate evaluation stack.
        if (Stack.Count > 0) Stack.Pop(); // col
        if (Stack.Count > 0) Stack.Pop(); // row
        if (Stack.Count > 0) Stack.Pop(); // array ref marker

        int? rowConst = rowInstr.GetLdcValue();
        int? colConst = colInstr.GetLdcValue();
        int stride = _active2DArrayStride;
        string label = _active2DArrayLabel;

        // Fast path: both indices are compile-time constants → resolve fully.
        if (rowConst != null && colConst != null)
        {
            int idx = rowConst.Value * stride + colConst.Value;
            RemoveEmissionsFromIL(rowInstr.Offset);
            EmitAbsoluteByteLoad(label, idx);
            FinishElementLoadInA();
            return;
        }

        // Runtime path: at least one index is a local/sfld. Generate
        //   row_expr → A
        //   * stride (ASLs for power-of-two, otherwise add chain)
        //   + col_expr
        //   TAX
        //   LDA label,X
        int? rowLocalIdx = rowInstr.GetLdlocIndex();
        int? colLocalIdx = colInstr.GetLdlocIndex();

        // Remove the previously emitted instructions for the row+col loads
        // so we can rebuild them in the order the indexing math needs.
        RemoveEmissionsFromIL(rowInstr.Offset);

        // Step 1: load row into A.
        if (rowConst != null)
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)rowConst.Value);
        else if (rowLocalIdx != null && Locals.TryGetValue(rowLocalIdx.Value, out var rl) && rl.Address != null)
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)rl.Address);
        else
            throw new TranspileException(
                "byte[,] runtime indexing requires the row index to be a constant or a local variable.",
                MethodName);

        // Step 2: multiply A by stride.
        EmitMultiplyAByStride(stride);

        // Step 3: add the column value.
        if (colConst != null)
        {
            if (colConst.Value != 0)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, (byte)colConst.Value);
            }
        }
        else if (colLocalIdx != null && Locals.TryGetValue(colLocalIdx.Value, out var cl) && cl.Address != null)
        {
            Emit(Opcode.CLC, AddressMode.Implied);
            Emit(Opcode.ADC, AddressMode.Absolute, (ushort)cl.Address);
        }
        else
        {
            throw new TranspileException(
                "byte[,] runtime indexing requires the column index to be a constant or a local variable.",
                MethodName);
        }

        // Step 4: A → X, then LDA label,X.
        Emit(Opcode.TAX, AddressMode.Implied);
        EmitWithLabel(Opcode.LDA, AddressMode.AbsoluteX, label);

        FinishElementLoadInA();
    }

    /// <summary>
    /// Handles <c>call byte[,]::Set(int row, int col, byte value)</c>.
    ///
    /// <para>
    /// <b>Currently unsupported.</b> ROM-initialised <c>byte[,]</c> tables
    /// (the only form this PR wires up) live in PRG-ROM, so a <c>STA</c>
    /// against the table label is silently dropped by the NES hardware —
    /// the code would compile but the write would not take effect. Until
    /// RAM-backed rectangular arrays are implemented, this throws a
    /// <see cref="TranspileException"/> with a descriptive message rather
    /// than emit code that quietly does nothing.
    /// </para>
    /// </summary>
    internal void HandleArray2DByteSet()
    {
        // Pop the three call args + array-ref marker so the eval-stack
        // tracker stays balanced for any future diagnostics, then throw.
        if (Stack.Count > 0) Stack.Pop(); // value
        if (Stack.Count > 0) Stack.Pop(); // col
        if (Stack.Count > 0) Stack.Pop(); // row
        if (Stack.Count > 0) Stack.Pop(); // array ref marker

        throw new TranspileException(
            "byte[,] Set is not supported: the rectangular array is stored in PRG-ROM " +
            "(via InitializeArray) and writes to ROM addresses are silently dropped by " +
            "the NES hardware. RAM-backed byte[,] arrays are not implemented yet.",
            MethodName);
    }

    /// <summary>
    /// Removes everything emitted from <paramref name="firstILOffset"/> onward.
    /// </summary>
    void RemoveEmissionsFromIL(int firstILOffset)
    {
        if (_blockCountAtILOffset.TryGetValue(firstILOffset, out int blockCount))
        {
            int toRemove = GetBufferedBlockCount() - blockCount;
            if (toRemove > 0)
                RemoveLastInstructions(toRemove);
        }
    }

    /// <summary>
    /// Emits the 6502 load for <c>label + idx</c>. Uses absolute mode for idx=0,
    /// otherwise <c>LDX #idx; LDA label,X</c>. Throws if <paramref name="idx"/>
    /// exceeds the 8-bit Absolute,X offset range.
    /// </summary>
    void EmitAbsoluteByteLoad(string label, int idx)
    {
        if (idx >= 256)
            throw new TranspileException(
                $"byte[,] index ({idx}) exceeds the 8-bit Absolute,X range. " +
                "Only byte[,] tables of <= 256 bytes are supported.",
                MethodName);
        if (idx == 0)
        {
            EmitWithLabel(Opcode.LDA, AddressMode.Absolute, label);
        }
        else
        {
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)idx);
            EmitWithLabel(Opcode.LDA, AddressMode.AbsoluteX, label);
        }
    }

    /// <summary>
    /// Multiplies A by <paramref name="stride"/> in place. Power-of-two strides
    /// use a chain of <c>ASL A</c>. Other strides save A to <c>TEMP</c> and
    /// build the product with repeated <c>CLC/ADC</c> (fallback path; small
    /// strides stay well under 256 cycles per access).
    /// </summary>
    void EmitMultiplyAByStride(int stride)
    {
        if (stride == 1)
            return;
        if (IsPowerOfTwo(stride))
        {
            int shifts = IntegerLog2(stride);
            for (int i = 0; i < shifts; i++)
                Emit(Opcode.ASL, AddressMode.Accumulator);
            return;
        }
        // Fallback: A → TEMP; build A = stride * TEMP via shift+add.
        // We use the well-known shift-and-add algorithm: walk the stride's
        // bits high-to-low, ASL the running sum, and ADC the saved value on
        // each set bit. This stays under 256 cycles even for full bytes.
        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)TEMP);
        // Start the running sum with the top set bit's contribution: that bit
        // simply copies A (=TEMP) into the running sum, which it already is.
        int highBit = IntegerLog2(stride);
        for (int bit = highBit - 1; bit >= 0; bit--)
        {
            Emit(Opcode.ASL, AddressMode.Accumulator);
            if (((stride >> bit) & 1) != 0)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.ZeroPage, (byte)TEMP);
            }
        }
    }

    static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    static int IntegerLog2(int n)
    {
        int log = 0;
        while ((1 << (log + 1)) <= n) log++;
        return log;
    }

    /// <summary>
    /// Common epilogue for a byte-array element load: pushes a placeholder,
    /// marks A as holding a runtime value, and saves to the cc65 stack when
    /// the value is about to feed a multi-arg call.
    /// </summary>
    void FinishElementLoadInA()
    {
        Stack.Push(0);
        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = true;
        if (ScanForUpcomingMultiArgCall())
        {
            EmitJSR("pusha");
            _runtimeValueInA = false;
        }
    }
}
