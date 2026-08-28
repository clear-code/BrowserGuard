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
    // Written under another name and moved into place, so that whoever sees
    // the file sees all of it. Writing straight to it leaves a window where
    // it exists and is still empty, which is what a watcher would read.
    var partial = OutputName + ".tmp";
    File.WriteAllText(partial, output.ToString(), new UTF8Encoding(false));
    File.Move(partial, OutputName, overwrite: true);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"cannot write {OutputName}: {ex.Message}");
    return 1;
}
