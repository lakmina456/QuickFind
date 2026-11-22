namespace QuickFind.ViewModels;

public sealed class CommandItem
{
    public string Title { get; init; } = "";
    public string Hint { get; init; } = "";
    public Action Run { get; init; } = () => { };
}
