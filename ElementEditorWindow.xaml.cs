using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace VideoTimeStudy;

public class ElementItem : INotifyPropertyChanged
{
    private string _name = "";

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public partial class ElementEditorWindow : Window
{
    public ObservableCollection<ElementItem> Elements { get; set; }
    private static List<string> defaultElements = new List<string>
    {
        "Reach", "Grasp", "Move", "Position", "Release",
        "Inspect", "Assemble", "Disassemble",
        "Use", "Wait", "Search", "Select", "Plan", "Hold"
    };

    public ElementEditorWindow(List<string> currentElements)
    {
        InitializeComponent();
        
        Elements = new ObservableCollection<ElementItem>();
        foreach (var element in currentElements)
        {
            if (!string.IsNullOrWhiteSpace(element))
            {
                Elements.Add(new ElementItem { Name = element });
            }
        }
        
        ElementsGrid.ItemsSource = Elements;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will reset the element library to default values. Continue?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Elements.Clear();
            foreach (var element in defaultElements)
            {
                Elements.Add(new ElementItem { Name = element });
            }
        }
    }

    public List<string> GetElements()
    {
        return Elements
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => e.Name.Trim())
            .Distinct()
            .ToList();
    }

    public static List<string> GetDefaultElements()
    {
        return new List<string>(defaultElements);
    }
}
