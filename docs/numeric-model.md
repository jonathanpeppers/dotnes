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
An unsigned word local can retain compact byte storage when every assignment
explicitly truncates to `byte` and its address is not exposed. Signed word locals
remain word-sized so a narrowed value such as 255 is not later sign-extended
from its low byte. The software stack and local frame accounting must still
preserve every byte of a live word across calls.

Supported captured scalar variables retain their declared numeric types through
closure loads, arithmetic, comparisons and returns. Word stores use the actual
runtime operand, or the original full literal, rather than a byte-sized tracking
value. Byte fields do not inherit a previous operand's high-register state.
Static and captured enums decoded to `short`/`ushort` allocate the same two bytes
that their typed loads consume; this is not a separate enum calling convention.
Enums backed by `long`/`ulong` remain unsupported storage and are diagnosed.

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

Unsigned word comparisons, including comparisons against constants or bytes,
use both bytes when producing a Boolean value. For example, a stored result of
`value < 256` is false when the `ushort` value is 300. Existing complete-word immediate branch forms keep
their specialized lowering; materialized Boolean results do not use a byte-only
comparison as a substitute.

Unary negation and bitwise complement operate on both word bytes and retain signed
provenance through comparisons and calls. Their original promoted range is also
validated: `-ushortValue`, `~ushortValue`, and negating an arbitrary `short` can
need more than the supported signed/unsigned word range. An explicit word cast
requests wrapping; a later shift or comparison cannot recover discarded sign bits.

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

Opt-in [`NESOptimizePromotedByteArithmetic`](msbuild-properties.md#nesoptimizepromotedbytearithmetic)
specializes addition/subtraction of two proven unsigned-byte operands without
changing these semantics. The 6502 low-byte carry/borrow constructs the complete
high byte directly, replacing generic word scratch traffic. Operand reconstruction
must still pass the existing purity, single-use, live-value, and branch-entry
checks; signed/word operands keep their generic path. This is not range-based
narrowing of `int` indexes or a new calling convention. It defaults to `false`
to preserve existing ROM bytes.

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

User-defined and extern primitive returns support `byte`, `sbyte`, `short`, `ushort`,
`bool` and `void`. Int32/UInt32 and other unsupported primitive return signatures are
diagnosed before emission; the compact-range exception for Int32 **locals** does
not change the user-method return ABI.

Generic scalar `ref`/`out` and address-taking operations are diagnosed: the existing
managed-address lowering is for struct and closure access, not a scalar pointer ABI.
Use byte/sbyte value parameters or explicit native memory interfaces instead.

Signed division by a positive power-of-two constant truncates toward zero, including for negative
operands. Other signed divisors and signed remainder produce diagnostics rather
than using unsigned routines. Unsigned word division and remainder support
positive constant divisors from 1 through 65535, retaining the full quotient and
remainder. Nonzero runtime `byte` divisors are supported for byte and word
dividends; runtime word divisors produce an actionable diagnostic. Runtime
divisors must be nonzero. The backend does not implement CLR divide-by-zero
exceptions; its full-width division routine traps zero in a labeled fault loop.
Check the divisor before `/` or `%` when zero is possible. A zero constant word
divisor is diagnosed.
Word multiplication preserves both bytes for
runtime byte operands (255 times 3 is 765), including values held across calls.
An explicit final `byte` conversion retains the low eight bits. Conditional
operands and mixed byte/word multiplication preserve the selected values and
their source signedness before that conversion; a runtime `ushort` 300 times a
`byte` 3 therefore narrows to 132, not zero. Runtime remainder uses the actual
divisor even when the compiler's value tracking retains an earlier power-of-two
assignment or loop increment.
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

Converted local, field and byte-parameter operands of `peek`/`poke` retain their
source width and signedness through shared argument materialization, including
compound values held across calls. The original source ranges are checked before
either memory-address or expression spills introduce synthetic conversions;
those generated conversions cannot authorize a wider intermediate.

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
