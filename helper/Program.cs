using System.Text;
using Wsg.OpenXR;

try {
    if (args.Length != 2 || args[0] != "--vector-flags" || args[1].Length > 32768) return 2;
    var changes = Json.Read<List<RegistryChange>>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1])));
    if (changes.Any(c => c.Before.Hive != "Machine")) return 2;
    OpenXrRegistry.SetVectorBatch(changes); return 0;
} catch { return 1; }
