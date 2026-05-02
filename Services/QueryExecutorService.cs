using System;
using System.Data;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace db_restore_tool.Services
{
    public static class QueryExecutorService
    {
        public static async Task ProcessResultsAsync(IDataReader reader, string outputDir, Action<string> messageCallback, CancellationToken cancellationToken)
        {
            int resultSetCount = 0;
            do
            {
                if (reader.FieldCount > 0)
                {
                    resultSetCount++;
                    if (!string.IsNullOrWhiteSpace(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                        string fileName = Path.Combine(outputDir, $"Result_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 4)}_{resultSetCount}.csv");

                        using var writer = new StreamWriter(fileName, false, Encoding.UTF8);
                        
                        // Write Headers
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            writer.Write(EscapeCsv(reader.GetName(i)));
                            if (i < reader.FieldCount - 1) writer.Write(",");
                        }
                        await writer.WriteLineAsync();

                        // Write Rows
                        int rowCount = 0;
                        while (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var val = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString();
                                writer.Write(EscapeCsv(val));
                                if (i < reader.FieldCount - 1) writer.Write(",");
                            }
                            await writer.WriteLineAsync();
                            rowCount++;
                        }
                        
                        messageCallback?.Invoke($"Result set {resultSetCount} ({rowCount} rows) exported to: {fileName}");
                    }
                    else
                    {
                        messageCallback?.Invoke($"Result set {resultSetCount} has {reader.FieldCount} columns. Output path not specified; results not saved.");
                        // just consume the reader
                        while (reader.Read()) { }
                    }
                }
                else if (reader.RecordsAffected >= 0)
                {
                    messageCallback?.Invoke($"({reader.RecordsAffected} rows affected)");
                }
            }
            while (reader.NextResult());
        }

        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\r") || field.Contains("\n"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
    }
}
