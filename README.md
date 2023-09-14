# .NES ("dot" NES)

![dot NES logo](assets/logo.png)

.NET for the NES game console!

![Gif of NES Emulator launching from VS Code](assets/vscode.gif)

## Getting Started

Simply install the template:

```sh
dotnet new install dotnes.templates
```

Create a project:

```sh
dotnet new nes
```

Or use the project template in Visual Studio:

![Screenshot of the NES project template in Visual Studio](assets/vs-template.png)

Build and run it as you would a console app:

```sh
dotnet run
```

Of course, you can also just open the project in Visual Studio and hit F5.

> Note that Ctrl+F5 currently works better in C# Dev Kit in VS Code.

## Anatomy of an NES application

"Hello World" looks something like:

```csharp
// set palette colors
pal_col(0, 0x02);   // set screen to dark blue
pal_col(1, 0x14);   // fuchsia
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

// write text to name table
vram_adr(NTADR_A(2, 2));            // set address
vram_write("Hello, world!");         // write bytes to video RAM

// enable PPU rendering (turn on screen)
ppu_on_all();

// infinite loop
while (true) ;
```

This looks very much like ["Hello World" in
C](https://8bitworkshop.com/v3.10.0/?platform=nes&file=hello.c), taking
advantage of the latest C# features in 2023.

By default the APIs like `pal_col`, etc. are provided by an implicit
`global using static NESLib;` and all code is written within a single
`Program.cs`.

Additionally, a `chr_generic.s` file is included as your game's "artwork" (lol?):

```assembly
.segment "CHARS"
.byte $00,$00,$00,$00,$00,$00,$00,$00
...
.byte $B4,$8C,$FC,$3C,$98,$C0,$00,$00
;;
```

This table of data is used to render sprites, text, etc.

## Scope

The types of things I wanted to get working initially:

* An object model for writing NES binaries
* Building a project should produce a `*.nes` binary, that is byte-for-byte
  identical to a program written in C.
* "Hello World" runs
* Byte arrays, and a more advanced sample like `attributetable` run
* Local variables work in some form
* Project template, MSBuild support, IDE support

Down the road, I might think about support for:

* Methods
* Structs
* Multiple files
* Some subset of useful BCL methods

## How it works

For lack of a better word, .NES is a "transpiler" that takes
[MSIL](https://en.wikipedia.org/wiki/MSIL) and transforms it directly into a
working [6502 microprocessor](http://www.6502.org/) binary that can run in your
favorite NES emulator. If you think about .NET's Just-In-Time (JIT) compiler or
the various an Ahead-Of-Time (AOT) compilers, .NES is doing something similiar:
taking MSIL and turning it into runnable machine code.

To understand further, let's look at the MSIL of a `pal_col` method call:

```msil
// pal_col((byte)0, (byte)2);
IL_0000: ldc.i4.0
IL_0001: ldc.i4.2
IL_0002: call void [neslib]NES.NESLib::pal_col(uint8, uint8)
```

In 6502 assembly, this would look something like:

```assembly
A900          LDA #$00
20A285        JSR pusha
A902          LDA #$02
203E82        JSR _pal_col
```

You can see how one might envision using [System.Reflection.Metadata][srm] to
iterate over the contents of a .NET assembly and generate [6502
instructions][6502-instructions] -- that's how this whole idea was born!

[srm]: https://learn.microsoft.com/dotnet/api/system.reflection.metadata
[6502-instructions]: https://www.masswerk.at/6502/6502_instruction_set.html

## Limitations

This is a hobby project, so only around 5 C# programs are known to work. But to
get an idea of what is not available:

* No runtime
* No BCL
* No objects or GC
* No debugger
* Strings are ASCII

What we *do* have is a way to express an NES program in a single `Program.cs`.

## Links

To learn more about NES development, I found the following useful:

* [8bitworkshop](https://8bitworkshop.com)
* [NES 6502 Programming Tutorial](https://www.vbforums.com/showthread.php?858389-NES-6502-Programming-Tutorial-Part-1-Getting-Started)
* [INES File Format](https://wiki.nesdev.org/w/index.php/INES)
* [6502 Instruction Set][6502-instructions]
* [HxD Hex Editor](https://mh-nexus.de/en/hxd/)

## ANESE License

I needed a simple, small NES emulator to redistribute with .NES that runs on Mac
and Windows. Special thanks to [@daniel5151 and
ANESE](https://github.com/daniel5151/ANESE). This is the default NES emulator
used in the `dotnet.anese` package, [license
here](https://github.com/daniel5151/ANESE/blob/8ae814d615479b1496c98033a1f5bc4da5921c6f/LICENSE).