using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitPort.Models;

public class RepoTabItem : INotifyPropertyChanged
{
    private string _currentBranch = "";
    private bool _isActive;

    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsCatalogTab { get; set; }

    public string CurrentBranch
    {
        get => _currentBranch;
        set
        {
            if (_currentBranch != value)
            {
                _currentBranch = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
