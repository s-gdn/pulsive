using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pulsive;

public sealed partial class MainWindow
{
    Window settingsWindow=null!;
    static readonly string[] BackgroundTypes=["None","Solid color","Blur"];
    ComboBox settingsBackgroundSelect=null!,backgroundBlurMenuSelect=null!;
    FrameworkElement settingsBlurGroup=null!;
    GlassColorPicker backgroundColorPicker=null!,gradientStartPicker=null!,gradientEndPicker=null!;
    readonly List<GlassColorPicker> colorPickers=[];
    CheckBox settingsTopmostToggle=null!;
    Slider settingsBlurSlider=null!;
    ComboBox borderSelect=null!,borderMenuSelect=null!;
    GlassColorPicker borderColorPicker=null!;
    string? parsedBorderColor;
    Color borderTint=Color.FromRgb(104,239,222);

    void InitializeSettingsWindow(StackPanel appearance,StackPanel other,ComboBox palette,Grid heading,ContextMenu menu,Grid visualOptions,StackPanel feather)
    {
        settingsWindow=new Window
        {
            Title="pulsive · Settings",Icon=this.Icon,Width=790,Height=620,MinWidth=720,MinHeight=360,
            WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
            ResizeMode=ResizeMode.CanResizeWithGrip,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            FontFamily=FontFamily,Foreground=TextColor,Content=panel
        };
        settingsWindow.Resources.MergedDictionaries.Add(Resources);
        var controls=panel.Child; panel.Child=null;
        panel.Child=new ScrollViewer { Content=controls,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        heading.Background=Brushes.Transparent;
        heading.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource is TextBlock||ReferenceEquals(e.OriginalSource,heading)) settingsWindow.DragMove(); };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var close=Button("×","Close settings · Esc",(_,_)=>HideSettings());
        close.Margin=new Thickness(12,0,0,0); Grid.SetColumn(close,2); heading.Children.Add(close);
        settingsWindow.Closing+=(_,e)=> { if(!closing) { e.Cancel=true; HideSettings(); } };
        settingsWindow.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { var open=colorPickers.FirstOrDefault(p=>p.IsPickerOpen); if(open!=null) open.ClosePicker(); else HideSettings(); e.Handled=true; } };
        settingsWindow.LocationChanged+=(_,_)=>backdrops.RefreshNative();
        settingsWindow.SizeChanged+=(_,_)=>backdrops.RefreshNative();
        settingsWindow.SourceInitialized+=(_,_)=>WindowFrame.Suppress(new WindowInteropHelper(settingsWindow).Handle);

        settingsTopmostToggle=new CheckBox { Content="Always on top",ToolTip="Keep pulsive above other windows · T",IsChecked=Topmost,Foreground=TextColor,FontSize=12,Margin=new Thickness(0,14,0,0) };
        settingsTopmostToggle.Click+=(_,_)=> { Topmost=settingsTopmostToggle.IsChecked==true; Save(); };
        other.Children.Add(settingsTopmostToggle);
        var customOptions=new Grid();
        customOptions.ColumnDefinitions.Add(new ColumnDefinition()); customOptions.ColumnDefinitions.Add(new ColumnDefinition());
        gradientStartPicker=Picker(visual.CustomStart,"GRADIENT START"); gradientEndPicker=Picker(visual.CustomEnd,"GRADIENT END");
        gradientStartPicker.ToolTip="Choose the first colour to use a custom gradient"; gradientEndPicker.ToolTip="Choose the second colour to use a custom gradient";
        void ApplyGradient()
        {
            if(applyingPreset) return;
            visual.CustomStart=gradientStartPicker.SelectedColor; visual.CustomEnd=gradientEndPicker.SelectedColor;
            prefs.CustomColorStart=Hex(visual.CustomStart); prefs.CustomColorEnd=Hex(visual.CustomEnd);
            palette.SelectedIndex=4; visual.InvalidateVisual(); UpdateBorder(); Save();
        }
        gradientStartPicker.ColorChanged+=(_,_)=>ApplyGradient(); gradientEndPicker.ColorChanged+=(_,_)=>ApplyGradient();
        customOptions.Children.Add(Labeled("CUSTOM GRADIENT · START",gradientStartPicker,0)); customOptions.Children.Add(Labeled("CUSTOM GRADIENT · END",gradientEndPicker,1));
        appearance.Children.Insert(1,customOptions);
        var backdropOptions=new Grid { Margin=new Thickness(0,0,0,10) };
        backdropOptions.ColumnDefinitions.Add(new ColumnDefinition()); backdropOptions.ColumnDefinitions.Add(new ColumnDefinition());
        settingsBackgroundSelect=Select(BackgroundTypes,prefs.BackgroundType,double.NaN); settingsBackgroundSelect.HorizontalAlignment=HorizontalAlignment.Stretch;
        settingsBackgroundSelect.SelectionChanged+=(_,_)=>SetBackgroundType(settingsBackgroundSelect.SelectedIndex);
        backdropOptions.Children.Add(Labeled("TYPE",settingsBackgroundSelect,0));
        settingsBlurSlider=Slider(0,48,prefs.BackgroundBlurAmount,v=>backgroundBlurSlider.Value=v);
        var label=(StackPanel)Labeled($"BLUR STRENGTH · {prefs.BackgroundBlurAmount/48:P0}",settingsBlurSlider,1);
        settingsBlurGroup=label;
        settingsBlurSlider.ValueChanged+=(_,e)=> { ((TextBlock)label.Children[0]).Text=$"BLUR STRENGTH · {e.NewValue/48:P0}"; settingsBlurSlider.ToolTip=$"Strength: {e.NewValue/48:P0}"; };
        backgroundBlurSlider.ValueChanged+=(_,e)=>settingsBlurSlider.Value=e.NewValue;
        backdropOptions.Children.Add(label); appearance.Children.Add(backdropOptions);
        backgroundColorPicker=Picker(ParseColor(prefs.BackgroundColor,Color.FromRgb(16,23,37)),"BACKGROUND COLOUR");
        backgroundColorPicker.ColorChanged+=(_,_)=> { prefs.BackgroundColor=Hex(backgroundColorPicker.SelectedColor); solidBackdrop.Background=new SolidColorBrush(backgroundColorPicker.SelectedColor); backdrops.RefreshContrast(); Save(); };
        var solidOptions=new Grid();
        solidOptions.ColumnDefinitions.Add(new ColumnDefinition());
        solidOptions.Children.Add(Labeled("SOLID BACKGROUND COLOUR",backgroundColorPicker,0)); appearance.Children.Add(solidOptions);

        var borderOptions=new Grid();
        borderOptions.ColumnDefinitions.Add(new ColumnDefinition()); borderOptions.ColumnDefinitions.Add(new ColumnDefinition());
        borderSelect=Select(OverlayBorder.Styles,Math.Clamp(prefs.BorderType,0,4),double.NaN);
        borderSelect.HorizontalAlignment=HorizontalAlignment.Stretch;
        borderSelect.SelectionChanged+=(_,_)=>SetBorderType(borderSelect.SelectedIndex);
        borderOptions.Children.Add(Labeled("TYPE",borderSelect,0));
        borderColorPicker=Picker(borderTint,"BORDER COLOUR");
        borderColorPicker.ColorChanged+=(_,_)=>
        {
            var color=borderColorPicker.SelectedColor;
            prefs.BorderColor=$"#{color.R:X2}{color.G:X2}{color.B:X2}";
            UpdateBorder(); Save();
        };
        borderOptions.Children.Add(Labeled("COLOUR",borderColorPicker,1)); appearance.Children.Add(borderOptions);

        borderMenuSelect=Select(OverlayBorder.Styles,Math.Clamp(prefs.BorderType,0,4),218);
        borderMenuSelect.SelectionChanged+=(_,_)=>SetBorderType(borderMenuSelect.SelectedIndex);
        var borderMenu=new StackPanel { Width=218 };
        borderMenu.Children.Add(new TextBlock { Text="Border type",FontSize=11,Margin=new Thickness(0,0,0,4) }); borderMenu.Children.Add(borderMenuSelect);
        menu.Items.Add(new MenuItem { Header=borderMenu,StaysOpenOnClick=true,Focusable=false,Padding=new Thickness(10,4,10,4) });
        var settingsItem=new MenuItem { Header="Settings…" }; settingsItem.Click+=(_,_)=>ShowSettings(); menu.Items.Add(settingsItem);
        ArrangeAppearanceSections(appearance,visualOptions,customOptions,backdropOptions,solidOptions,borderOptions,feather);
    }
    void ArrangeAppearanceSections(StackPanel appearance,UIElement visualOptions,UIElement customOptions,UIElement backgroundOptions,UIElement solidOptions,UIElement borderOptions,StackPanel feather)
    {
        appearance.Children.Clear();
        Border Section(string title,params UIElement[] children)
        {
            var content=new StackPanel();
            content.Children.Add(new TextBlock { Text=title,Foreground=AccentColor,FontSize=10,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
            foreach(var child in children) content.Children.Add(child);
            return new Border { Child=content,Padding=new Thickness(10),CornerRadius=new CornerRadius(16),Background=new SolidColorBrush(Color.FromArgb(5,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(22,255,255,255)),BorderThickness=new Thickness(1) };
        }
        appearance.Children.Add(Section("VISUALISER",visualOptions,customOptions));
        var middle=new Grid { Margin=new Thickness(0,10,0,10) };
        middle.ColumnDefinitions.Add(new ColumnDefinition()); middle.ColumnDefinitions.Add(new ColumnDefinition());
        var background=Section("BACKGROUND",backgroundOptions,solidOptions); background.Margin=new Thickness(0,0,5,0);
        var border=Section("BORDER",borderOptions); border.Margin=new Thickness(5,0,0,0); Grid.SetColumn(border,1);
        middle.Children.Add(background); middle.Children.Add(border); appearance.Children.Add(middle);
        appearance.Children.Add(Section("FEATHER",feather));
    }
    void ShowSettings()
    {
        if(closing) return;
        settingsOpen=true; panel.Visibility=Visibility.Visible;
        if(settingsWindow.Owner==null) settingsWindow.Owner=this;
        settingsWindow.Show(); settingsWindow.Activate(); settingsWindow.UpdateLayout(); backdrops.RefreshNative();
    }
    void HideSettings()
    {
        foreach(var picker in colorPickers) picker.ClosePicker();
        foreach(var selector in selectors) if(Window.GetWindow(selector)==settingsWindow) selector.IsDropDownOpen=false;
        settingsOpen=false; settingsWindow.Hide(); panel.Visibility=Visibility.Collapsed; backdrops.RefreshNative();
    }
    void SetBorderType(int type)
    {
        prefs.BorderType=Math.Clamp(type,0,4);
        if(borderSelect.SelectedIndex!=prefs.BorderType) borderSelect.SelectedIndex=prefs.BorderType;
        if(borderMenuSelect.SelectedIndex!=prefs.BorderType) borderMenuSelect.SelectedIndex=prefs.BorderType;
        UpdateBorder(); Save();
    }
    void UpdateBorder()
    {
        if(parsedBorderColor!=prefs.BorderColor)
        {
            parsedBorderColor=prefs.BorderColor;
            if(!TryBorderColor(prefs.BorderColor,out borderTint)) borderTint=Color.FromRgb(104,239,222);
        }
        overlayBorder.SetAppearance(prefs.BorderType,borderTint,visual.Palette,visual.CustomStart,visual.CustomEnd);
    }
    GlassColorPicker Picker(Color initial,string title) { var picker=new GlassColorPicker(initial,title); colorPickers.Add(picker); return picker; }
    static string Hex(Color color)=>$"#{color.R:X2}{color.G:X2}{color.B:X2}";
    static Color ParseColor(string text,Color fallback)=>TryBorderColor(text,out var color)?color:fallback;
    void SetBackgroundType(int type)
    {
        type=Math.Clamp(type,0,2); prefs.BackgroundType=type; prefs.BackgroundBlur=type==2;
        if(backgroundBlurMenuSelect.SelectedIndex!=type) backgroundBlurMenuSelect.SelectedIndex=type;
        if(settingsBackgroundSelect!=null&&settingsBackgroundSelect.SelectedIndex!=type) settingsBackgroundSelect.SelectedIndex=type;
        visualBackdrop.Visibility=type==2?Visibility.Visible:Visibility.Collapsed;
        solidBackdrop.Visibility=type==1?Visibility.Visible:Visibility.Collapsed;
        visualBackdrop.BlurRadius=prefs.BackgroundBlurAmount;
        backgroundBlurSlider.IsEnabled=type==2;
        if(backgroundBlurSlider.Parent is UIElement menuGroup) menuGroup.Opacity=type==2?1:.35;
        if(settingsBlurSlider!=null) { settingsBlurSlider.IsEnabled=type==2; settingsBlurGroup.Opacity=type==2?1:.35; }
        if(backgroundColorPicker!=null) { backgroundColorPicker.IsEnabled=type==1; backgroundColorPicker.Opacity=type==1?1:.35; }
        backdrops.RefreshContrast();
        if(IsLoaded)
        {
            var hwnd=new WindowInteropHelper(this).Handle;
            if(hwnd!=IntPtr.Zero) ToolbarMenus.UpdateSystemMenu(hwnd,prefs.ToolbarLocked,prefs.BackgroundBlur);
            ToolbarMenus.UpdateJumpList(prefs.ToolbarLocked,prefs.BackgroundBlur); Save(); backdrops.RefreshNative();
        }
    }
    static bool TryBorderColor(string text,out Color color)
    {
        color=default;
        if(text is null||text.Length!=7||text[0]!='#') return false;
        try { color=(Color)ColorConverter.ConvertFromString(text); return true; } catch { return false; }
    }
}
