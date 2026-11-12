namespace WarmupJoiner.ViewModels;

public sealed class RegionOption : ViewModelBase
{
    private bool _isSelected;

    public RegionOption(string name) => Name = name;

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
