using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Pulsive;

public sealed partial class MainWindow
{
    ProgressBar milkdropImportBar=null!;
    TextBlock milkdropImportLabel=null!;
    int importGeneration;
    string importBatch="";
    ProgressBar BuildMilkdropImportBar()
    {
        return new ProgressBar
        {
            Height=5,Margin=new Thickness(0,10,0,0),Visibility=Visibility.Collapsed,
            Minimum=0,Maximum=1,Foreground=AccentColor,
            Template=(ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ProgressBar">
              <Grid x:Name="PART_Track" ClipToBounds="True">
                <Border Background="#30FFFFFF" CornerRadius="3"/>
                <Border x:Name="PART_Indicator" HorizontalAlignment="Left" Background="{TemplateBinding Foreground}" CornerRadius="3"/>
              </Grid>
            </ControlTemplate>
            """)
        };
    }
    IProgress<MilkdropImportProgress> BeginMilkdropImport(string message)
    {
        importingMilkdrop=true; int generation=++importGeneration; importBatch="";
        importMilkdropButton.IsEnabled=selectMilkdropButton.IsEnabled=newMilkdropCollectionButton.IsEnabled=milkdropCollectionList.IsEnabled=milkdropList.IsEnabled=false;
        milkdropStatus.Text=message; milkdropStatus.ToolTip=null;
        milkdropImportLabel.Text=message; milkdropImportLabel.Visibility=Visibility.Visible;
        milkdropImportBar.Value=0; milkdropImportBar.IsIndeterminate=true; milkdropImportBar.Visibility=Visibility.Visible;
        UpdateMilkdropSelection();
        return new Progress<MilkdropImportProgress>(progress=>
        {
            if(closing||!importingMilkdrop||generation!=importGeneration) return;
            milkdropImportBar.IsIndeterminate=progress.Total<=0;
            milkdropImportBar.Maximum=Math.Max(1,progress.Total);
            milkdropImportBar.Value=Math.Clamp(progress.Completed,0,milkdropImportBar.Maximum);
            milkdropStatus.Text=importBatch+progress.Stage+(progress.Total>0?$" · {progress.Completed:N0}/{progress.Total:N0} · {(double)progress.Completed/progress.Total:P0}":$" · {progress.Completed:N0} files found");
            milkdropImportLabel.Text=milkdropStatus.Text;
            milkdropStatus.ToolTip=progress.File;
        });
    }
    void EndMilkdropImport()
    {
        importingMilkdrop=false; importGeneration++;
        milkdropImportBar.IsIndeterminate=false; milkdropImportBar.Visibility=Visibility.Collapsed;
        milkdropImportLabel.Visibility=Visibility.Collapsed;
        importMilkdropButton.IsEnabled=selectMilkdropButton.IsEnabled=newMilkdropCollectionButton.IsEnabled=milkdropCollectionList.IsEnabled=milkdropList.IsEnabled=true;
        UpdateMilkdropSelection();
    }
}
