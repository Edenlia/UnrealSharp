using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var r = pe.GetMetadataReader();
byte[] raw = pe.GetMetadata().GetContent().ToArray();
var tables = new Dictionary<string, object>();
foreach (TableIndex table in Enum.GetValues<TableIndex>())
{
    int count = r.GetTableRowCount(table);
    if (count == 0) continue;
    int offset = r.GetTableMetadataOffset(table), size = count * r.GetTableRowSize(table);
    tables[table.ToString()] = new { count, offset, size, data = Convert.ToHexString(raw.AsSpan(offset, size)) };
}
var heaps = new Dictionary<string, object>();
foreach (HeapIndex heap in Enum.GetValues<HeapIndex>())
{
    int offset = r.GetHeapMetadataOffset(heap), size = r.GetHeapSize(heap);
    heaps[heap.ToString()] = new { offset, size, data = Convert.ToHexString(raw.AsSpan(offset, size)) };
}
var methods = r.MethodDefinitions.Select(h => { var m = r.GetMethodDefinition(h); return new { token = MetadataTokens.GetToken(h), name = r.GetString(m.Name), m.RelativeVirtualAddress }; }).ToArray();
File.WriteAllText(args[1], JsonSerializer.Serialize(new { path = args[0], machine = pe.PEHeaders.CoffHeader.Machine.ToString(), magic = pe.PEHeaders.PEHeader!.Magic.ToString(), flags = pe.PEHeaders.CorHeader!.Flags.ToString(), tables, heaps, methods }, new JsonSerializerOptions { WriteIndented = true }));
