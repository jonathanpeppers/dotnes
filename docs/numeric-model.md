# Integer storage and arithmetic

.NES targets a byte/word 6502 backend, not a CLR runtime. A C# `int` must not
silently become an eight-bit accumulator, nor does choosing this target redefine
`int` as a sixteen-bit type.

## Scalar values

| Type | Range | Storage |
| --- | --- | --- |
| `byte` | 0 to 255 | One byte |
| `sbyte` | -128 to 127 | One byte, sign-extended when widened |
| `ushort` | 0 to 65535 | Two bytes |
| `short` | -32768 to 32767 | Two bytes, signed |
| `int` local | A statically proven byte/word range | Compact storage only when the range is proven |

Local signatures, rather than the initializer's current value, determine signedness.
A word local can retain compact byte storage when every assignment explicitly
truncates to `byte`. The software stack and local frame accounting must still
preserve every byte of a live word across calls.

Conversions to `byte`/`sbyte` retain the low eight bits, and conversions to
`ushort`/`short` retain the low sixteen bits. Widening a signed byte sign-extends;
widening an unsigned byte zero-extends. For example, `(ushort)(sbyte)-1` is 65535,
not 255. `checked` arithmetic is not an alternative implementation of overflow
exceptions on the NES.

C# promotes small operands before comparing them. Signed comparisons therefore
cannot be implemented as unsigned byte comparisons or by inspecting only the
6502 subtraction's negative flag. In particular, `-128 < 127` and
`(short)-1 < (ushort)65535` are both true. Word addition/subtraction must propagate
carry/borrow before an explicit narrowing conversion.

Signed right shifts with constant counts preserve the sign. Shift counts use
the C# low-five-bit mask. Runtime-count signed right shifts produce an actionable
diagnostic rather than being emitted as logical shifts.

Byte sums preserve their promoted carry before a right shift or division:
`(byte)((a + b) / 2)` and `(byte)((a + b) >> 1)` both produce 210 for
runtime bytes 200 and 220. A `ushort + ushort` expression can instead need
seventeen bits. If those bits remain observable, the compiler diagnoses that
unsupported result **before** inserting temporary spill storage. A final byte
cast does not authorize losing carry before a shift or comparison.

User-defined scalar parameters support `byte` and `sbyte`, not word arguments;
an unsupported parameter reports its method, index and type. Word returns retain
both bytes, including signed extension from a byte-sized source. Signed division
by a positive power-of-two constant truncates toward zero, including for negative
operands. Other signed divisors and signed remainder produce diagnostics rather
than using unsigned routines. Word multiplication supports a positive
power-of-two constant factor; a general full-width product is diagnosed.
This numeric work does not add a general 32-bit arithmetic runtime.

When a merged evaluation-stack operand cannot be represented, a diagnostic
identifies the arithmetic and suggests storing the conditional result in an
explicitly typed local. Distinct conditional stores to byte/word locals remain
supported; instruction adjacency alone is never a range proof.

## `int` is not an arbitrary-width accumulator

The backend does not provide full 32-bit local arithmetic. It accepts `int`
locals when it can prove that all assigned values fit its byte/word
representation. Proofs include primitive source ranges, explicit narrow
conversions, constant masks, and canonical bounded increasing counters:

```csharp
for (int index = 0; index < 10; index++)
    poke(0x6000, (byte)index);
```

Unknown or cyclic assignments without a supported bound do not establish such a
proof. For example, the following must not silently lose the carry from 250 + 20:

```csharp
int total = 0;
while (true)
{
    total += peek(0x6000);
    total += 20;
}
```

This produces a `TranspileException` identifying the method, local and IL offset.
Use an explicit supported storage type only when its range and truncation
semantics match the program's intent:

```csharp
ushort total = 0;
while (true)
{
    total = (ushort)(total + peek(0x6000));
    total = (ushort)(total + 20);
}
```

This example deliberately wraps at 65536; it is **not** equivalent to unrestricted
`int` accumulation. Ordinary IL `int` promotion inside explicitly narrowed
byte/word expressions is not itself grounds for rejecting a program.
