namespace SnapRAIDGUI.Views;

using System.Windows.Controls;
using SnapRAIDGUI.Models;
using SnapRAIDGUI.ViewModels;

public partial class RecoveryView : UserControl
{
    public RecoveryView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// When a tree item is selected:
    /// - File node  → set SelectedFile so Check/Fix buttons activate.
    /// - Folder node → populate the results panel with the folder's direct files.
    /// </summary>
    private void TreeItem_Selected(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FileTreeNode node } tvi)
        {
            if (DataContext is RecoveryViewModel vm)
            {
                if (node.IsDirectory)
                    vm.SelectFolder(node);
                else if (node.FileEntry != null)
                    vm.SelectedFile = node.FileEntry;
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// Sync the ListView's multi-selection to the ViewModel so Fix/Check can act on all selected files.
    /// </summary>
    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RecoveryViewModel vm) return;

        var selected = ResultsListView.SelectedItems
            .OfType<FileEntry>()
            .ToList();

        vm.SetSelectedFiles(selected);
    }
}
