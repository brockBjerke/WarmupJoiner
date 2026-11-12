namespace WarmupJoiner.ViewModels;

/// <summary>One selectable game mode (e.g. "MultiCFG", "Rifles only", "AIM-DM").</summary>
public sealed class ModeOption : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public ModeOption(string name, Action onChanged)
    {
        Name = name;
        _onChanged = onChanged;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                _onChanged();
            }
        }
    }
}
