using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agamemnon.DeviceControl;

// Function/Agamemnon.DeviceControl 设备控制包的无硬件检查：包隔离、统一连接/状态模型、
// 相机/位移台/激光门/缓冲加工的接口语义（用仿真实现驱动）。全部纯内存，不接硬件。
internal static class DeviceControlPackageChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        PackageIsolation(check);
        UnifiedModelChecks(check);
        CameraChecks(check);
        StageChecks(check);
        LaserGateChecks(check);
        ProcessingChecks(check);
    }

    private static void PackageIsolation(Action<bool, string> check)
    {
        var assembly = typeof(SimulatedStage).Assembly;
        check(assembly.GetName().Name == "Agamemnon.DeviceControl", "device package assembly is Agamemnon.DeviceControl");
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        check(!refs.Any(n => n != null && n.StartsWith("Agamemnon.")), "device package references no other Agamemnon assembly");
        check(typeof(ICameraDevice).IsAssignableFrom(typeof(SimulatedCamera)) && typeof(IStageController).IsAssignableFrom(typeof(SimulatedStage))
            && typeof(ILaserGateController).IsAssignableFrom(typeof(SimulatedLaserGate)) && typeof(IBufferedProcessingController).IsAssignableFrom(typeof(SimulatedBufferedProcessing)),
            "simulated devices implement their interfaces");
        var devices = new IDevice[] { new SimulatedCamera(), new SimulatedStage(), new SimulatedLaserGate(), new SimulatedBufferedProcessing() };
        check(devices.All(d => d.IsSimulated && d.State == DeviceConnectionState.Disconnected), "all simulated devices declare simulation and start disconnected");
        foreach (var device in devices) device.Dispose();
    }

    private static void UnifiedModelChecks(Action<bool, string> check)
    {
        using var stage = new SimulatedStage();
        var events = new List<DeviceStatus>();
        stage.StatusChanged += (_, status) => events.Add(status);
        stage.Connect();
        check(events.Select(s => s.State).SequenceEqual(new[] { DeviceConnectionState.Connecting, DeviceConnectionState.Connected }),
            "connect publishes connecting then connected");
        stage.Connect();
        check(events.Count(s => s.State == DeviceConnectionState.Connected) == 1, "second connect is idempotent without new events");
        stage.Disconnect(); stage.Disconnect();
        check(stage.State == DeviceConnectionState.Disconnected && events.Count == 3, "disconnect is idempotent and published once");
        check(!stage.IsConnected && stage.LastStatus != null && stage.LastStatus.ObservedAt > DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1),
            "last status carries a fresh observation");
        stage.Dispose();
        bool refused = false;
        try { ((IDevice)new SimulatedStage()).Dispose(); stage.Connect(); } catch (ObjectDisposedException) { refused = true; }
        check(refused, "disposed device cannot reconnect");
    }

    private static void CameraChecks(Action<bool, string> check)
    {
        using var camera = new SimulatedCamera();
        bool refused = false;
        try { camera.StartCapture(); } catch (InvalidOperationException) { refused = true; }
        check(refused, "capture requires a connected camera");
        camera.Connect(); camera.Connect();
        camera.StartCapture(); camera.StartCapture();
        check(camera.IsConnected && camera.IsCapturing && camera.IsBusy, "camera lifecycle is explicit and idempotent");
        int frames = 0; camera.FrameArrived += (_, _) => frames++;
        var first = camera.GrabFrame(); var second = camera.GrabFrame();
        check(first.WidthPx == 64 && first.HeightPx == 48 && first.Gray8.Length == 64 * 48, "frame dimensions and buffer agree");
        check(first.FrameIndex == 1 && second.FrameIndex == 2 && first.Gray8[0] == (byte)(1 * 7) && second.Gray8[0] == (byte)(2 * 7) && frames == 2,
            "frames are deterministic and raise FrameArrived");
        camera.StopCapture(); camera.StopCapture();
        refused = false;
        try { camera.GrabFrame(); } catch (InvalidOperationException) { refused = true; }
        check(refused && !camera.IsCapturing, "stopped camera cannot grab");
        var exposure = camera.Parameters.Single(p => p.Key == "exposure.ms");
        check(exposure.Minimum == 0.1 && exposure.Maximum == 1000 && exposure.Step == 0.1, "parameter catalogue declares range and step");
        Rejects<ArgumentOutOfRangeException>(() => camera.WriteParameter("exposure.ms", 0), check, "below-range exposure rejected");
        Rejects<ArgumentOutOfRangeException>(() => camera.WriteParameter("exposure.ms", 10.05), check, "off-step exposure rejected");
        Rejects<ArgumentException>(() => camera.ReadParameter("nope"), check, "unknown parameter key rejected");
        camera.WriteParameter("exposure.ms", 25.5);
        check(camera.ReadParameter("exposure.ms") == 25.5, "parameter write reads back");
        camera.Disconnect();
        check(!camera.IsConnected && !camera.IsCapturing, "disconnect clears capture state");
        Rejects<InvalidOperationException>(() => camera.WriteParameter("exposure.ms", 10), check, "write while disconnected rejected");
    }

    private static void StageChecks(Action<bool, string> check)
    {
        using var stage = new SimulatedStage();
        check(stage.AxisIds.SequenceEqual(new[] { "X", "Y", "Z" }) && !stage.HardwareHomingAvailable, "stage exposes three demo axes without hardware homing");
        check(stage.Snapshot().Axes.All(a => a.Powered == null), "initial power state is unknown, not false");
        Rejects<ArgumentException>(() => stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["Q"] = 1 }).GetAwaiter().GetResult(), check, "unknown axis rejected");
        stage.Connect();
        var cold = stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 1 }).GetAwaiter().GetResult();
        check(!cold.Accepted && cold.Error != null && cold.Error.Contains("上电"), "unpowered move rejected with explicit reason");
        stage.SetAxisPower("X", true);
        var clamped = stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 15 }).GetAwaiter().GetResult();
        check(clamped.Accepted && clamped.AnyClamped && clamped.EffectiveMm["X"] == 10 && clamped.RequestedMm["X"] == 15,
            "absolute move clamps to travel and reports it");
        stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 5 }).GetAwaiter().GetResult();
        var relative = stage.MoveRelativeAsync(new Dictionary<string, double> { ["X"] = 1.5 }).GetAwaiter().GetResult();
        check(relative.Accepted && !relative.AnyClamped && relative.EffectiveMm["X"] == 6.5, "relative move starts from current position");
        var jog = stage.JogStart("X", 5);
        check(jog.Accepted && stage.Snapshot().Axes.Single(a => a.AxisId == "X").Moving && stage.IsBusy, "jog marks the axis moving and busy");
        var blocked = stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 0 }).GetAwaiter().GetResult();
        check(!blocked.Accepted && blocked.Error.Contains("JOG"), "move during jog is rejected");
        stage.JogStop("X"); stage.StopAll();
        check(!stage.IsBusy && stage.Snapshot().Axes.Single(a => a.AxisId == "X").VelocityMmPerS == 0, "stop clears jog state");
        stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 2 }).GetAwaiter().GetResult();
        var zero = stage.SetSoftwareZero("X");
        var xAxis = stage.Snapshot().Axes.Single(a => a.AxisId == "X");
        check(zero.Accepted && xAxis.PositionMm == 0 && xAxis.MinimumMm == -12 && xAxis.MaximumMm == 8,
            "software zero shifts coordinates and travel together");
        Rejects<InvalidOperationException>(() => stage.HomeAsync("X").GetAwaiter().GetResult(), check, "hardware homing explicitly rejected");
        var cancelled = stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 1 }, new CancellationToken(true)).GetAwaiter().GetResult();
        check(!cancelled.Accepted && cancelled.Error.Contains("取消"), "pre-cancelled move reports cancellation");
        Rejects<ArgumentOutOfRangeException>(() => stage.JogStart("X", 0), check, "zero jog velocity rejected");
        stage.Disconnect();
        Rejects<InvalidOperationException>(() => stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 1 }).GetAwaiter().GetResult(), check, "disconnected stage rejects moves");
    }

    private static void LaserGateChecks(Action<bool, string> check)
    {
        using var gate = new SimulatedLaserGate();
        Rejects<InvalidOperationException>(() => gate.SetAsync(true).GetAwaiter().GetResult(), check, "disconnected gate rejected");
        gate.Connect();
        var off = gate.ReadAsync().GetAwaiter().GetResult();
        check(off.On == false && off.Error == null, "initial gate bit reads back as off");
        var on = gate.SetAsync(true).GetAwaiter().GetResult();
        check(on.On == true && gate.LastKnownOn == true, "gate set reads back the requested state");
        gate.SimulateReadbackFailure = true;
        var unknown = gate.ReadAsync().GetAwaiter().GetResult();
        check(unknown.On == null && unknown.Error != null && gate.LastKnownOn == null, "readback failure reports unknown instead of off");
        var setUnknown = gate.SetAsync(false).GetAwaiter().GetResult();
        check(setUnknown.On == null, "set with failing readback reports unknown, never pretends success");
        gate.SimulateReadbackFailure = false;
        gate.Disconnect();
        check(gate.LastKnownOn == null, "disconnected gate state is unknown");
    }

    private static void ProcessingChecks(Action<bool, string> check)
    {
        using var processing = new SimulatedBufferedProcessing();
        Dictionary<string, double> P(double x, double y) => new() { ["X"] = x, ["Y"] = y };
        var program = new BufferedProgram(Enumerable.Range(0, 5)
            .Select(i => new BufferedSegment(P(i, 0), P(i + 1, 0), 16, 10)).ToArray(), LaserEnabled: false);
        Rejects<InvalidOperationException>(() => processing.RunAsync(program).GetAwaiter().GetResult(), check, "disconnected processing rejected");
        processing.Connect();
        var dry = processing.RunAsync(program).GetAwaiter().GetResult();
        check(dry.Completed && dry.CompletedSegments == 5 && dry.TotalSegments == 5, "dry run completes all segments");
        check(!processing.Status.Running && !processing.Status.LaserRequested, "idle status after completion");
        Rejects<ArgumentException>(() => processing.RunAsync(new BufferedProgram(Array.Empty<BufferedSegment>(), false)).GetAwaiter().GetResult(), check, "empty program rejected");
        Rejects<ArgumentException>(() => processing.RunAsync(new BufferedProgram(new[] { new BufferedSegment(P(0, 0), P(1, 0), -1, 10) }, false)).GetAwaiter().GetResult(), check, "negative gate count rejected");
        Rejects<ArgumentException>(() => processing.RunAsync(new BufferedProgram(new[] { new BufferedSegment(P(0, 0), P(1, 0), 16, 0) }, false)).GetAwaiter().GetResult(), check, "zero speed rejected");
        Rejects<ArgumentException>(() => processing.RunAsync(new BufferedProgram(new[] { new BufferedSegment(P(0, 0), new Dictionary<string, double> { ["Y"] = 1 }, 16, 10) }, false)).GetAwaiter().GetResult(), check, "mismatched segment axes rejected");
        var cancelled = processing.RunAsync(program, new CancellationToken(true)).GetAwaiter().GetResult();
        check(cancelled.Cancelled && !cancelled.Completed, "pre-cancelled run reports cancellation");
        var laser = new BufferedProgram(program.Segments, LaserEnabled: true);
        var longProgram = new BufferedProgram(Enumerable.Range(0, 80).Select(i => new BufferedSegment(P(i, 0), P(i + 1, 0), 16, 10)).ToArray(), true);
        var running = Task.Run(() => processing.RunAsync(longProgram).GetAwaiter().GetResult());
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!processing.Status.Running && DateTime.UtcNow < deadline) Thread.Sleep(5);
        check(processing.Status.Running && processing.Status.LaserRequested, "running status exposes laser request flag");
        Rejects<InvalidOperationException>(() => processing.RunAsync(laser).GetAwaiter().GetResult(), check, "second program rejected while running");
        check(processing.Stop(), "stop request accepted while running");
        var stopped = running.GetAwaiter().GetResult();
        check(stopped.Stopped && !stopped.Completed && stopped.CompletedSegments < stopped.TotalSegments && !processing.IsBusy,
            "stopped run reports partial completion honestly");
        var real = processing.RunAsync(laser).GetAwaiter().GetResult();
        check(real.Completed && real.CompletedSegments == 5, "queued real-flag program runs to completion after stop");
    }

    private static void Rejects<TException>(Action action, Action<bool, string> check, string name) where TException : Exception
    {
        bool threw = false;
        try { action(); } catch (TException) { threw = true; } catch (Exception) { threw = false; }
        check(threw, name);
    }
}
