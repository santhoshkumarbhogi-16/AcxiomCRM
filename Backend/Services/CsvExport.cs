using System.Globalization;
using System.Text;

namespace AcxiomCRM.Services;

public static class CsvExport
{
    public static byte[] Create<T>(
        IEnumerable<T> rows,
        params (string Header, Func<T, object?> Value)[] columns)
    {
        var output = new StringBuilder();
        output.AppendLine(string.Join(",", columns.Select(column => Escape(column.Header))));
        foreach (var row in rows)
            output.AppendLine(string.Join(",", columns.Select(column =>
                Escape(Convert.ToString(column.Value(row), CultureInfo.InvariantCulture) ?? ""))));
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(output.ToString());
    }

    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = $"'{value}";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
