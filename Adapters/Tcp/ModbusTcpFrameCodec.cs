using PlasticMes.ModbusSimulator.Application;
using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Adapters.Tcp;

public sealed class ModbusTcpFrameCodec : IModbusFrameCodec
{
    private const ushort ProtocolIdentifier = 0x0000;

    public ParseResult<ModbusRequest> Decode(ReadOnlyMemory<byte> payload)
    {
        var buffer = payload.Span;
        if (buffer.Length < 8)
        {
            return ParseResult<ModbusRequest>.Failure(CreateDecodeErrorResponse(0, 0, 0, ModbusExceptionCode.IllegalDataValue, "Frame is shorter than the Modbus TCP minimum."));
        }

        var transactionId = ReadUInt16BigEndian(buffer, 0);
        var protocolId = ReadUInt16BigEndian(buffer, 2);
        var declaredLength = ReadUInt16BigEndian(buffer, 4);
        var unitId = buffer[6];
        var functionCode = buffer[7];

        if (protocolId != ProtocolIdentifier)
        {
            return ParseResult<ModbusRequest>.Failure(CreateDecodeErrorResponse(transactionId, unitId, functionCode, ModbusExceptionCode.IllegalDataValue, "Protocol identifier must be 0x0000."));
        }

        if (declaredLength < 2)
        {
            return ParseResult<ModbusRequest>.Failure(CreateDecodeErrorResponse(transactionId, unitId, functionCode, ModbusExceptionCode.IllegalDataValue, "MBAP length must include unit id and function code."));
        }

        if (buffer.Length != declaredLength + 6)
        {
            return ParseResult<ModbusRequest>.Failure(CreateDecodeErrorResponse(transactionId, unitId, functionCode, ModbusExceptionCode.IllegalDataValue, "Declared MBAP length does not match the frame length."));
        }

        try
        {
            var request = functionCode switch
            {
                ModbusRequestHandler.ReadCoilsFunctionCode => ParseReadRequest(buffer, transactionId, unitId, functionCode, ModbusArea.Coil, 2000),
                ModbusRequestHandler.ReadDiscreteInputsFunctionCode => ParseReadRequest(buffer, transactionId, unitId, functionCode, ModbusArea.DiscreteInput, 2000),
                ModbusRequestHandler.ReadHoldingRegistersFunctionCode => ParseReadRequest(buffer, transactionId, unitId, functionCode, ModbusArea.HoldingRegister, 125),
                ModbusRequestHandler.ReadInputRegistersFunctionCode => ParseReadRequest(buffer, transactionId, unitId, functionCode, ModbusArea.InputRegister, 125),
                ModbusRequestHandler.WriteSingleCoilFunctionCode => ParseSingleCoilWrite(buffer, transactionId, unitId, functionCode),
                ModbusRequestHandler.WriteSingleRegisterFunctionCode => ParseSingleRegisterWrite(buffer, transactionId, unitId, functionCode),
                ModbusRequestHandler.WriteMultipleCoilsFunctionCode => ParseMultipleCoilWrite(buffer, transactionId, unitId, functionCode),
                ModbusRequestHandler.WriteMultipleRegistersFunctionCode => ParseMultipleRegisterWrite(buffer, transactionId, unitId, functionCode),
                _ => new ModbusRequest(transactionId, unitId, functionCode, default),
            };

            return ParseResult<ModbusRequest>.Success(request);
        }
        catch (InvalidOperationException ex)
        {
            return ParseResult<ModbusRequest>.Failure(CreateDecodeErrorResponse(transactionId, unitId, functionCode, ModbusExceptionCode.IllegalDataValue, ex.Message));
        }
    }

    public ReadOnlyMemory<byte> Encode(ModbusResponse response)
    {
        var isException = response.ExceptionCode.HasValue;
        var pduLength = 1 + (isException ? 1 : response.Data.Length);
        var length = checked((ushort)(1 + pduLength));
        var frame = new byte[6 + length];

        WriteUInt16BigEndian(frame, 0, response.TransactionId);
        WriteUInt16BigEndian(frame, 2, ProtocolIdentifier);
        WriteUInt16BigEndian(frame, 4, length);
        frame[6] = response.UnitId;
        frame[7] = isException
            ? (byte)(response.FunctionCode | 0x80)
            : response.FunctionCode;

        if (isException)
        {
            frame[8] = response.ExceptionCode!.Value;
        }
        else
        {
            response.Data.Span.CopyTo(frame.AsSpan(8));
        }

        return frame;
    }

    private static ModbusRequest ParseReadRequest(
        ReadOnlySpan<byte> buffer,
        ushort transactionId,
        byte unitId,
        byte functionCode,
        ModbusArea area,
        ushort maxQuantity)
    {
        EnsurePduLength(buffer, 4, "Read request");
        var offset = ReadUInt16BigEndian(buffer, 8);
        var quantity = ReadUInt16BigEndian(buffer, 10);
        ValidateQuantity(quantity, maxQuantity);

        return new ModbusRequest(transactionId, unitId, functionCode, new ModbusRange(new ModbusAddress(area, offset), quantity));
    }

