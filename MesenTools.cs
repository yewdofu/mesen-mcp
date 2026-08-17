using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpServers.MesenCE;

[McpServerToolType]
public static class MesenTools
{
    [McpServerTool(Name = "mesen_get_status", ReadOnly = true, OpenWorld = false)]
    [Description("Gets the current MesenCE ROM and emulation status.")]
    public static Task<JsonElement> GetStatus(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("system.getStatus", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_pause", Idempotent = true, OpenWorld = false)]
    [Description("Pauses SNES emulation and waits until the debugger has stopped.")]
    public static Task<JsonElement> Pause(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("debug.pause", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_resume", Idempotent = true, OpenWorld = false)]
    [Description("Resumes SNES emulation and waits until execution has restarted.")]
    public static Task<JsonElement> Resume(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("debug.resume", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_step", OpenWorld = false)]
    [Description("Executes one SNES CPU instruction. Emulation must already be paused.")]
    public static Task<JsonElement> Step(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("debug.step", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_get_current_instruction", ReadOnly = true, OpenWorld = false)]
    [Description("Gets the SNES CPU instruction at the current program counter. Emulation must be paused.")]
    public static Task<JsonElement> GetCurrentInstruction(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("debug.getCurrentInstruction", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_get_registers", ReadOnly = true, OpenWorld = false)]
    [Description("Gets all SNES 65816 CPU registers. Emulation must be paused.")]
    public static Task<JsonElement> GetRegisters(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("cpu.getRegisters", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_set_registers", OpenWorld = false)]
    [Description("Updates only the specified SNES 65816 CPU registers. Emulation must be paused.")]
    public static Task<JsonElement> SetRegisters(
        MesenDebugClient client,
        [Description("Accumulator value from 0 to 65535.")] int? a = null,
        [Description("X register value from 0 to 65535.")] int? x = null,
        [Description("Y register value from 0 to 65535.")] int? y = null,
        [Description("Stack pointer value from 0 to 65535.")] int? sp = null,
        [Description("Direct page register value from 0 to 65535.")] int? d = null,
        [Description("Program counter value from 0 to 65535.")] int? pc = null,
        [Description("Program bank register value from 0 to 255.")] int? k = null,
        [Description("Data bank register value from 0 to 255.")] int? dbr = null,
        [Description("Processor status value from 0 to 255.")] int? ps = null,
        [Description("Whether the CPU is in emulation mode.")] bool? emulationMode = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>();
        AddIfSpecified(parameters, "a", a);
        AddIfSpecified(parameters, "x", x);
        AddIfSpecified(parameters, "y", y);
        AddIfSpecified(parameters, "sp", sp);
        AddIfSpecified(parameters, "d", d);
        AddIfSpecified(parameters, "pc", pc);
        AddIfSpecified(parameters, "k", k);
        AddIfSpecified(parameters, "dbr", dbr);
        AddIfSpecified(parameters, "ps", ps);
        AddIfSpecified(parameters, "emulationMode", emulationMode);
        return client.InvokeAsync("cpu.setRegisters", parameters, cancellationToken);
    }

    [McpServerTool(Name = "mesen_list_memory_regions", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the SNES memory regions available through the MesenCE debug API.")]
    public static Task<JsonElement> ListMemoryRegions(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("memory.list", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_read_memory", ReadOnly = true, OpenWorld = false)]
    [Description("Reads SNES memory and returns its bytes as an uppercase hexadecimal string. Emulation must be paused.")]
    public static async Task<MemoryReadResult> ReadMemory(
        MesenDebugClient client,
        [Description("Memory region ID returned by mesen_list_memory_regions, such as SnesWorkRam.")] string type,
        [Description("Zero-based byte address within the selected memory region.")] long address,
        [Description("Number of bytes to read, from 1 to 65536.")] int length,
        CancellationToken cancellationToken)
    {
        JsonElement result = await client.InvokeAsync(
            "memory.read",
            new { type, address, length },
            cancellationToken).ConfigureAwait(false);

        string encodedData = result.GetProperty("data").GetString()
            ?? throw new McpException("MesenCE returned memory data without a value.");
        byte[] bytes = Convert.FromBase64String(encodedData);
        return new MemoryReadResult(result.GetProperty("address").GetInt64(), bytes.Length, Convert.ToHexString(bytes));
    }

    [McpServerTool(Name = "mesen_write_memory", OpenWorld = false)]
    [Description("Writes bytes represented by a hexadecimal string to SNES memory. Emulation must be paused.")]
    public static Task<JsonElement> WriteMemory(
        MesenDebugClient client,
        [Description("Memory region ID returned by mesen_list_memory_regions, such as SnesWorkRam.")] string type,
        [Description("Zero-based byte address within the selected memory region.")] long address,
        [Description("An even-length hexadecimal byte string without separators, containing 1 to 65536 bytes.")] string hex,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException exception)
        {
            throw new McpException("hex must be an even-length hexadecimal string without separators.", exception);
        }

        if (bytes.Length is < 1 or > 65536)
        {
            throw new McpException("hex must contain between 1 and 65536 bytes.");
        }

        return client.InvokeAsync(
            "memory.write",
            new { type, address, data = Convert.ToBase64String(bytes) },
            cancellationToken);
    }

    [McpServerTool(Name = "mesen_list_breakpoints", ReadOnly = true, OpenWorld = false)]
    [Description("Lists breakpoints created through the external MesenCE debug API.")]
    public static Task<JsonElement> ListBreakpoints(
        MesenDebugClient client,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("breakpoint.list", cancellationToken: cancellationToken);

    [McpServerTool(Name = "mesen_add_breakpoint", OpenWorld = false)]
    [Description("Adds a non-persistent MesenCE breakpoint that is removed when this MCP server disconnects.")]
    public static Task<JsonElement> AddBreakpoint(
        MesenDebugClient client,
        [Description("Start address in the selected memory region.")] long address,
        [Description("Breakpoint access type: exec, read, write, or readwrite.")] string type = "exec",
        [Description("Memory region ID. Defaults to SnesMemory.")] string memoryType = "SnesMemory",
        [Description("Optional inclusive end address for a range breakpoint.")] long? endAddress = null,
        [Description("Whether the breakpoint is enabled.")] bool enabled = true,
        [Description("Optional MesenCE breakpoint condition expression.")] string? condition = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["memoryType"] = memoryType,
            ["address"] = address,
            ["enabled"] = enabled
        };
        AddIfSpecified(parameters, "endAddress", endAddress);
        AddIfSpecified(parameters, "condition", condition);
        return client.InvokeAsync("breakpoint.add", parameters, cancellationToken);
    }

    [McpServerTool(Name = "mesen_remove_breakpoint", OpenWorld = false)]
    [Description("Removes a breakpoint created through the external MesenCE debug API.")]
    public static Task<JsonElement> RemoveBreakpoint(
        MesenDebugClient client,
        [Description("Breakpoint ID returned by mesen_add_breakpoint or mesen_list_breakpoints.")] long id,
        CancellationToken cancellationToken) =>
        client.InvokeAsync("breakpoint.remove", new { id }, cancellationToken);

    private static void AddIfSpecified<T>(Dictionary<string, object?> parameters, string name, T? value)
    {
        if (value is not null)
        {
            parameters[name] = value;
        }
    }
}

public sealed record MemoryReadResult(long Address, int Length, string Hex);
