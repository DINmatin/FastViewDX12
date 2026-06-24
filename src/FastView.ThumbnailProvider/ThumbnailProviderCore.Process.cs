using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FastView.ThumbnailProvider;

/// <summary>
/// Starts FastViewDX12 in headless thumbnail mode and records process diagnostics.
/// </summary>
internal static partial class ThumbnailProviderCore
{
    /// <summary>
    /// Starts the self-contained viewer beside the provider, captures diagnostics, enforces a timeout, and verifies the output file.
    /// </summary>
    private static void RunFastViewThumbnailCommand(
        string executablePath,
        string inputPath,
        string outputPath,
        int size)
    {
        string workingDirectory =
            Path.GetDirectoryName(
                executablePath)
            ?? throw new InvalidOperationException(
                "Could not determine the FastView working directory.");

        string providerLogPath =
            Path.Combine(
                Path.GetTempPath(),
                "FastView_ThumbnailProvider_LastRun.txt");

        var standardOutput =
            new StringBuilder();

        var standardError =
            new StringBuilder();

        using var process =
            new Process();

        process.StartInfo =
            new ProcessStartInfo
            {
                FileName =
                    executablePath,

                WorkingDirectory =
                    workingDirectory,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                WindowStyle =
                    ProcessWindowStyle.Hidden,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true
            };

        process.StartInfo.ArgumentList.Add(
            "--thumbnail");

        process.StartInfo.ArgumentList.Add(
            inputPath);

        process.StartInfo.ArgumentList.Add(
            outputPath);

        process.StartInfo.ArgumentList.Add(
            size.ToString());

        process.StartInfo.ArgumentList.Add(
            size.ToString());

        process.OutputDataReceived +=
            (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    standardOutput.AppendLine(
                        eventArgs.Data);
                }
            };

        process.ErrorDataReceived +=
            (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    standardError.AppendLine(
                        eventArgs.Data);
                }
            };

        WriteProviderRunLog(
            providerLogPath,
            executablePath,
            workingDirectory,
            inputPath,
            outputPath,
            size,
            null,
            standardOutput,
            standardError);

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "FastView thumbnail process could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited =
            process.WaitForExit(
                60_000);

        if (!exited)
        {
            try
            {
                process.Kill(
                    true);

                process.WaitForExit(
                    5_000);
            }
            catch
            {
            }

            WriteProviderRunLog(
                providerLogPath,
                executablePath,
                workingDirectory,
                inputPath,
                outputPath,
                size,
                "TIMEOUT after 60 seconds",
                standardOutput,
                standardError);

            throw new TimeoutException(
                "FastView thumbnail generation timed out. See " +
                providerLogPath);
        }

        process.WaitForExit();

        WriteProviderRunLog(
            providerLogPath,
            executablePath,
            workingDirectory,
            inputPath,
            outputPath,
            size,
            "Exit code: " +
            process.ExitCode,
            standardOutput,
            standardError);

        if (process.ExitCode != 0)
        {
            string errorPath =
                outputPath +
                ".error.txt";

            string details =
                File.Exists(
                    errorPath)
                    ? Environment.NewLine +
                      File.ReadAllText(
                          errorPath)
                    : string.Empty;

            throw new InvalidOperationException(
                $"FastView returned exit code " +
                $"{process.ExitCode}.{details}" +
                Environment.NewLine +
                "See " +
                providerLogPath);
        }

        if (!File.Exists(
            outputPath))
        {
            throw new FileNotFoundException(
                "FastView did not create the thumbnail PNG. See " +
                providerLogPath,
                outputPath);
        }
    }

    /// <summary>
    /// Writes one diagnostic record for the latest provider invocation. Logging failures never fail thumbnail generation.
    /// </summary>
    private static void WriteProviderRunLog(
        string logPath,
        string executablePath,
        string workingDirectory,
        string inputPath,
        string outputPath,
        int size,
        string? result,
        StringBuilder standardOutput,
        StringBuilder standardError)
    {
        try
        {
            long inputSize =
                File.Exists(
                    inputPath)
                    ? new FileInfo(
                        inputPath).Length
                    : -1;

            long outputSize =
                File.Exists(
                    outputPath)
                    ? new FileInfo(
                        outputPath).Length
                    : -1;

            File.WriteAllText(
                logPath,
                "Time: " +
                DateTime.Now.ToString("O") +
                Environment.NewLine +
                "Result: " +
                (result ?? "Starting") +
                Environment.NewLine +
                "EXE: " +
                executablePath +
                Environment.NewLine +
                "Working directory: " +
                workingDirectory +
                Environment.NewLine +
                "Input: " +
                inputPath +
                Environment.NewLine +
                "Input exists: " +
                File.Exists(inputPath) +
                Environment.NewLine +
                "Input bytes: " +
                inputSize +
                Environment.NewLine +
                "Output: " +
                outputPath +
                Environment.NewLine +
                "Output exists: " +
                File.Exists(outputPath) +
                Environment.NewLine +
                "Output bytes: " +
                outputSize +
                Environment.NewLine +
                "Requested size: " +
                size +
                " x " +
                size +
                Environment.NewLine +
                Environment.NewLine +
                "STDOUT:" +
                Environment.NewLine +
                standardOutput +
                Environment.NewLine +
                "STDERR:" +
                Environment.NewLine +
                standardError);
        }
        catch
        {
        }
    }

}