    private static ModbusRequest ParseSingleCoilWrite(ReadOnlySpan<byte> buffer, ushort transactionId, byte unitId, byte functionCode)
    {
        EnsurePduLength(buffer, 4, "Write single coil request");
        var offset = ReadUInt16BigEndian(buffer, 8);
        var rawValue = ReadUInt16BigEndian(buffer, 10);
        var value = rawValue switch
        {
            0xFF00 => true,
            0x0000 => false,
            _ => throw new InvalidOperationException("Single coil writes must use 0xFF00 or 0x0000."),
        };

        var range = new ModbusRange(new ModbusAddress(ModbusArea.Coil, offset), 1);
        return new ModbusRequest(transactionId, unitId, functionCode, range, new ModbusWrite(range, null, [value]));
    }

    private static ModbusRequest ParseSingleRegisterWrite(ReadOnlySpan<byte> buffer, ushort transactionId, byte unitId, byte functionCode)
    {
        EnsurePduLength(buffer, 4, "Write single register request");
        var offset = ReadUInt16BigEndian(buffer, 8);
        var value = ReadUInt16BigEndian(buffer, 10);
        var range = new ModbusRange(new ModbusAddress(ModbusArea.HoldingRegister, offset), 1);
        return new ModbusRequest(transactionId, unitId, functionCode, range, new ModbusWrite(range, [value], null));
    }

    private static ModbusRequest ParseMultipleCoilWrite(ReadOnlySpan<byte> buffer, ushort transactionId, byte unitId, byte functionCode)
    {
        EnsurePduLength(buffer, 5, "Write multiple coils request");
        var offset = ReadUInt16BigEndian(buffer, 8);
        var quantity = ReadUInt16BigEndian(buffer, 10);
        ValidateQuantity(quantity, 1968);
        var byteCount = buffer[12];
        var expectedByteCount = (quantity + 7) / 8;
        if (byteCount != expectedByteCount)
        {
            throw new InvalidOperationException("Coil byte count does not match the requested quantity.");
        }

        EnsurePduLength(buffer, 5 + byteCount, "Write multiple coils request");
        var values = new List<bool>(quantity);
        for (var index = 0; index < quantity; index++)
        {
            values.Add((buffer[13 + (index / 8)] & (1 << (index % 8))) != 0);
        }

        var range = new ModbusRange(new ModbusAddress(ModbusArea.Coil, offset), quantity);
        return new ModbusRequest(transactionId, unitId, functionCode, range, new ModbusWrite(range, null, values));
    }

    private static ModbusRequest ParseMultipleRegisterWrite(ReadOnlySpan<byte> buffer, ushort transactionId, byte unitId, byte functionCode)
    {
        EnsurePduLength(buffer, 5, "Write multiple registers request");
        var offset = ReadUInt16BigEndian(buffer, 8);
        var quantity = ReadUInt16BigEndian(buffer, 10);
        ValidateQuantity(quantity, 123);
        var byteCount = buffer[12];
        var expectedByteCount = quantity * sizeof(ushort);
        if (byteCount != expectedByteCount)
        {
            throw new InvalidOperationException("Register byte count does not match the requested quantity.");
        }

        EnsurePduLength(buffer, 5 + byteCount, "Write multiple registers request");
        var values = new List<ushort>(quantity);
        for (var index = 0; index < quantity; index++)
        {
            values.Add(ReadUInt16BigEndian(buffer, 13 + (index * sizeof(ushort))));
        }

        var range = new ModbusRange(new ModbusAddress(ModbusArea.HoldingRegister, offset), quantity);
        return new ModbusRequest(transactionId, unitId, functionCode, range, new ModbusWrite(range, values, null));
    }

    private static void EnsurePduLength(ReadOnlySpan<byte> buffer, int expectedBytesAfterFunctionCode, string operation)
    {
        var pduLength = buffer.Length - 7;
        if (pduLength != 1 + expectedBytesAfterFunctionCode)
        {
            throw new InvalidOperationException($"{operation} length is invalid.");
        }
    }

    private static void ValidateQuantity(ushort quantity, ushort maxQuantity)
    {
        if (quantity == 0 || quantity > maxQuantity)
        {
            throw new InvalidOperationException($"Quantity must be between 1 and {maxQuantity}.");
        }
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    private static void WriteUInt16BigEndian(Span<byte> buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }

    private static ModbusResponse CreateDecodeErrorResponse(
        ushort transactionId,
        byte unitId,
        byte functionCode,
        ModbusExceptionCode exceptionCode,
        string message) =>
        new(transactionId, unitId, functionCode, ReadOnlyMemory<byte>.Empty, (byte)exceptionCode, new ModbusProtocolError(exceptionCode, message));
}
