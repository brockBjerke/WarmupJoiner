using System.Collections.ObjectModel;

namespace WarmupJoiner.ViewModels;

/// <summary>
/// A region (e.g. "North America") with its cities as checkable sub-options.
/// Checking the region selects/deselects all of its cities; checking individual
/// cities updates the region checkbox to a partial (indeterminate) state.
/// </summary>
public sealed class RegionGroupViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool? _isSelected = false;
    private bool _suppressCascade;

    public RegionGroupViewModel(string name, Action onChanged)
    {
        Name = name;
        _onChanged = onChanged;
    }

    public string Name { get; }

    public ObservableCollection<CityOption> Cities { get; } = new();

    public bool? IsSelected
    {
        get => _isSelected;
        set
        {
            // A user can cycle a three-state checkbox into "indeterminate"
            // themselves - that's not a real selection, so ignore it.
            if (value is null || !SetField(ref _isSelected, value))
            {
                return;
            }

            _suppressCascade = true;
            foreach (var city in Cities)
            {
                city.IsSelected = value.Value;
            }
            _suppressCascade = false;

            _onChanged();
        }
    }

    public void AddCity(string cityName, bool isSelected)
    {
        var city = new CityOption(cityName, OnCityChanged);
        Cities.Add(city);
        if (isSelected)
        {
            city.IsSelected = true;
        }
    }

    private void OnCityChanged()
    {
        RecomputeFromChildren();
        _onChanged();
    }

    private void RecomputeFromChildren()
    {
        if (_suppressCascade || Cities.Count == 0)
        {
            return;
        }

        var selectedCount = Cities.Count(c => c.IsSelected);
        bool? newState = selectedCount == 0 ? false : selectedCount == Cities.Count ? true : null;
        SetField(ref _isSelected, newState, nameof(IsSelected));
    }
}
