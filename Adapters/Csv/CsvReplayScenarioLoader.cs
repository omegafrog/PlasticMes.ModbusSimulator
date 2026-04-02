using System.Globalization;
using PlasticMes.ModbusSimulator.Application;
using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Adapters.Csv;

public sealed class CsvReplayScenarioLoader : IReplayScenarioLoader
{
    public async Task<ReplayScenarioLoadResult> LoadAsync(string csvPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            return ReplayScenarioLoadResult.Failure("CSV path is required.");
        }

        try
        {
            var fullPath = Path.GetFullPath(csvPath);
            if (!File.Exists(fullPath))
            {
                return ReplayScenarioLoadResult.Failure($"CSV file was not found: {fullPath}");
            }

            var lines = await File.ReadAllLinesAsync(fullPath, ct);
            if (lines.Length < 2)
            {
                return ReplayScenarioLoadResult.Failure("CSV must contain a header row and at least one data row.");
            }

            var headerFields = ParseCsvLine(lines[0]);
            var header = ParseHeader(headerFields);
            if (!header.IsSuccess)
            {
                return ReplayScenarioLoadResult.Failure(header.ErrorMessage!);
            }

            var steps = new List<ReplayStep>();
            long? firstTimestamp = null;
            long? previousTimestamp = null;

            for (var index = 1; index < lines.Length; index++)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    continue;
                }

                var rowNumber = index + 1;
                var fields = ParseCsvLine(lines[index]);
                if (fields.Count != headerFields.Count)
                {
                    return ReplayScenarioLoadResult.Failure($"Row {rowNumber} has {fields.Count} cells but the header declares {headerFields.Count} columns.");
                }

                if (!long.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
                {
                    return ReplayScenarioLoadResult.Failure($"Row {rowNumber} contains an invalid time value '{fields[0]}'.");
                }

                if (timestamp < 0)
                {
                    return ReplayScenarioLoadResult.Failure($"Row {rowNumber} contains a negative time value.");
                }

                if (previousTimestamp is not null && timestamp < previousTimestamp.Value)
                {
                    return ReplayScenarioLoadResult.Failure($"Row {rowNumber} time is earlier than the previous row.");
                }

                firstTimestamp ??= timestamp;
                previousTimestamp = timestamp;

                var writes = new List<ModbusWrite>(header.Columns.Count);
                for (var columnIndex = 0; columnIndex < header.Columns.Count; columnIndex++)
                {
                    var rawValue = fields[columnIndex + 1].Trim();
                    if (rawValue.Length == 0)
                    {
                        return ReplayScenarioLoadResult.Failure($"Row {rowNumber} column '{header.Columns[columnIndex].HeaderName}' is empty.");
                    }

                    var write = TryParseWrite(header.Columns[columnIndex], rawValue, rowNumber, out var errorMessage);
                    if (write is null)
                    {
                        return ReplayScenarioLoadResult.Failure(errorMessage!);
                    }

                    writes.Add(write);
                }

                steps.Add(new ReplayStep(TimeSpan.FromMilliseconds(timestamp - firstTimestamp.Value), writes, rowNumber));
            }

            if (steps.Count == 0)
            {
                return ReplayScenarioLoadResult.Failure("CSV must contain at least one non-empty data row.");
            }

            return ReplayScenarioLoadResult.Success(new RegisterReplayScenario(fullPath, header.Columns, steps));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ReplayScenarioLoadResult.Failure(ex.Message);
        }
    }

    private static HeaderParseResult ParseHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count < 2)
        {
            return HeaderParseResult.Failure("CSV must declare a 'time' column and at least one register column.");
        }

        if (!string.Equals(fields[0].Trim(), "time", StringComparison.OrdinalIgnoreCase))
        {
            return HeaderParseResult.Failure("The first CSV column must be named 'time'.");
        }

        var columns = new List<ReplayColumn>(fields.Count - 1);
        var seenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < fields.Count; index++)
        {
            var headerName = fields[index].Trim();
            if (headerName.Length == 0)
            {
                return HeaderParseResult.Failure($"Header column {index + 1} is empty.");
            }

            if (!seenHeaders.Add(headerName))
            {
                return HeaderParseResult.Failure($"Header '{headerName}' is duplicated.");
            }

            if (!TryParseAddress(headerName, out var address, out var errorMessage))
            {
                return HeaderParseResult.Failure(errorMessage!);
            }

            columns.Add(new ReplayColumn(headerName, address));
        }

        return HeaderParseResult.Success(columns);
    }

    private static ModbusWrite? TryParseWrite(ReplayColumn column, string rawValue, int rowNumber, out string? errorMessage)
    {
        if (IsBitArea(column.Address.Area))
        {
            if (!TryParseBitValue(rawValue, out var bitValue))
            {
                errorMessage = $"Row {rowNumber} column '{column.HeaderName}' contains an invalid bit value '{rawValue}'.";
                return null;
            }

            errorMessage = null;
            var range = new ModbusRange(column.Address, 1);
            return new ModbusWrite(range, null, [bitValue]);
        }

        if (!ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var registerValue))
        {
            errorMessage = $"Row {rowNumber} column '{column.HeaderName}' contains an invalid register value '{rawValue}'.";
            return null;
        }

        errorMessage = null;
        var registerRange = new ModbusRange(column.Address, 1);
        return new ModbusWrite(registerRange, [registerValue], null);
    }

    private static bool TryParseAddress(string headerName, out ModbusAddress address, out string? errorMessage)
    {
        address = default;
        errorMessage = null;

        if (TryParseWithPrefix(headerName, "C", ModbusArea.Coil, 1, out address, out errorMessage) ||
            TryParseWithPrefix(headerName, "DI", ModbusArea.DiscreteInput, 10001, out address, out errorMessage) ||
            TryParseWithPrefix(headerName, "IR", ModbusArea.InputRegister, 30001, out address, out errorMessage) ||
            TryParseWithPrefix(headerName, "HR", ModbusArea.HoldingRegister, 40001, out address, out errorMessage))
        {
            return true;
        }

        errorMessage ??= $"Header '{headerName}' is not a supported Modbus reference.";
        return false;
    }

    private static bool TryParseWithPrefix(
        string headerName,
        string prefix,
        ModbusArea area,
        int baseAddress,
        out ModbusAddress address,
        out string? errorMessage)
    {
        address = default;
        errorMessage = null;

        if (!headerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numberText = headerName[prefix.Length..].Trim();
        if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reference))
        {
            errorMessage = $"Header '{headerName}' does not contain a valid numeric reference.";
            return false;
        }

        if (reference < baseAddress)
        {
            errorMessage = $"Header '{headerName}' is smaller than the {prefix} base reference {baseAddress}.";
            return false;
        }

        address = new ModbusAddress(area, reference - baseAddress);
        return true;
    }

    private static bool TryParseBitValue(string rawValue, out bool bitValue)
    {
        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
                bitValue = true;
                return true;
            case "0":
            case "false":
                bitValue = false;
                return true;
            default:
                bitValue = default;
                return false;
        }
    }

    private static bool IsBitArea(ModbusArea area) => area is ModbusArea.Coil or ModbusArea.DiscreteInput;

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        fields.Add(current.ToString());
        return fields;
    }

    private sealed record HeaderParseResult(bool IsSuccess, IReadOnlyList<ReplayColumn> Columns, string? ErrorMessage)
    {
        public static HeaderParseResult Success(IReadOnlyList<ReplayColumn> columns) => new(true, columns, null);

        public static HeaderParseResult Failure(string errorMessage) => new(false, [], errorMessage);
    }
}
