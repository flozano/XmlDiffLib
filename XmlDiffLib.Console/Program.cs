using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XmlDiffLib;

namespace XmlDiffLibConsole
{
  #nullable enable
  internal static class Program
  {
    private static readonly char[] IgnoreTypeDelimiters = new[] { '|', ',', ';' };

    public static Task<int> Main(string[] args) => BuildCommand().InvokeAsync(args);

    private static RootCommand BuildCommand()
    {
      var fromFileArgument = new Argument<string>("fromFile")
      {
        Description = "Source XML file to compare from."
      };
      var toFileArgument = new Argument<string>("toFile")
      {
        Description = "Target XML file to compare against."
      };

      var outputOption = new Option<string?>(new[] { "-o", "--outfile" }, "Output file to write to. CSV files open in Excel if possible.");
      var csvOption = new Option<bool>("--csv", "Creates a diff CSV file. Defaults to JSON output.");
      var noMatchOption = new Option<bool>(new[] { "-m", "--nomatch" }, "Don't match text node value types (i.e. 0.00 != 0).");
      var ignoreTypesOption = new Option<string[]>(new[] { "--ignoretypes" }, () => Array.Empty<string>(), "If value matching is enabled, ignore the supplied value types. Supported values: string, integer, double, datetime. Multiple values may be separated by '|' or provided repeatedly.")
      {
        AllowMultipleArgumentsPerToken = true
      };
      var noDetailOption = new Option<bool>(new[] { "-d", "--nodetail" }, "Suppress descendant matching details.");
      var caseSensitiveOption = new Option<bool>(new[] { "-c", "--case" }, "Case sensitive comparison (default compares ignoring case).");
      var twoWayOption = new Option<bool>(new[] { "--2way", "--two-way" }, "Compare differences in both directions.");

      var root = new RootCommand("XmlDiff: Tool for finding the difference between two XML files.")
      {
        fromFileArgument,
        toFileArgument,
        outputOption,
        csvOption,
        noMatchOption,
        ignoreTypesOption,
        noDetailOption,
        caseSensitiveOption,
        twoWayOption
      };

      root.SetHandler(context =>
      {
        var parseResult = context.ParseResult;
        var exitCode = ExecuteDiff(
          parseResult.GetValueForArgument(fromFileArgument),
          parseResult.GetValueForArgument(toFileArgument),
          parseResult.GetValueForOption(outputOption),
          parseResult.GetValueForOption(csvOption),
          parseResult.GetValueForOption(noMatchOption),
          parseResult.GetValueForOption(ignoreTypesOption),
          parseResult.GetValueForOption(noDetailOption),
          parseResult.GetValueForOption(caseSensitiveOption),
          parseResult.GetValueForOption(twoWayOption));

        context.ExitCode = exitCode;
      });

      return root;
    }

    private static int ExecuteDiff(
      string fromFile,
      string toFile,
      string? outputFile,
      bool toCsv,
      bool disableValueMatching,
      string[]? ignoreTypes,
      bool suppressDetail,
      bool caseSensitive,
      bool twoWayComparison)
    {
      var diffOptions = BuildDiffOptions(disableValueMatching, ignoreTypes ?? Array.Empty<string>(), suppressDetail, caseSensitive, twoWayComparison);
      if (diffOptions is null)
        return 1;

      XmlDiff diff;
      try
      {
        diff = new XmlDiff(File.ReadAllText(fromFile), File.ReadAllText(toFile), Path.GetFileName(fromFile), Path.GetFileName(toFile));
        diff.CompareDocuments(diffOptions);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error: {ex.Message}");
        return 1;
      }

      return WriteOutput(diff, outputFile, toCsv);
    }

    private static XmlDiffOptions? BuildDiffOptions(
      bool disableValueMatching,
      string[] ignoreTypes,
      bool suppressDetail,
      bool caseSensitive,
      bool twoWayComparison)
    {
      var diffOptions = new XmlDiffOptions
      {
        MatchValueTypes = !disableValueMatching,
        MatchDescendants = !suppressDetail,
        IgnoreCase = !caseSensitive,
        TwoWayMatch = twoWayComparison
      };

      foreach (var rawValue in ignoreTypes)
      {
        var values = rawValue.Split(IgnoreTypeDelimiters, StringSplitOptions.RemoveEmptyEntries);
        foreach (var value in values)
        {
          switch (value.Trim().ToLowerInvariant())
          {
            case "string":
              diffOptions.IgnoreTextTypes.Add(XmlDiffOptions.IgnoreTextNodeOptions.XmlString);
              break;
            case "integer":
              diffOptions.IgnoreTextTypes.Add(XmlDiffOptions.IgnoreTextNodeOptions.XmlInteger);
              break;
            case "double":
              diffOptions.IgnoreTextTypes.Add(XmlDiffOptions.IgnoreTextNodeOptions.XmlDouble);
              break;
            case "datetime":
              diffOptions.IgnoreTextTypes.Add(XmlDiffOptions.IgnoreTextNodeOptions.XmlDateTime);
              break;
            default:
              Console.WriteLine("Error: Unrecognized value '{0}' for --ignoretypes. Allowed values are string, integer, double, datetime.", value.Trim());
              return null;
          }
        }
      }

      return diffOptions;
    }

    private static int WriteOutput(XmlDiff diff, string? outputFile, bool toCsv)
    {
      if (toCsv)
      {
        var targetFile = string.IsNullOrEmpty(outputFile) ? "xmldiff.csv" : outputFile;
        try
        {
          using (var writer = new StreamWriter(targetFile))
          {
            writer.Write(diff.ToCSVString());
          }

          TryOpenWithShell(targetFile);
        }
        catch (Exception ex)
        {
          Console.WriteLine($"Error: {ex.Message}");
          return 1;
        }
      }
      else if (string.IsNullOrEmpty(outputFile))
      {
        Console.WriteLine(diff.ToJsonString());
      }
      else
      {
        try
        {
          using (var writer = new StreamWriter(outputFile))
          {
            writer.WriteLine(diff.ToJsonString());
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine($"Error: {ex.Message}");
          return 1;
        }
      }

      return 0;
    }

    private static void TryOpenWithShell(string filePath)
    {
      try
      {
        var startInfo = new ProcessStartInfo
        {
          FileName = filePath,
          UseShellExecute = true
        };
        Process.Start(startInfo);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Warning: Unable to open '{filePath}'. {ex.Message}");
      }
    }
  }
}
