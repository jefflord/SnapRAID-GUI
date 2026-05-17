namespace SnapRAIDGUI.Views;

using System.Windows;
using System.Windows.Controls;
using SnapRAIDGUI.Models;
using SnapRAIDGUI.ViewModels;

public partial class RecoveryView : UserControl
{
    public RecoveryView()
    {
        InitializeComponent();
    }

    private void TreeItem_Selected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FileTreeNode node })
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

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RecoveryViewModel vm) return;

        var selected = ResultsListView.SelectedItems
            .OfType<FileEntry>()
            .ToList();

        vm.SetSelectedFiles(selected);
    }

    private void ContextMenu_CheckSelected(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryViewModel vm && vm.CheckFileCommand.CanExecute(null))
            vm.CheckFileCommand.Execute(null);
    }

    private void ContextMenu_FixSelected(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryViewModel vm && vm.FixFileCommand.CanExecute(null))
            vm.FixFileCommand.Execute(null);
    }

    private void ContextMenu_ShowInBrowser(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecoveryViewModel vm) return;
        if (ResultsListView.SelectedItem is not FileEntry file) return;

        var node = vm.ShowInBrowser(file);
        if (node != null)
            BringTreeNodeIntoView(node);
    }

    /// Walk the visual tree to find the TreeViewItem for a node and scroll it into view.
    private void BringTreeNodeIntoView(FileTreeNode node)
    {
        // Give the UI a tick to expand, then scroll
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            var tvi = FindTreeViewItem(RecoveryTreeView, node);
            tvi?.BringIntoView();
        });
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent == null) return null;
        foreach (var child in parent.Items)
        {
            var tvi = parent.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
            if (tvi == null) continue;
            if (tvi.DataContext == item) return tvi;
            var found = FindTreeViewItem(tvi, item);
            if (found != null) return found;
        }
        return null;
    }
}
