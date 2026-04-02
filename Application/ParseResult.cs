using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public sealed record ParseResult<T>(T? Value, ModbusResponse? ErrorResponse)
{
    public bool IsSuccess => ErrorResponse is null;

    public static ParseResult<T> Success(T value) => new(value, null);

    public static ParseResult<T> Failure(ModbusResponse errorResponse) => new(default, errorResponse);
}
