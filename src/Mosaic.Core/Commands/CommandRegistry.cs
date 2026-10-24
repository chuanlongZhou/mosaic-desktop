namespace Mosaic.Core.Commands;

public sealed record CommandRequest(string Id, string? Target = null);

public sealed class CommandRegistry
{
    private readonly Dictionary<string, Func<string?, Task>> _commands = new(StringComparer.Ordinal);
    public void Register(string id, Func<string?, Task> execute) => _commands.Add(id, execute);
    public Task ExecuteAsync(CommandRequest request) => _commands.TryGetValue(request.Id, out var execute)
        ? execute(request.Target)
        : Task.FromException(new InvalidOperationException($"当前版本不支持命令：{request.Id}"));
}
