using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pulsive;

internal sealed class PresetNameDialog : Window
{
    internal TextBox NameInput { get; }
    readonly TextBlock error=new() { Foreground=Brushes.Salmon,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    readonly Action<string> save;
    readonly Action? confirmed;
    readonly string confirmationLabel;
    internal PresetNameDialog(Window owner,ResourceDictionary resources,Action<string> save,string? message=null,Action? confirmed=null,string confirmationLabel="Overwrite")
    {
        this.save=save; this.confirmed=confirmed; this.confirmationLabel=confirmationLabel; Owner=owner; Icon=owner.Icon;
        Title="pulsive · Presets"; Width=370; SizeToContent=SizeToContent.Height;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(resources);
        var content=new StackPanel { Margin=new Thickness(20) };
        var header=new TextBlock { Text=confirmed!=null?confirmationLabel.ToUpperInvariant()+" PRESET":message==null?"SAVE AS NEW PRESET":"PRESETS",Foreground=Brushes.White,FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14) };
        content.Children.Add(header);
        NameInput=new TextBox { MaxLength=40,Height=34,Padding=new Thickness(10,0,10,0),VerticalContentAlignment=VerticalAlignment.Center,Foreground=Brushes.White,CaretBrush=Brushes.White,SelectionBrush=new SolidColorBrush(Color.FromRgb(100,106,115)),Background=Brushes.Transparent,BorderThickness=new Thickness(0),FocusVisualStyle=null,ToolTip="Preset name" };
        NameInput.TextChanged+=(_,_)=>error.Text="";
        var template=new ControlTemplate(typeof(TextBox));
        var host=new FrameworkElementFactory(typeof(ScrollViewer)); host.Name="PART_ContentHost"; host.SetValue(FrameworkElement.MarginProperty,new Thickness(10,0,10,0)); host.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); template.VisualTree=host; NameInput.Template=template;
        if(message==null) content.Children.Add(new BackdropSurface { Child=NameInput,CornerRadius=new CornerRadius(14),Height=36,Adaptive=false,TintOpacity=.08,HoverHighlight=false });
        else content.Children.Add(new TextBlock { Text=message,Foreground=Brushes.White,FontSize=12,TextWrapping=TextWrapping.Wrap });
        content.Children.Add(error);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        var cancel=new Button { Content=message==null||confirmed!=null?"Cancel":"Close",Width=78,Height=32,Margin=new Thickness(0,0,8,0),IsCancel=true };
        cancel.Click+=(_,_)=>DialogResult=false; actions.Children.Add(cancel);
        if(message==null||confirmed!=null)
        {
            var confirm=new Button { Content=confirmed!=null?confirmationLabel:"Save",Width=88,Height=32,IsDefault=true };
            confirm.Click+=(_,_)=>Commit(); actions.Children.Add(confirm);
        }
        content.Children.Add(actions);
        var glass=new BackdropSurface { Child=content,CornerRadius=new CornerRadius(20),Adaptive=false,TintOpacity=.85,HoverHighlight=false };
        Content=glass;
        header.MouseLeftButtonDown+=(_,e)=> { if(e.ChangedButton==MouseButton.Left) DragMove(); };
        SourceInitialized+=(_,_)=>WindowFrame.Suppress(new WindowInteropHelper(this).Handle);
        Loaded+=(_,_)=> { if(message==null) NameInput.Focus(); };
    }
    internal void Commit()
    {
        try { if(confirmed!=null) confirmed(); else save(NameInput.Text); DialogResult=true; }
        catch(ArgumentException e) { error.Text=e.Message; NameInput.Focus(); NameInput.SelectAll(); }
        catch(Exception e) when(e is System.IO.IOException or UnauthorizedAccessException) { error.Text=confirmationLabel=="Delete"?"Couldn't delete this preset. Check folder access and try again.":"Couldn't save this preset. Check folder access and try again."; }
    }
}
