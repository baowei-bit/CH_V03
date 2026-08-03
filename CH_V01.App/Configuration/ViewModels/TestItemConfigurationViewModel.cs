using System.Collections.ObjectModel;
using System.Windows.Input;
using CH_V01.App.Commands;
using CH_V01.Core.Configuration;

namespace CH_V01.App.ViewModels;

public sealed class TestItemConfigurationViewModel : DialogViewModelBase
{
    private readonly TestItemConfigurationSet _configuration;
    private TestItemConfiguration? _selectedItem;

    public TestItemConfigurationViewModel(TestItemConfigurationSet configuration)
    {
        _configuration = configuration;
        Items = new ObservableCollection<TestItemConfiguration>(
            configuration.Items
                .OrderBy(item => item.Order)
                .Select(Clone));

        AddCommand = new RelayCommand(AddItem);
        DeleteCommand = new RelayCommand(DeleteSelectedItem);
        MoveUpCommand = new RelayCommand(MoveSelectedItemUp);
        MoveDownCommand = new RelayCommand(MoveSelectedItemDown);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public ObservableCollection<TestItemConfiguration> Items { get; }

    public TestItemConfiguration? SelectedItem
    {
        get => _selectedItem;
        set => SetField(ref _selectedItem, value);
    }

    public ICommand AddCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private void AddItem()
    {
        var item = new TestItemConfiguration
        {
            Order = Items.Count + 1,
            ItemName = $"TestItem{Items.Count + 1}"
        };
        Items.Add(item);
        SelectedItem = item;
    }

    private void DeleteSelectedItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var index = Items.IndexOf(SelectedItem);
        Items.Remove(SelectedItem);
        RenumberItems();
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    private void MoveSelectedItemUp()
    {
        MoveSelectedItem(-1);
    }

    private void MoveSelectedItemDown()
    {
        MoveSelectedItem(1);
    }

    private void MoveSelectedItem(int offset)
    {
        if (SelectedItem is null)
        {
            return;
        }

        var oldIndex = Items.IndexOf(SelectedItem);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Items.Count)
        {
            return;
        }

        Items.Move(oldIndex, newIndex);
        RenumberItems();
    }

    private void Save()
    {
        var orderedItems = Items.OrderBy(item => item.Order).ThenBy(item => Items.IndexOf(item)).ToList();
        for (var index = 0; index < orderedItems.Count; index++)
        {
            orderedItems[index].Order = index + 1;
        }

        _configuration.Items = orderedItems.Select(Clone).ToList();
        Close(true);
    }

    private void RenumberItems()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Order = index + 1;
        }
    }

    private static TestItemConfiguration Clone(TestItemConfiguration item)
    {
        return new TestItemConfiguration
        {
            Order = item.Order,
            ItemName = item.ItemName,
            UpperLimit = item.UpperLimit,
            LowerLimit = item.LowerLimit,
            Unit = item.Unit,
            PointA = item.PointA,
            PointB = item.PointB
        };
    }
}
