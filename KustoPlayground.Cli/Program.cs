using System.Globalization;
using System.Text;
using KustoPlayground.Cli;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = new UTF8Encoding(false);
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
using var error = Console.OpenStandardError();
return await Command.RunAsync(args, input, output, error).ConfigureAwait(false);
