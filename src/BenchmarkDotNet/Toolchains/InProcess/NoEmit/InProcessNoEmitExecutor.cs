﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using BenchmarkDotNet.Detectors;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Extensions;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.Parameters;
using BenchmarkDotNet.Toolchains.Results;

using JetBrains.Annotations;

namespace BenchmarkDotNet.Toolchains.InProcess.NoEmit
{
    /// <summary>
    /// Implementation of <see cref="IExecutor" /> for in-process (no emit) toolchain.
    /// </summary>
    [PublicAPI]
    [SuppressMessage("ReSharper", "ArrangeBraces_using")]
    public class InProcessNoEmitExecutor : IExecutor
    {
        private static readonly TimeSpan UnderDebuggerTimeout = TimeSpan.FromDays(1);

        /// <summary> Default timeout for in-process benchmarks. </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Initializes a new instance of the <see cref="InProcessNoEmitExecutor" /> class.</summary>
        /// <param name="timeout">Timeout for the run.</param>
        /// <param name="logOutput"><c>true</c> if the output should be logged.</param>
        public InProcessNoEmitExecutor(TimeSpan timeout, bool logOutput)
        {
            if (timeout == TimeSpan.Zero)
                timeout = DefaultTimeout;

            ExecutionTimeout = timeout;
            LogOutput = logOutput;
        }

        /// <summary>Timeout for the run.</summary>
        /// <value>The timeout for the run.</value>
        public TimeSpan ExecutionTimeout { get; }

        /// <summary>Gets a value indicating whether the output should be logged.</summary>
        /// <value><c>true</c> if the output should be logged; otherwise, <c>false</c>.</value>
        public bool LogOutput { get; }

        /// <summary>Executes the specified benchmark.</summary>
        public ExecuteResult Execute(ExecuteParameters executeParameters)
        {
            // TODO: preallocate buffer for output (no direct logging)?
            var hostLogger = LogOutput ? executeParameters.Logger : NullLogger.Instance;
            var host = new InProcessHost(executeParameters.BenchmarkCase, hostLogger, executeParameters.Diagnoser);

            int exitCode = -1;
            var runThread = new Thread(() => exitCode = ExecuteCore(host, executeParameters))
            {
                Name = "InProcess No Emit Executor"
            };

            if (executeParameters.BenchmarkCase.Descriptor.WorkloadMethod.GetCustomAttributes<STAThreadAttribute>(false).Any() &&
                OsDetector.IsWindows())
            {
                runThread.SetApartmentState(ApartmentState.STA);
            }

            runThread.IsBackground = true;

            var timeout = HostEnvironmentInfo.GetCurrent().HasAttachedDebugger ? UnderDebuggerTimeout : ExecutionTimeout;

            runThread.Start();

            if (!runThread.Join(timeout))
                throw new InvalidOperationException(
                    $"Benchmark {executeParameters.BenchmarkCase.DisplayInfo} takes too long to run. " +
                    "Prefer to use out-of-process toolchains for long-running benchmarks.");

            return ExecuteResult.FromRunResults(host.RunResults, exitCode);
        }

        private int ExecuteCore(IHost host, ExecuteParameters parameters)
        {
            int exitCode = -1;
            var process = Process.GetCurrentProcess();
            var oldPriority = process.PriorityClass;
            var oldAffinity = process.TryGetAffinity();
            var thread = Thread.CurrentThread;
            var oldThreadPriority = thread.Priority;

            var affinity = parameters.BenchmarkCase.Job.ResolveValueAsNullable(EnvironmentMode.AffinityCharacteristic);
            try
            {
                process.TrySetPriority(ProcessPriorityClass.High, parameters.Logger);
                thread.TrySetPriority(ThreadPriority.Highest, parameters.Logger);

                if (affinity != null)
                {
                    process.TrySetAffinity(affinity.Value, parameters.Logger);
                }

                exitCode = InProcessNoEmitRunner.Run(host, parameters.BenchmarkCase);
            }
            catch (Exception ex)
            {
                parameters.Logger.WriteLineError($"// ! {GetType().Name}, exception: {ex}");
            }
            finally
            {
                process.TrySetPriority(oldPriority, parameters.Logger);
                thread.TrySetPriority(oldThreadPriority, parameters.Logger);

                if (affinity != null && oldAffinity != null)
                {
                    process.TrySetAffinity(oldAffinity.Value, parameters.Logger);
                }
            }

            return exitCode;
        }
    }
}