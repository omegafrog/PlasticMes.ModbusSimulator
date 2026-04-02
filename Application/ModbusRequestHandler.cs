using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public sealed class ModbusRequestHandler(IModbusMemoryStore memoryStore) : IModbusRequestHandler
{
    public const byte ReadCoilsFunctionCode = 0x01;
    public const byte ReadDiscreteInputsFunctionCode = 0x02;
    public const byte ReadHoldingRegistersFunctionCode = 0x03;
    public const byte ReadInputRegistersFunctionCode = 0x04;
    public const byte WriteSingleCoilFunctionCode = 0x05;
    public const byte WriteSingleRegisterFunctionCode = 0x06;
    public const byte WriteMultipleCoilsFunctionCode = 0x0F;
    public const byte WriteMultipleRegistersFunctionCode = 0x10;

    public Task<ModbusResponse> HandleAsync(ModbusRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(request.FunctionCode switch
        {
            ReadCoilsFunctionCode or ReadDiscreteInputsFunctionCode or ReadHoldingRegistersFunctionCode or ReadInputRegistersFunctionCode => HandleRead(request),
            WriteSingleCoilFunctionCode or WriteSingleRegisterFunctionCode or WriteMultipleCoilsFunctionCode or WriteMultipleRegistersFunctionCode => HandleWrite(request),
            _ => CreateErrorResponse(request, ModbusExceptionCode.IllegalFunction, $"Unsupported function code 0x{request.FunctionCode:X2}."),
        });
    }

    private ModbusResponse HandleRead(ModbusRequest request)
    {
        var result = memoryStore.Read(request.Range);
        if (!result.IsSuccess)
        {
            return CreateErrorResponse(request, result.Error!);
        }

        byte[] payload = request.FunctionCode switch
        {
            ReadCoilsFunctionCode or ReadDiscreteInputsFunctionCode when result.BitValues is not null => EncodeBitReadPayload(result.BitValues),
            ReadHoldingRegistersFunctionCode or ReadInputRegistersFunctionCode when result.RegisterValues is not null => EncodeRegisterReadPayload(result.RegisterValues),
            _ => [],
        };

        if (payload.Length == 0 && request.Range.Length > 0)
        {
            return CreateErrorResponse(request, ModbusExceptionCode.IllegalDataValue, "Decoded value shape does not match the requested area.");
        }

        return new ModbusResponse(request.TransactionId, request.UnitId, request.FunctionCode, payload);
    }

    private ModbusResponse HandleWrite(ModbusRequest request)
    {
        if (request.Write is null)
        {
            return CreateErrorResponse(request, ModbusExceptionCode.IllegalDataValue, "Write payload is missing.");
        }

        var result = memoryStore.WriteClient(request.Write);
        if (!result.IsSuccess)
        {
            return CreateErrorResponse(request, result.Error!);
        }

        var payload = request.FunctionCode switch
        {
            WriteSingleCoilFunctionCode => EncodeSingleCoilAck(request.Write),
            WriteSingleRegisterFunctionCode => EncodeSingleRegisterAck(request.Write),
            WriteMultipleCoilsFunctionCode or WriteMultipleRegistersFunctionCode => EncodeMultiWriteAck(request.Range),
            _ => [],
        };

        return new ModbusResponse(request.TransactionId, request.UnitId, request.FunctionCode, payload);
    }

    private static byte[] EncodeBitReadPayload(IReadOnlyList<bool> values)
    {
        var byteCount = (values.Count + 7) / 8;
        var payload = new byte[1 + byteCount];
        payload[0] = (byte)byteCount;

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index])
            {
                payload[1 + (index / 8)] |= (byte)(1 << (index % 8));
            }
        }

        return payload;
    }

    private static byte[] EncodeRegisterReadPayload(IReadOnlyList<ushort> values)
    {
        var payload = new byte[1 + (values.Count * sizeof(ushort))];
        payload[0] = (byte)(values.Count * sizeof(ushort));

        for (var index = 0; index < values.Count; index++)
        {
            WriteUInt16BigEndian(payload, 1 + (index * sizeof(ushort)), values[index]);
        }

        return payload;
    }

    private static byte[] EncodeSingleCoilAck(ModbusWrite write)
    {
        var payload = new byte[4];
        WriteUInt16BigEndian(payload, 0, checked((ushort)write.Range.Start.Offset));
        WriteUInt16BigEndian(payload, 2, write.BitValues![0] ? (ushort)0xFF00 : (ushort)0x0000);
        return payload;
    }

    private static byte[] EncodeSingleRegisterAck(ModbusWrite write)
    {
        var payload = new byte[4];
        WriteUInt16BigEndian(payload, 0, checked((ushort)write.Range.Start.Offset));
        WriteUInt16BigEndian(payload, 2, write.RegisterValues![0]);
        return payload;
    }

    private static byte[] EncodeMultiWriteAck(ModbusRange range)
    {
        var payload = new byte[4];
        WriteUInt16BigEndian(payload, 0, checked((ushort)range.Start.Offset));
        WriteUInt16BigEndian(payload, 2, range.Length);
        return payload;
    }

    private static void WriteUInt16BigEndian(Span<byte> buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }

    private static ModbusResponse CreateErrorResponse(ModbusRequest request, ModbusProtocolError error) =>
        new(request.TransactionId, request.UnitId, request.FunctionCode, ReadOnlyMemory<byte>.Empty, (byte)error.ExceptionCode, error);

    private static ModbusResponse CreateErrorResponse(ModbusRequest request, ModbusExceptionCode code, string message) =>
        CreateErrorResponse(request, new ModbusProtocolError(code, message));
}
