using System.Globalization;
using System.Text;
using KustoPlayground.Cli;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = new UTF8Encoding(false);
using var output = Console.OpenStandardOutput();
using var error = Console.OpenStandardError();
return Command.Run(args, output, error);
