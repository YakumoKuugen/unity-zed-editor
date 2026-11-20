using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// Thin wrapper around <see cref="Process"/> used to talk to the Zed CLI.
    /// The CLI forwards every open request over IPC to a running instance and exits, so calls are
    /// short lived - but they are still executed off the main thread to keep the editor responsive.
    /// </summary>
    internal static class ZedProcess
    {
        private const int k_DefaultLaunchTimeoutMs = 20000;

        /// <summary>Runs the executable and waits for it to exit. Never throws.</summary>
        public static bool TryRun(string executable, string arguments, string workingDirectory, int timeoutMs, out string standardOutput, out string standardError)
        {
            standardOutput = string.Empty;
            standardError = string.Empty;

            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                standardError = $"Executable not found: {executable}";
                return false;
            }

            Process process = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                    startInfo.WorkingDirectory = workingDirectory;

                process = Process.Start(startInfo);
                if (process == null)
                {
                    standardError = $"Failed to start process: {executable}";
                    return false;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                var exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    TryKill(process);
                    standardError = $"Timed out after {timeoutMs}ms while running: {executable} {arguments}";
                    return false;
                }

                // Streams are drained already since the process exited; the timeout is just a safety net.
                Task.WaitAny(new Task[] { outputTask, errorTask }, 2000);
                standardOutput = outputTask.Status == TaskStatus.RanToCompletion ? outputTask.Result : string.Empty;
                standardError = errorTask.Status == TaskStatus.RanToCompletion ? errorTask.Result : string.Empty;

                if (process.ExitCode == 0)
                    return true;

                if (string.IsNullOrEmpty(standardError))
                    standardError = $"'{executable} {arguments}' exited with code {process.ExitCode}.";
                return false;
            }
            catch (Exception exception)
            {
                standardError = exception.Message;
                return false;
            }
            finally
            {
                if (process != null)
                    process.Dispose();
            }
        }

        /// <summary>
        /// Starts the CLI on a background thread. <paramref name="workspaceArgument"/> (when set) is
        /// launched first so a cold Zed instance already has the Unity project as its worktree.
        /// </summary>
        public static void LaunchAsync(string executable, string workingDirectory, string workspaceArgument, string fileArgument, string description)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(workspaceArgument))
                    {
                        if (!TryRun(executable, workspaceArgument, workingDirectory, k_DefaultLaunchTimeoutMs, out _, out var workspaceError))
                            Debug.LogWarning($"[Zed] Could not open the project folder ({description}): {workspaceError.Trim()}");

                        // Give the Zed UI a moment to spin up/focus the workspace before asking for the file.
                        Thread.Sleep(200);
                    }

                    if (!string.IsNullOrEmpty(fileArgument))
                    {
                        if (!TryRun(executable, fileArgument, workingDirectory, k_DefaultLaunchTimeoutMs, out _, out var fileError))
                            Debug.LogWarning($"[Zed] Could not open '{description}': {fileError.Trim()}");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Zed] Failed to launch '{description}': {exception.Message}");
                }
            })
            {
                IsBackground = true,
                Name = "Zed.OpenProject",
            };

            thread.Start();
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // The process may have exited between the check and the kill call.
            }
        }
    }
}
