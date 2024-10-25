using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Helpers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Tests.Loggers;
using BenchmarkDotNet.Tests.XUnit;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Xunit;
using Xunit.Abstractions;

namespace BenchmarkDotNet.IntegrationTests;

public class WakeLockTests : BenchmarkTestExecutor
{
    private static readonly string expectedRequesterName =
        Process.GetCurrentProcess().MainModule.FileName;

    public WakeLockTests(ITestOutputHelper output) : base(output) =>
        Assert.True(IsAdministrator(), "'powercfg /requests' requires administrator privileges and must be executed from an elevated command prompt.");

    [Fact]
    public void DefaultConfigValue()
    {
        Assert.Equal(WakeLockType.No, DefaultConfig.Instance.WakeLock);
        Assert.Equal(WakeLockType.No, new DebugBuildConfig().WakeLock);
        Assert.Equal(WakeLockType.No, new DebugInProcessConfig().WakeLock);
    }

    [TheoryEnvSpecific(EnvRequirement.NonWindows)]
    [InlineData(WakeLockType.No)]
    [InlineData(WakeLockType.RequireSystem)]
    [InlineData(WakeLockType.RequireDisplay)]
    public void WakeLockIsWindowsOnly(WakeLockType wakeLockType)
    {
        using IDisposable wakeLock = WakeLock.Request(wakeLockType, "dummy");
        Assert.Null(wakeLock);
    }

    [FactEnvSpecific(EnvRequirement.WindowsOnly)]
    public void SleepOrDisplayIsAllowed()
    {
        using IDisposable wakeLock = WakeLock.Request(WakeLockType.No, "dummy");
        Assert.Null(wakeLock);
    }

    [FactEnvSpecific(EnvRequirement.WindowsOnly)]
    public void RequireSystem()
    {
        using (IDisposable wakeLock = WakeLock.Request(WakeLockType.RequireSystem, "WakeLockTests"))
        {
            Assert.NotNull(wakeLock);
            Assert.Equal("SYSTEM", GetPowerRequests(expectedRequesterName, "WakeLockTests"));
        }
        Assert.Equal("", GetPowerRequests(expectedRequesterName));
    }

    [FactEnvSpecific(EnvRequirement.WindowsOnly)]
    public void RequireDisplay()
    {
        using (IDisposable wakeLock = WakeLock.Request(WakeLockType.RequireDisplay, "WakeLockTests"))
        {
            Assert.NotNull(wakeLock);
            Assert.Equal("DISPLAY, SYSTEM", GetPowerRequests(expectedRequesterName, "WakeLockTests"));
        }
        Assert.Equal("", GetPowerRequests(expectedRequesterName));
    }

    [FactEnvSpecific(EnvRequirement.NonWindows)]
    public void BenchmarkRunnerIgnoresWakeLock() =>
        _ = CanExecute<IgnoreWakeLock>(fullValidation: false);

    [WakeLock(WakeLockType.RequireDisplay)]
    public class IgnoreWakeLock
    {
        [Benchmark] public void Sleep() { }
    }

    [TheoryEnvSpecific(EnvRequirement.WindowsOnly)]
    [InlineData(typeof(Sleepy), "")]
    [InlineData(typeof(System), "SYSTEM")]
    [InlineData(typeof(Display), "DISPLAY, SYSTEM")]
    public void BenchmarkRunnerAcquiresWakeLock(Type type, string expected)
    {
        OutputLogger logger = new OutputLogger(Output);
        IConfig config = CreateSimpleConfig(logger);
        _ = CanExecute(type, config, false);
        Assert.Contains($"### {expected} ###", logger.GetLog());
    }

    public class Sleepy : Base { }

    [WakeLock(WakeLockType.RequireSystem)] public class System : Base { }

    [WakeLock(WakeLockType.RequireDisplay)] public class Display : Base { }

    public class Base
    {
        [Benchmark]
        public void Sleep() => Console.WriteLine(
            $"### {GetPowerRequests(expectedReason: "BenchmarkDotNet Running Benchmarks")} ###");
    }

    private static string GetPowerRequests(string? expectedRequesterName = null, string? expectedReason = null)
    {
        string pwrRequests = ProcessHelper.RunAndReadOutput("powercfg", "/requests");
        string mustEndWith = expectedRequesterName?.Substring(Path.GetPathRoot(expectedRequesterName).Length);

        return string.Join(", ",
            from pr in PowerRequestsParser.Parse(pwrRequests)
            where
                (mustEndWith == null || pr.RequesterName.EndsWith(mustEndWith, StringComparison.InvariantCulture)) &&
                string.Equals(pr.RequesterType, "PROCESS", StringComparison.InvariantCulture) &&
                (expectedReason == null || string.Equals(pr.Reason, expectedReason, StringComparison.InvariantCulture))
            select pr.RequestType);
    }

    private static bool IsAdministrator()
    {
#if !NET462
        // Following line prevents error CA1416: This call site is reachable on all platforms.
        // 'WindowsIdentity.GetCurrent()' is only supported on: 'windows'.
        Debug.Assert(OperatingSystem.IsWindows());
#endif
        using WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(currentUser).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
