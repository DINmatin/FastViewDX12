using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FastViewDX12;

/// <summary>
/// Selects interactive viewer startup or isolated Explorer thumbnail generation from command-line arguments.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Application entry point. Thumbnail mode is selected before any ToolStrip or SystemEvents initialization.
    /// </summary>
    [STAThread]
    private static void Main(
        string[] args)
    {
        bool thumbnailMode =
            args.Length > 0 &&
            args[0].Equals(
                "--thumbnail",
                StringComparison.OrdinalIgnoreCase);

        if (thumbnailMode)
        {
            RunThumbnailMode(
                args);

            return;
        }

        ApplicationConfiguration.Initialize();

        string? startupModelPath =
            args.Length > 0
                ? args[0]
                : null;

        Application.Run(
            new MainForm(
                startupModelPath));
    }

    /// <summary>
    /// Validates the thumbnail command and runs a minimal hidden render form.
    /// </summary>
    private static void RunThumbnailMode(
        string[] args)
    {
        TryWriteThumbnailStartupLog(
            args);

        try
        {
            if (!TryParseThumbnailCommand(
                args,
                out string inputPath,
                out string outputPath,
                out int width,
                out int height,
                out string errorMessage))
            {
                Environment.ExitCode =
                    2;

                TryWriteThumbnailError(
                    GetOutputPathIfAvailable(
                        args),
                    errorMessage);

                return;
            }

            // Do not call ApplicationConfiguration.Initialize() in thumbnail mode:
            // Explorer thumbnail generation needs neither visual styles
            // nor ToolStrip/SystemEvents initialization.
            Application.Run(
                new ThumbnailRenderForm(
                    inputPath,
                    outputPath,
                    width,
                    height));
        }
        catch (Exception exception)
        {
            Environment.ExitCode =
                1;

            TryWriteThumbnailError(
                GetOutputPathIfAvailable(
                    args),
                exception.ToString());
        }
    }

    /// <summary>
    /// Parses and validates source path, output path, and requested dimensions.
    /// </summary>
    private static bool TryParseThumbnailCommand(
        string[] args,
        out string inputPath,
        out string outputPath,
        out int width,
        out int height,
        out string errorMessage)
    {
        inputPath =
            string.Empty;

        outputPath =
            string.Empty;

        width =
            512;

        height =
            512;

        errorMessage =
            string.Empty;

        if (args.Length < 3 ||
            args.Length > 5)
        {
            errorMessage =
                "Usage: FastViewDX12.exe --thumbnail " +
                "\"input.glb\" \"output.png\" " +
                "[width] [height]";

            return false;
        }

        try
        {
            inputPath =
                Path.GetFullPath(
                    args[1]);

            outputPath =
                Path.GetFullPath(
                    args[2]);
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Invalid path: {exception.Message}";

            return false;
        }

        if (!File.Exists(
            inputPath))
        {
            errorMessage =
                $"Input model was not found: {inputPath}";

            return false;
        }

        string inputExtension =
            Path.GetExtension(
                inputPath);

        bool supportedInput =
            inputExtension.Equals(
                ".glb",
                StringComparison.OrdinalIgnoreCase) ||
            inputExtension.Equals(
                ".gltf",
                StringComparison.OrdinalIgnoreCase);

        if (!supportedInput)
        {
            errorMessage =
                "The input file must be a GLB or glTF file.";

            return false;
        }

        string outputExtension =
            Path.GetExtension(
                outputPath);

        if (string.IsNullOrWhiteSpace(
            outputExtension))
        {
            outputPath +=
                ".png";
        }
        else if (!outputExtension.Equals(
            ".png",
            StringComparison.OrdinalIgnoreCase))
        {
            errorMessage =
                "The output file must have the extension .png.";

            return false;
        }

        if (args.Length >= 4)
        {
            if (!TryParseDimension(
                args[3],
                out width))
            {
                errorMessage =
                    "Width must be a number between 64 and 2048.";

                return false;
            }

            height =
                width;
        }

        if (args.Length >= 5)
        {
            if (!TryParseDimension(
                args[4],
                out height))
            {
                errorMessage =
                    "Height must be a number between 64 and 2048.";

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Accepts thumbnail dimensions within the supported safety limits.
    /// </summary>
    private static bool TryParseDimension(
        string value,
        out int result)
    {
        return
            int.TryParse(
                value,
                out result) &&
            result >= 64 &&
            result <= 2048;
    }

    /// <summary>
    /// Returns a normalized output path for diagnostics when command parsing fails.
    /// </summary>
    private static string? GetOutputPathIfAvailable(
        string[] args)
    {
        if (args.Length < 3 ||
            string.IsNullOrWhiteSpace(
                args[2]))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(
                args[2]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a best-effort thumbnail error file without masking the original failure.
    /// </summary>
    private static void TryWriteThumbnailError(
        string? outputPath,
        string text)
    {
        try
        {
            string errorPath =
                !string.IsNullOrWhiteSpace(
                    outputPath)
                    ? outputPath + ".error.txt"
                    : Path.Combine(
                        Path.GetTempPath(),
                        "FastViewDX12_Thumbnail_Error.txt");

            File.WriteAllText(
                errorPath,
                text);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Records executable, assembly, working-directory, and argument details for COM troubleshooting.
    /// </summary>
    private static void TryWriteThumbnailStartupLog(
        string[] args)
    {
        try
        {
            string logPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "FastViewDX12_Thumbnail_Startup.txt");

            string executablePath =
                Environment.ProcessPath ??
                Process.GetCurrentProcess().MainModule?.FileName ??
                "<unknown>";

            string assemblyPath =
                typeof(Program).Assembly.Location;

            string logText =
                $"Time: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Executable: {executablePath}{Environment.NewLine}" +
                $"Assembly: {assemblyPath}{Environment.NewLine}" +
                $"Base directory: {AppContext.BaseDirectory}{Environment.NewLine}" +
                $"Working directory: {Environment.CurrentDirectory}{Environment.NewLine}" +
                $"Arguments ({args.Length}):{Environment.NewLine}" +
                string.Join(
                    Environment.NewLine,
                    args.Select(
                        (argument, index) =>
                            $"  [{index}] = {argument}"));

            File.WriteAllText(
                logPath,
                logText);
        }
        catch
        {
        }
    }
}
