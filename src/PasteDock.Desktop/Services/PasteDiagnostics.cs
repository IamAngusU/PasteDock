using PasteDock.Desktop.Clipboard;
using PasteDock.Desktop.Interop;

namespace PasteDock.Desktop.Services;

/// <summary>Exercises the real paste coordinator with fake OS boundaries. Never reads or writes the clipboard.</summary>
internal static class PasteDiagnostics
{
    public static async Task<bool> RunAsync(List<object> checks)
    {
        var passed = true;
        var target = new PasteTarget(new IntPtr(1), 123, 456, "Synthetic target");
        var payload = new ClipPayload { Text = "Synthetic paste fixture" };
        async Task Check(string name, Func<Task> test)
        {
            try { await test(); checks.Add(new { name = "Paste: " + name, status = "passed", detail = "Fake OS boundary; no clipboard writes or input." }); }
            catch (Exception error) { passed = false; checks.Add(new { name = "Paste: " + name, status = "failed", detail = error.GetType().Name }); }
        }
        static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Paste regression check failed."); }
        async Task Blocked(string name, Action<FakeClipboard, FakeDesktop> configure)
        {
            await Check(name, async () =>
            {
                var clipboard = new FakeClipboard(); var desktop = new FakeDesktop(); configure(clipboard, desktop);
                var result = await new PasteService(clipboard, desktop).PasteAsync(payload, target, true, () => { });
                Require(!result.InputSent && result.ClipboardWritten && desktop.SendCalls == 0);
            });
        }
        await Check("missing target copies without hiding", async () =>
        {
            var clipboard = new FakeClipboard(); var desktop = new FakeDesktop(); var hidden = false;
            var result = await new PasteService(clipboard, desktop).PasteAsync(payload, null, true, () => hidden = true);
            Require(result.ClipboardWritten && !result.InputSent && !hidden && desktop.SendCalls == 0);
        });
        await Blocked("exited or reused target sends no input", (_, d) => d.SameTarget = false);
        await Blocked("activation refusal sends no input", (_, d) => { d.CanActivate = false; d.Foreground = new IntPtr(2); });
        await Blocked("held shortcut keys stop automatic paste", (_, d) => d.Held = true);
        await Blocked("focus change before injection sends no input", (_, d) => d.Foreground = new IntPtr(2));
        await Blocked("secure desktop sends no input", (_, d) => d.Interactive = false);
        await Blocked("session locks after copy", (c, _) => c.IsLocked = true);
        await Blocked("newer clipboard sequence wins", (_, d) => d.Sequence = 8);
        await Check("partial injection releases synthetic modifiers", async () =>
        {
            var desktop = new FakeDesktop { AcceptedInputs = 1 };
            var result = await new PasteService(new FakeClipboard(), desktop).PasteAsync(payload, target, true, () => { });
            Require(!result.InputSent && desktop.SendCalls == 1 && desktop.ReleaseCalls == 1);
        });
        await Check("blocked injection does not release user modifiers", async () =>
        {
            var desktop = new FakeDesktop { AcceptedInputs = 0 };
            var result = await new PasteService(new FakeClipboard(), desktop).PasteAsync(payload, target, true, () => { });
            Require(!result.InputSent && desktop.ReleaseCalls == 0);
        });
        await Check("successful paste sends exactly one sequence", async () =>
        {
            var desktop = new FakeDesktop(); var hidden = false;
            var result = await new PasteService(new FakeClipboard(), desktop).PasteAsync(payload, target, true, () => hidden = true);
            Require(result.InputSent && result.ClipboardWritten && desktop.SendCalls == 1 && hidden);
        });
        await Check("expired request keeps retry available", async () =>
        {
            var clipboard = new FakeClipboard { Write = _ => throw new OperationCanceledException() }; var desktop = new FakeDesktop();
            var service = new PasteService(clipboard, desktop);
            var result = await service.PasteAsync(payload, target, true, () => { });
            Require(!result.InputSent && desktop.SendCalls == 0);
            clipboard.Write = _ => Task.FromResult(7u);
            Require((await service.PasteAsync(payload, target, true, () => { })).InputSent);
        });
        await Check("caller cancellation performs no clipboard write", async () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var clipboard = new FakeClipboard(); var service = new PasteService(clipboard, new FakeDesktop());
            try { await service.PasteAsync(payload, target, true, () => { }, cancellation.Token); }
            catch (OperationCanceledException) { Require(clipboard.WriteCalls == 0); return; }
            throw new InvalidOperationException("Caller cancellation was ignored.");
        });
        await Check("concurrent paste is rejected without replay", async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clipboard = new FakeClipboard { Write = _ => { started.SetResult(); return finish.Task; } };
            var service = new PasteService(clipboard, new FakeDesktop());
            var first = service.PasteAsync(payload, null, true, () => { }); await started.Task;
            var second = await service.PasteAsync(payload, target, true, () => { });
            finish.SetResult(7); await first;
            Require(!second.InputSent && !second.ClipboardWritten && clipboard.WriteCalls == 1);
        });
        return passed;
    }

    private sealed class FakeClipboard : IPasteClipboard
    {
        public bool IsLocked { get; set; }
        public int WriteCalls { get; private set; }
        public Func<CancellationToken, Task<uint>> Write { get; set; } = _ => Task.FromResult(7u);
        public Task<uint> WriteAsync(ClipPayload payload, bool preventCloud, CancellationToken ct = default)
        { WriteCalls++; return Write(ct); }
    }
    private sealed class FakeDesktop : IPasteDesktop
    {
        public bool SameTarget = true, CanActivate = true, Held, Interactive = true;
        public IntPtr Foreground = new(1);
        public uint Sequence = 7, AcceptedInputs = 4;
        public int SendCalls, ReleaseCalls;
        public bool IsSameTarget(PasteTarget target) => SameTarget;
        public bool SetForegroundWindow(IntPtr handle) => CanActivate;
        public IntPtr GetForegroundWindow() => Foreground;
        public bool KeysHeld() => Held;
        public bool IsInteractiveDesktop() => Interactive;
        public uint GetClipboardSequenceNumber() => Sequence;
        public uint SendPaste() { SendCalls++; return AcceptedInputs; }
        public void ReleaseKeys() => ReleaseCalls++;
    }
}
