namespace WarmupJoiner.ViewModels;

/// <summary>One selectable city within a <see cref="RegionGroupViewModel"/>.</summary>
public sealed class CityOption : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public CityOption(string name, Action onChanged)
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
