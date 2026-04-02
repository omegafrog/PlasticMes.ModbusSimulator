using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public interface IModbusFrameCodec
{
    ParseResult<ModbusRequest> Decode(ReadOnlyMemory<byte> payload);

    ReadOnlyMemory<byte> Encode(ModbusResponse response);
}
