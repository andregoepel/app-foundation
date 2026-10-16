using System.Diagnostics;
using System.Text;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record CommandOutput(int ExitCode, string Text);

internal interface IProcessRunner
{
    Task<Result<CommandOutput>> RunAsync(
        string root,
        string[] arguments,
        CancellationToken cancellationToken
    );
}

internal sealed class ProcessRunner : IProcessRunner
{
    internal static ProcessStartInfo CreateStartInfo(string root, string[] arguments)
    {
        var start = new ProcessStartInfo(arguments[0])
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments.Skip(1))
            start.ArgumentList.Add(argument);

        start.Environment.Remove("APPFOUNDATION_SONAR_TOKEN");
        start.Environment.Remove("APPFOUNDATION_SONAR_VERIFY_TOKEN");
        start.Environment.Remove("APPFOUNDATION_GITHUB_TOKEN");
        start.Environment.Remove("GITHUB_TOKEN");
        start.Environment.Remove("GH_TOKEN");
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.Environment["NO_COLOR"] = "1";

        return start;
    }

    public async Task<Result<CommandOutput>> RunAsync(
        string root,
        string[] arguments,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = CreateStartInfo(root, arguments) };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));

        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                await process.WaitForExitAsync(CancellationToken.None);

                return Result.Fail<CommandOutput>(
                    "Command cancelled or exceeded its 30-minute limit."
                );
            }

            return Result.Ok(new CommandOutput(process.ExitCode, await output + await error));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Result.Fail<CommandOutput>(
                "Could not start a required command. Check the documented prerequisites."
            );
        }
    }
}
