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
    /// When a tree item is selected, if it is a file node, set it as the selected file
    /// in the view model so Check/Fix buttons activate.
    /// </summary>
    private void TreeItem_Selected(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FileTreeNode node } && !node.IsDirectory)
        {
            if (DataContext is RecoveryViewModel vm && node.FileEntry != null)
                vm.SelectedFile = node.FileEntry;
        }
        e.Handled = true; // prevent bubbling
    }
}
