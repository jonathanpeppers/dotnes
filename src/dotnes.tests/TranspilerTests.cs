using System.Text;
using Xunit.Abstractions;

namespace dotnes.tests;

public class TranspilerTests
{
    readonly ILogger _logger;

    public TranspilerTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    [Theory]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    [InlineData("multifile", true)]
    [InlineData("multifile", false)]
    public Task ReadStaticVoidMain(string name, bool debug)
    {
        var suffix = debug ? "debug" : "release";
        var dll = Utilities.GetResource($"{name}.{suffix}.dll");
        var transpiler = new Transpiler(dll, Array.Empty<AssemblyReader>());
        var builder = new StringBuilder();
        foreach (var instruction in transpiler.ReadStaticVoidMain())
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(instruction.ToString());
        }

        var settings = new VerifySettings();
        settings.DisableRequireUniquePrefix();
        settings.UseFileName($"TranspilerTests.ReadStaticVoidMain.{name}");
        return Verify(builder, settings);
    }

    [Theory]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    [InlineData("music", true)]
    [InlineData("music", false)]
    [InlineData("metasprites", true)]
    [InlineData("metasprites", false)]
    [InlineData("flicker", true)]
    [InlineData("flicker", false)]
    [InlineData("tint", true)]
    [InlineData("tint", false)]
    [InlineData("scroll", true)]
    [InlineData("scroll", false)]
    [InlineData("rletitle", true)]
    [InlineData("rletitle", false)]
    [InlineData("tileset1", true)]
    [InlineData("tileset1", false)]
    [InlineData("sprites", true)]
    [InlineData("sprites", false)]
    [InlineData("metacursor", true)]
    [InlineData("metacursor", false)]
    [InlineData("metatrigger", true)]
    [InlineData("metatrigger", false)]
    [InlineData("statusbar", true, "Vertical")]
    [InlineData("statusbar", false, "Vertical")]
    [InlineData("vrambuffer", true)]
    [InlineData("vrambuffer", false)]
    [InlineData("horizscroll", true, "Vertical")]
    [InlineData("horizscroll", false, "Vertical")]
    [InlineData("horizmask", true, "Vertical")]
    [InlineData("horizmask", false, "Vertical")]
    [InlineData("animation", true)]
    [InlineData("animation", false)]
    [InlineData("multifile", true)]
    [InlineData("multifile", false)]
    [InlineData("peekpoke", true)]
    [InlineData("peekpoke", false)]
    [InlineData("fade", true)]
    [InlineData("fade", false)]
    [InlineData("scoreboard", true)]
    [InlineData("scoreboard", false)]
    [InlineData("bigsprites", true)]
    [InlineData("bigsprites", false)]
    [InlineData("aputest", true)]
    [InlineData("aputest", false)]
    [InlineData("bankswitch", true, "Horizontal", 4, 4, 8)]
    [InlineData("bankswitch", false, "Horizontal", 4, 4, 8)]
    [InlineData("irq", true, "Horizontal", 4, 4, 1)]
    [InlineData("irq", false, "Horizontal", 4, 4, 1)]
    [InlineData("climber", true)]
    [InlineData("climber", false)]
    [InlineData("siegegame", true)]
    [InlineData("siegegame", false)]
    [InlineData("pong", true)]
    [InlineData("pong", false)]
    [InlineData("transtable", true, "Horizontal", 0, 2, 0)]
    [InlineData("transtable", false, "Horizontal", 0, 2, 0)]
    [InlineData("snake", true)]
    [InlineData("snake", false)]
    [InlineData("monobitmap", true, "Horizontal", 2, 2, 0)]
    [InlineData("monobitmap", false, "Horizontal", 2, 2, 0)]
    [InlineData("shoot2", true, "Horizontal", 2, 2, 0)]
    [InlineData("shoot2", false, "Horizontal", 2, 2, 0)]
    [InlineData("conio", true)]
    [InlineData("conio", false)]
    [InlineData("procgen", true)]
    [InlineData("procgen", false)]
    [InlineData("vertscroll", true)]
    [InlineData("vertscroll", false)]
    [InlineData("slideshow", true, "Horizontal", 3, 2, 2)]
    [InlineData("slideshow", false, "Horizontal", 3, 2, 2)]
    [InlineData("mmc1", true, "Horizontal", 1)]
    [InlineData("mmc1", false, "Horizontal", 1)]
    [InlineData("battery", true, "Horizontal", 0, 2, 1, true)]
    [InlineData("battery", false, "Horizontal", 0, 2, 1, true)]
    [InlineData("ushortarray", true)]
    [InlineData("ushortarray", false)]
    public Task Write(string name, bool debug, string mirroring = "Horizontal", int mapper = 0, int prgBanks = 2, int chrBanks = 1, bool battery = false)
    {
        var configuration = debug ? "debug" : "release";

        var assemblyReaders = new List<AssemblyReader>();

        // CHR RAM samples (chrBanks=0) don't need a CHR assembly file
        if (chrBanks > 0)
        {
            // Check for numbered CHR bank files (e.g., chr_slideshow_0.s, chr_slideshow_1.s).
            // Files must be numbered sequentially starting from 0 with no gaps.
            bool foundNumbered = false;
            for (int b = 0; b < chrBanks; b++)
            {
                var numberedName = $"chr_{name}_{b}.s";
                var numberedStream = typeof(Utilities).Assembly.GetManifestResourceStream(numberedName);
                if (numberedStream != null)
                {
                    assemblyReaders.Add(new AssemblyReader(new StreamReader(numberedStream)));
                    foundNumbered = true;
                }
                else
                {
                    break; // Sequential numbering required — stop at first missing file
                }
            }

            if (!foundNumbered)
            {
                var chrName = $"chr_{name}.s";
                var chrStream = typeof(Utilities).Assembly.GetManifestResourceStream(chrName);
                var chr_generic = new StreamReader(chrStream ?? Utilities.GetResource("chr_generic.s"));
                assemblyReaders.Add(new AssemblyReader(chr_generic));
            }
        }

        // Include fami assembly files (famitone2.s, demosounds.s, etc.) only for samples that use extern methods
        if (name is "climber" or "fami")
        {
            var famiDir = Path.Combine(AppContext.BaseDirectory, "Data", "fami");
            if (Directory.Exists(famiDir))
            {
                foreach (var sFile in Directory.GetFiles(famiDir, "*.s").OrderBy(f => f))
                    assemblyReaders.Add(new AssemblyReader(sFile));
            }
        }

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var il = new Transpiler(dll, assemblyReaders, _logger, mirroring, mapper, prgBanks, chrBanks, battery);
        using var ms = new MemoryStream();
        il.Write(ms);

        var settings = new VerifySettings();
        settings.DisableRequireUniquePrefix();
        settings.UseFileName($"TranspilerTests.Write.{name}");
        return Verify(ms.ToArray(), settings);
    }

    [Theory]
    [InlineData(false)] // no battery: flags6 bit 1 = 0
    [InlineData(true)]  // battery: flags6 bit 1 = 1
    public void Write_BatteryFlag(bool battery)
    {
        using var dll = Utilities.GetResource("hello.release.dll");
        var chr_generic = new StreamReader(Utilities.GetResource("chr_generic.s"));
        var assemblyReaders = new List<AssemblyReader> { new AssemblyReader(chr_generic) };
        using var il = new Transpiler(dll, assemblyReaders, _logger, battery: battery);
        using var ms = new MemoryStream();
        il.Write(ms);

        var bytes = ms.ToArray();
        // iNES header byte 6 is flags6; only check bit 1 (battery flag)
        byte batteryBit = (byte)(bytes[6] & 0x02);
        Assert.Equal(battery ? 0x02 : 0x00, batteryBit);
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    public void BuildProgram6502(string name, bool debug)
    {
        var configuration = debug ? "debug" : "release";

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var transpiler = new Transpiler(dll, [], _logger);
        
        // Build using single-pass transpilation
        var program = transpiler.BuildProgram6502(out ushort sizeOfMain, out ushort locals);

        // Verify the program has blocks
        Assert.True(program.BlockCount > 0, "Program should have blocks");
        Assert.True(program.TotalSize > 0, "Program should have non-zero size");

        // Verify main block exists with correct size
        var mainBlock = program.GetBlock("main");
        Assert.NotNull(mainBlock);
        Assert.True(mainBlock.Count > 0, "Main block should have instructions");
        Assert.Equal(sizeOfMain, mainBlock.Size);

        // Verify built-in labels are defined
        var labels = program.GetLabels();
        Assert.True(labels.ContainsKey("popa"), "popa label should be defined");
        Assert.True(labels.ContainsKey("popax"), "popax label should be defined");
        Assert.True(labels.ContainsKey("pusha"), "pusha label should be defined");
        Assert.True(labels.ContainsKey("pushax"), "pushax label should be defined");
        Assert.True(labels.ContainsKey("main"), "main label should be defined");

        // Verify forward reference labels have non-zero addresses
        Assert.NotEqual(0, labels["popa"]);
        Assert.NotEqual(0, labels["popax"]);
        Assert.NotEqual(0, labels["pusha"]);
        Assert.NotEqual(0, labels["pushax"]);
    }

    /// <summary>
    /// Verifies that re-assigning local variables does not inflate LocalCount.
    /// Each unique local slot should only count once toward the total allocation,
    /// regardless of how many times it is stored to.
    /// </summary>
    [Theory]
    [InlineData("tint", false, 3)]       // 2 byte locals + 1 pad_poll temp
    [InlineData("tint", true, 3)]
    [InlineData("peekpoke", false, 3)]   // 3 byte locals
    [InlineData("peekpoke", true, 3)]
    public void LocalCountNotInflatedByReassignment(string name, bool debug, int expectedLocals)
    {
        var configuration = debug ? "debug" : "release";

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var transpiler = new Transpiler(dll, [], _logger);

        _ = transpiler.BuildProgram6502(out ushort sizeOfMain, out ushort locals);

        _logger.WriteLine($"{name} ({configuration}): LocalCount={locals}, MainSize={sizeOfMain}");
        Assert.Equal(expectedLocals, locals);
    }

    [Theory]
    [InlineData("climber", true)]
    [InlineData("climber", false)]
    public void ClimberPushaPopaBalanced(string name, bool debug)
    {
        // Regression: The climber sample must not have unmatched JSR pusha calls
        // that leak cc65 stack bytes. Every pusha must be balanced by a matching
        // popa, incsp1, or be a function argument consumed by the callee.
        var configuration = debug ? "debug" : "release";

        var assemblyReaders = new List<AssemblyReader>();
        var chrStream = typeof(Utilities).Assembly.GetManifestResourceStream($"chr_{name}.s")
            ?? Utilities.GetResource("chr_generic.s");
        assemblyReaders.Add(new AssemblyReader(new StreamReader(chrStream)));

        var famiDir = Path.Combine(AppContext.BaseDirectory, "Data", "fami");
        if (Directory.Exists(famiDir))
        {
            foreach (var sFile in Directory.GetFiles(famiDir, "*.s").OrderBy(f => f))
                assemblyReaders.Add(new AssemblyReader(sFile));
        }

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var transpiler = new Transpiler(dll, assemblyReaders, _logger);
        var program = transpiler.BuildProgram6502(out _, out _);

        var mainBlock = program.GetBlock("main");
        Assert.NotNull(mainBlock);

        // Count pusha/popa/incsp in the main block.
        // Function-argument pushas are consumed by the callee (popa inside the
        // callee's block, not main). Detect them by checking if a JSR to a
        // library/user function follows within a short window.
        int leakedPusha = 0, funcArgPusha = 0, popaCount = 0;
        var instrs = mainBlock.InstructionsWithLabels.ToList();
        for (int i = 0; i < instrs.Count; i++)
        {
            var (instruction, _) = instrs[i];
            if (instruction.Opcode != ObjectModel.Opcode.JSR || instruction.Operand is not ObjectModel.LabelOperand lbl)
                continue;
            // Count bytes popped: popa=1, popax=2, incsp1=1, incsp2=2
            if (lbl.Label is "popa" or "incsp1") popaCount++;
            else if (lbl.Label is "popax" or "incsp2") popaCount += 2;
            if (lbl.Label != "pusha") continue;

            // Check if this pusha is for a function argument
            bool isForFuncCall = false;
            for (int j = i + 1; j < Math.Min(i + 8, instrs.Count); j++)
            {
                var next = instrs[j].Instruction;
                if (next.Opcode == ObjectModel.Opcode.JSR && next.Operand is ObjectModel.LabelOperand callLbl
                    && callLbl.Label is not "pusha" and not "pushax" and not "popa"
                       and not "popax" and not "incsp1" and not "incsp2")
                {
                    isForFuncCall = true;
                    break;
                }
            }
            if (isForFuncCall) funcArgPusha++;
            else leakedPusha++;
        }

        _logger.WriteLine($"{name} ({configuration}): funcArgPusha={funcArgPusha}, leakedPusha={leakedPusha}, popa={popaCount}");

        // Non-argument pusha calls should roughly balance popa/incsp in the same block.
        // Allow a small deficit for init-phase code paths that run once before the main
        // loop starts (verified via runtime stack monitoring in Mesen2).
        Assert.True(leakedPusha <= popaCount + 2,
            $"Unmatched state-saving pusha calls in {name} ({configuration}): " +
            $"stateSave={leakedPusha}, popa={popaCount}. " +
            $"Deficit: {leakedPusha - popaCount} bytes leaked per execution.");
    }
}
