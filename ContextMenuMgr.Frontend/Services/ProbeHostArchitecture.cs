using System.IO;

namespace ContextMenuMgr.Frontend.Services;

public enum ProbeHostArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64
}

public sealed record PeMachineTypeResult(
    ProbeHostArchitecture Architecture,
    string MachineType,
    string? RawValue,
    string? Error);

public static class PeMachineTypeDetector
{
    public static PeMachineTypeResult Detect(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new PeMachineTypeResult(ProbeHostArchitecture.Unknown, "unknown", null, "No file path.");
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return new PeMachineTypeResult(ProbeHostArchitecture.Unknown, "unknown", null, "File does not exist.");
            }

            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40)
            {
                return new PeMachineTypeResult(ProbeHostArchitecture.Unknown, "unknown", null, "File is too small.");
            }

            stream.Position = 0x3C;
            var peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset <= 0 || peHeaderOffset + 6 > stream.Length)
            {
                return new PeMachineTypeResult(ProbeHostArchitecture.Unknown, "unknown", null, "Invalid PE header offset.");
            }

            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return new PeMachineTypeResult(ProbeHostArchitecture.Unknown, "unknown", null, "Missing PE signature.");
            }

            var machine = reader.ReadUInt16();
            var raw = $"0x{machine:X4}";
            return machine switch
            {
                0x014C => new PeMachineTypeResult(ProbeHostArchitecture.X86, "x86", raw, null),
                0x8664 => new PeMachineTypeResult(ProbeHostArchitecture.X64, "x64", raw, null),
                0xAA64 => new PeMachineTypeResult(ProbeHostArchitecture.Arm64, "arm64", raw, null),
                0xA641 => new PeMachineTypeResult(ProbeHostArchitecture.Arm64, "arm64ec", raw, null),
                _ => new PeMachineTypeResult(ProbeHostArchitecture.Unknown, $"unknown({raw})", raw, null)
            };
        }
        catch (Exception ex)
        {
            return new PeMachineTypeResult(ProbeHostArchitecture.Unknown, "unknown", null, ex.Message);
        }
    }
}

public sealed record ProbeHostSelection(
    string? SelectedProbeHostPath,
    ProbeHostArchitecture SelectedProbeHostArchitecture,
    string? HandlerFilePath,
    bool HandlerFileExists,
    string HandlerMachineType,
    string? HandlerMachineRawValue,
    string Reason,
    string? FailureCode);
