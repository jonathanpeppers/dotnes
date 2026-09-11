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

Used unsupported primitive locals, static fields and captured variables are
diagnosed before storage allocation. In particular, `uint` does not implicitly
become a word, and static or closure `int` fields do not inherit the compact-range
exception for Int32 locals. Conflicting same-name static field types are diagnosed
rather than guessing their storage width; rename the fields to disambiguate them. A later
explicit conversion cannot repair bytes already lost in unsupported storage.
Boolean locals and static fields retain their one-byte logical representation.

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
The unsigned right-shift operator `>>>` on a signed operand requires its CLR
32-bit promotion and is diagnosed instead of shifting only its narrow storage.
An explicit byte/ushort conversion before `>>>` requests different, supported
narrow unsigned semantics.

Byte sums preserve their promoted carry before a right shift or division:
`(byte)((a + b) / 2)` and `(byte)((a + b) >> 1)` both produce 210 for
runtime bytes 200 and 220. A `ushort + ushort` expression can instead need
seventeen bits. If those bits remain observable, the compiler diagnoses that
unsupported result **before** inserting temporary spill storage. A final byte
cast does not authorize losing carry before a shift or comparison.

Runtime-count unsigned right shifts also preserve a proven byte sum's ninth bit.
An explicit word conversion before the shift defines wrapping: for example,
`(byte)((ushort)(value + 1) >> count)` produces 128 for byte `value = 255`
and `count = 1`. Every arithmetic stage must preserve the required word bits;
an already-truncated intermediate cannot be widened afterward to recover them.
Promoted left shifts whose observable results need more than a word are diagnosed,
including a later right shift that would bring those bits back into a byte.

User-defined scalar parameters support `byte` and `sbyte`; all other decoded
primitive parameter types are rejected, even when the method does not read the
argument. An unsupported parameter reports its method, index and type. Arrays
and closure references use their separate lowering. Word returns retain
both bytes, including signed extension from a byte-sized source.

User-defined primitive returns support `byte`, `sbyte`, `short`, `ushort`, `bool`
and `void`. Int32/UInt32 and other unsupported primitive return signatures are
diagnosed before emission; the compact-range exception for Int32 **locals** does
not change the user-method return ABI.

Signed division by a positive power-of-two constant truncates toward zero, including for negative
operands. Other signed divisors and signed remainder produce diagnostics rather
than using unsigned routines. Unsigned word division and remainder support
positive constant divisors from 1 through 65535, retaining the full quotient and
remainder. Nonzero runtime `byte` divisors are also supported for word dividends;
runtime word divisors produce an actionable diagnostic. A zero runtime byte
divisor enters a labeled fault loop without producing a result: the NES backend
does not implement CLR divide-by-zero exceptions. Check the divisor before `/`
or `%` when zero is possible. A zero constant word divisor is diagnosed.
Word multiplication preserves both bytes for
runtime byte operands (255 times 3 is 765), including values held across calls.
Explicit word conversions can request the low sixteen bits of a larger product;
otherwise an observable wider product is diagnosed. Word-width demand also
propagates through bitwise operations, so `(ushort)((value << 4) | 1)` retains
the high byte rather than widening an already truncated byte result.
This numeric work does not add a general 32-bit arithmetic runtime.

Conditional values are materialized without treating generated temporary
conversions as source-requested truncation. Their original incoming ranges still
govern arithmetic. A mixed signed/unsigned conditional can need seventeen bits
even when each arm separately fits a word; narrow explicitly before observing it
only if wrapping is intended. When a merged operand cannot be represented, an
actionable diagnostic identifies the operation. Distinct conditional stores to
byte/word locals remain supported; instruction adjacency alone is never a range
proof.

For constant-address `poke`, converted local, field and byte-parameter loads
retain their value and stack balance. A converted compound expression whose
memory-call lowering cannot be proven produces a diagnostic rather than treating
a runtime placeholder as a constant. Store that converted result in an explicit
`byte` local before the memory call.

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
proof. Taking an Int32 local's address also prevents compaction: an initializer
or loop bound does not constrain indirect writes through `ref`/`out`.
For example, the following must not silently lose the carry from 250 + 20:

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
