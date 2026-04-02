using PlasticMes.ModbusSimulator.Application;
using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Adapters.Memory;

public sealed class InMemoryModbusMemoryStore(
    int coilCapacity = 4096,
    int discreteInputCapacity = 4096,
    int inputRegisterCapacity = 4096,
    int holdingRegisterCapacity = 4096) : IModbusMemoryStore
{
    private readonly bool[] coils = new bool[coilCapacity];
    private readonly bool[] discreteInputs = new bool[discreteInputCapacity];
    private readonly ushort[] inputRegisters = new ushort[inputRegisterCapacity];
    private readonly ushort[] holdingRegisters = new ushort[holdingRegisterCapacity];
    private readonly Lock gate = new();

    public ModbusReadResult Read(ModbusRange range)
    {
        var validation = ValidateRange(range);
        if (validation is not null)
        {
            return ModbusReadResult.Failure(validation);
        }

        lock (gate)
        {
            return range.Start.Area switch
            {
                ModbusArea.Coil => ModbusReadResult.SuccessBits(coils.AsSpan(range.Start.Offset, range.Length).ToArray()),
                ModbusArea.DiscreteInput => ModbusReadResult.SuccessBits(discreteInputs.AsSpan(range.Start.Offset, range.Length).ToArray()),
                ModbusArea.InputRegister => ModbusReadResult.SuccessRegisters(inputRegisters.AsSpan(range.Start.Offset, range.Length).ToArray()),
                ModbusArea.HoldingRegister => ModbusReadResult.SuccessRegisters(holdingRegisters.AsSpan(range.Start.Offset, range.Length).ToArray()),
                _ => throw new InvalidOperationException($"Unsupported area {range.Start.Area}."),
            };
        }
    }

    public ModbusWriteResult WriteClient(ModbusWrite write) => Write(write, allowReadOnlyAreaWrite: false);

    public ModbusWriteResult WriteScenario(ModbusWrite write) => Write(write, allowReadOnlyAreaWrite: true);

    private ModbusWriteResult Write(ModbusWrite write, bool allowReadOnlyAreaWrite)
    {
        var validation = ValidateWrite(write, allowReadOnlyAreaWrite);
        if (validation is not null)
        {
            return ModbusWriteResult.Failure(validation);
        }

        lock (gate)
        {
            switch (write.Range.Start.Area)
            {
                case ModbusArea.Coil:
                    ApplyBits(coils, write);
                    break;
                case ModbusArea.DiscreteInput:
                    ApplyBits(discreteInputs, write);
                    break;
                case ModbusArea.InputRegister:
                    ApplyRegisters(inputRegisters, write);
                    break;
                case ModbusArea.HoldingRegister:
                    ApplyRegisters(holdingRegisters, write);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported area {write.Range.Start.Area}.");
            }
        }

        return ModbusWriteResult.Success();
    }

    private static void ApplyBits(bool[] target, ModbusWrite write)
    {
        for (var index = 0; index < write.Range.Length; index++)
        {
            target[write.Range.Start.Offset + index] = write.BitValues![index];
        }
    }

    private static void ApplyRegisters(ushort[] target, ModbusWrite write)
    {
        for (var index = 0; index < write.Range.Length; index++)
        {
            target[write.Range.Start.Offset + index] = write.RegisterValues![index];
        }
    }

    private ModbusProtocolError? ValidateWrite(ModbusWrite write, bool allowReadOnlyAreaWrite)
    {
        var rangeValidation = ValidateRange(write.Range);
        if (rangeValidation is not null)
        {
            return rangeValidation;
        }

        if (!allowReadOnlyAreaWrite &&
            (write.Range.Start.Area is ModbusArea.DiscreteInput or ModbusArea.InputRegister))
        {
            return new ModbusProtocolError(ModbusExceptionCode.IllegalFunction, $"Client writes are not allowed for {write.Range.Start.Area}.");
        }

        if (IsBitArea(write.Range.Start.Area))
        {
            if (write.BitValues is null || write.BitValues.Count != write.Range.Length)
            {
                return new ModbusProtocolError(ModbusExceptionCode.IllegalDataValue, "Bit payload length does not match the requested range.");
            }

            if (write.RegisterValues is not null)
            {
                return new ModbusProtocolError(ModbusExceptionCode.IllegalDataValue, "Bit writes cannot include register values.");
            }

            return null;
        }

        if (write.RegisterValues is null || write.RegisterValues.Count != write.Range.Length)
        {
            return new ModbusProtocolError(ModbusExceptionCode.IllegalDataValue, "Register payload length does not match the requested range.");
        }

        if (write.BitValues is not null)
        {
            return new ModbusProtocolError(ModbusExceptionCode.IllegalDataValue, "Register writes cannot include bit values.");
        }

        return null;
    }

    private ModbusProtocolError? ValidateRange(ModbusRange range)
    {
        if (range.Length == 0)
        {
            return new ModbusProtocolError(ModbusExceptionCode.IllegalDataValue, "Range length must be greater than zero.");
        }

        if (range.Start.Offset < 0)
        {
            return new ModbusProtocolError(ModbusExceptionCode.IllegalDataAddress, "Range offset must not be negative.");
        }

        var capacity = range.Start.Area switch
        {
            ModbusArea.Coil => coils.Length,
            ModbusArea.DiscreteInput => discreteInputs.Length,
            ModbusArea.InputRegister => inputRegisters.Length,
            ModbusArea.HoldingRegister => holdingRegisters.Length,
            _ => 0,
        };

        if (range.Start.Offset + range.Length > capacity)
        {
            return new ModbusProtocolError(ModbusExceptionCode.IllegalDataAddress, "Requested range exceeds the configured memory.");
        }

        return null;
    }

    private static bool IsBitArea(ModbusArea area) =>
        area is ModbusArea.Coil or ModbusArea.DiscreteInput;
}
