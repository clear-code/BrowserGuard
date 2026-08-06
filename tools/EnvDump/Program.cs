using System.Collections;
using System.Text;

const string OutputName = "envdump.txt";

var output = new StringBuilder();
output.AppendLine($"WorkingDirectory={Directory.GetCurrentDirectory()}");
output.AppendLine();
output.AppendLine("[Environment]");

var variables = Environment.GetEnvironmentVariables()
    .Cast<DictionaryEntry>()
    .OrderBy(entry => (string)entry.Key, StringComparer.OrdinalIgnoreCase);

foreach (var variable in variables)
{
    output.AppendLine($"{variable.Key}={variable.Value}");
}

try
{
    File.WriteAllText(OutputName, output.ToString(), new UTF8Encoding(false));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"cannot write {OutputName}: {ex.Message}");
    return 1;
}
