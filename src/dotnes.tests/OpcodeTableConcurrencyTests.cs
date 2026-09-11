using dotnes.ObjectModel;
using System.Runtime.Loader;

namespace dotnes.tests;

public class OpcodeTableConcurrencyTests
{
    [Fact]
    public async Task ConcurrentDecodingPublishesACompleteTable()
    {
        // A collectible context guarantees a cold table even if other tests decoded first.
        var context = new AssemblyLoadContext(nameof(OpcodeTableConcurrencyTests), isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(Program6502).Assembly.Location);
            var type = assembly.GetType("dotnes.ObjectModel.OpcodeTable", throwOnError: true)!;
            var decode = type.GetMethod("Decode")!;
            using var start = new Barrier(8);
            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                Assert.Equal("(LDA, Immediate)", decode.Invoke(null, new object[] { (byte)0xA9 })!.ToString());
                Assert.Equal("(RTI, Implied)", decode.Invoke(null, new object[] { (byte)0x40 })!.ToString());
                Assert.Equal("(NOP, Implied)", decode.Invoke(null, new object[] { (byte)0xEA })!.ToString());
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await Task.WhenAll(tasks);
        }
        finally
        {
            context.Unload();
        }
    }
}
