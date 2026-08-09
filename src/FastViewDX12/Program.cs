using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FastViewDX12;

internal static class Program
{
    [STAThread]
    private static void Main(
        string[] args)
    {
        bool thumbnailMode =
            args.Length > 0 &&
            args[0].Equals(
                "--thumbnail",
                StringComparison.OrdinalIgnoreCase);

        bool previewVideoMode =
            args.Length > 0 &&
            args[0].Equals(
                "--preview-video",
                StringComparison.OrdinalIgnoreCase);

        if (thumbnailMode)
        {
            RunThumbnailMode(
                args);

            return;
        }

        if (previewVideoMode)
        {
            RunPreviewVideoMode(args);
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

    private static void RunPreviewVideoMode(string[] args)
    {
        TryWriteThumbnailStartupLog(args);

        try
        {
            if (!TryParsePreviewVideoCommand(
                args,
                out string inputPath,
                out string outputPath,
                out int width,
                out int height,
                out int framesPerSecond,
                out int durationSeconds,
                out string errorMessage))
            {
                Environment.ExitCode = 2;
                TryWriteThumbnailError(
                    GetOutputPathIfAvailable(args),
                    errorMessage);
                return;
            }

            Application.Run(
                new PreviewVideoRenderForm(
                    inputPath,
                    outputPath,
                    width,
                    height,
                    framesPerSecond,
                    durationSeconds));
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            TryWriteThumbnailError(
                GetOutputPathIfAvailable(args),
                exception.ToString());
        }
    }

    private static bool TryParsePreviewVideoCommand(
        string[] args,
        out string inputPath,
        out string outputPath,
        out int width,
        out int height,
        out int framesPerSecond,
        out int durationSeconds,
        out string errorMessage)
    {
        inputPath = string.Empty;
        outputPath = string.Empty;
        width = 1920;
        height = 1080;
        framesPerSecond = 30;
        durationSeconds = 8;
        errorMessage = string.Empty;

        if (args.Length < 3 || args.Length > 7)
        {
            errorMessage =
                "Usage: FastViewDX12.exe --preview-video " +
                "\"input-model\" \"output.mp4\" " +
                "[width] [height] [fps] [seconds]";
            return false;
        }

        try
        {
            inputPath = Path.GetFullPath(args[1]);
            outputPath = Path.GetFullPath(args[2]);
        }
        catch (Exception exception)
        {
            errorMessage = $"Invalid path: {exception.Message}";
            return false;
        }

        if (!File.Exists(inputPath) || !ModelSceneLoader.IsSupportedPath(inputPath))
        {
            errorMessage = "The input model does not exist or has an unsupported format.";
            return false;
        }

        if (!Path.GetExtension(outputPath).Equals(
            ".mp4",
            StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "The output file must have the extension .mp4.";
            return false;
        }

        if (args.Length >= 4 && !TryParseEvenValue(args[3], 64, 3840, out width))
        {
            errorMessage = "Width must be an even number between 64 and 3840.";
            return false;
        }

        if (args.Length >= 5 && !TryParseEvenValue(args[4], 64, 2160, out height))
        {
            errorMessage = "Height must be an even number between 64 and 2160.";
            return false;
        }

        if (args.Length >= 6 &&
            (!int.TryParse(args[5], out framesPerSecond) ||
             framesPerSecond is < 1 or > 60))
        {
            errorMessage = "FPS must be a number between 1 and 60.";
            return false;
        }

        if (args.Length >= 7 &&
            (!int.TryParse(args[6], out durationSeconds) ||
             durationSeconds is < 1 or > 60))
        {
            errorMessage = "Duration must be a number between 1 and 60 seconds.";
            return false;
        }

        return true;
    }

    private static bool TryParseEvenValue(
        string value,
        int minimum,
        int maximum,
        out int result)
    {
        return int.TryParse(value, out result) &&
            result >= minimum &&
            result <= maximum &&
            (result & 1) == 0;
    }

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

            // Absichtlich kein ApplicationConfiguration.Initialize():
            // Der Explorer-Thumbnailprozess benötigt weder Visual Styles
            // noch ToolStrip-/SystemEvents-Initialisierung.
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
                "\"input-model\" \"output.png\" " +
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

        if (!ModelSceneLoader.IsSupportedPath(inputPath))
        {
            errorMessage =
                "The input file must be a GLB, glTF, FBX, or OBJ file.";

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
