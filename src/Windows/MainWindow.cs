using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Data;

namespace Pulsive;

public sealed partial class MainWindow : Window
{
    readonly AudioEngine audio = new();
    readonly Visualizer visual;
    readonly Grid root = new();
    readonly Border toolbar;
    readonly Border panel;
    readonly TextBlock deviceLabel;
    readonly ComboBox modeSelect;
    readonly ComboBox paletteSelect;
    readonly Slider sensitivitySlider,brightnessSlider;
    readonly ComboBox deviceSelect;
    readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly Preferences prefs;
    readonly List<Thumb> grips = [];
    readonly List<ComboBox> selectors = [];
    readonly List<Slider> featherSliders = [];
    readonly List<Slider> featherBoundsSliders = [];
    readonly ComboBox featherControlSelect;
    readonly CheckBox startupToggle;
    readonly CheckBox linkFeatherToggle;
    readonly Button edgeSettingsTab;
    readonly MenuItem toolbarLockMenuItem;
    readonly MenuItem backgroundBlurMenuItem;
    readonly Slider backgroundBlurSlider;
    readonly Slider zoomSlider;
    readonly BackdropSurface visualBackdrop;
    readonly Border solidBackdrop;
    readonly Button appearanceSettingsTab;
    readonly BackdropService backdrops;
    readonly OverlayBorder overlayBorder=new();
    OuterGlowWindow? outerGlow;
    WindowAreaContextMenu? areaContextMenu;
    bool updatingFeather;
    readonly Button settingsButton;
    readonly Button previewButton;
    readonly TextBlock welcome;
    bool hovered, settingsOpen, paused, closing;
    double lastTime, lastDeviceCheck;
    long lastToolbarPress;
    Point lastToolbarPoint;
    bool toolbarDragging;
    Point toolbarDragStart,toolbarDragOrigin;
    const int HotkeyId = 0x5052;
    static readonly string SettingsPath = App.SettingsFile;
    static readonly Brush TextColor = new SolidColorBrush(Color.FromRgb(235,237,240));
    static readonly Brush MutedColor = new SolidColorBrush(Color.FromRgb(158,164,173));
    static readonly Brush AccentColor = new SolidColorBrush(Color.FromRgb(220,223,228));
    public MainWindow(bool preview)
    {
        prefs = App.IsUiTest ? Preferences.CreateDefault() : Load();
        prefs.BackgroundBlurAmount=Math.Clamp(prefs.BackgroundBlurAmount,0,48);
        prefs.BackgroundType=prefs.BackgroundType<0?(prefs.BackgroundBlur?2:0):Math.Clamp(prefs.BackgroundType,0,2);
        prefs.BackgroundBlur=prefs.BackgroundType==2;
        Title = ToolbarMenus.WindowTitle;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/assets/pulsive.ico",UriKind.Absolute));
        Width = Math.Clamp(prefs.Width,720,2400); Height = Math.Clamp(prefs.Height,340,1600);
        MinWidth = 720; MinHeight = 340; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        // Custom grips resize the overlay; a native sizing frame would draw an outline.
        ResizeMode = ResizeMode.NoResize; Topmost = prefs.Topmost;
        FontFamily = new FontFamily("Segoe UI"); Foreground = TextColor;
        Content = root;
        backdrops=new BackdropService(this);
        backdrops.ContrastBackground=surface=>prefs.BackgroundType==1&&Window.GetWindow(surface)==this?ParseColor(prefs.BackgroundColor,Color.FromRgb(16,23,37)):null;
        AddStyles();
        visual = new Visualizer(audio) { Mode = Math.Clamp(prefs.Mode,0,7), Palette = Math.Clamp(prefs.Palette,0,4), Intensity = Math.Clamp(prefs.Intensity,.1,1),Zoom=Math.Clamp(prefs.Zoom,.25,4),CustomStart=ParseColor(prefs.CustomColorStart,Color.FromRgb(104,239,222)),CustomEnd=ParseColor(prefs.CustomColorEnd,Color.FromRgb(193,122,255)) };
        audio.Gain = (float)Math.Clamp(prefs.Gain,.25,5); audio.Demo = preview;
        visualBackdrop=new BackdropSurface { Adaptive=false,TintOpacity=.025,Outline=false,RequireFrame=true,CornerRadius=new CornerRadius(24),BlurRadius=Math.Clamp(prefs.BackgroundBlurAmount,0,48),IsHitTestVisible=false,Visibility=prefs.BackgroundBlur?Visibility.Visible:Visibility.Collapsed };
        solidBackdrop=new Border { CornerRadius=new CornerRadius(24),Background=new SolidColorBrush(ParseColor(prefs.BackgroundColor,Color.FromRgb(16,23,37))),IsHitTestVisible=false,Visibility=prefs.BackgroundType==1?Visibility.Visible:Visibility.Collapsed };
        root.Children.Add(solidBackdrop); root.Children.Add(visualBackdrop); root.Children.Add(visual);
        root.Children.Add(overlayBorder);
        UpdateBorder();
        UpdateFeather();

        toolbar = new Border { Margin=new Thickness(8), Background=Brushes.Transparent };
        toolbar.VerticalAlignment = VerticalAlignment.Top;
        toolbar.Height = 34;
        var bar = new Grid();
        foreach(var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1,GridUnitType.Star), GridLength.Auto }) bar.ColumnDefinitions.Add(new ColumnDefinition { Width=width });
        toolbar.Child=bar;
        var brand = new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
        var wordmark = new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        wordmark.Children.Add(new TextBlock { Text="pulsive", FontSize=13, FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center });
        wordmark.Children.Add(new TextBlock { Text="v1.0.0", FontSize=8,TextAlignment=TextAlignment.Center });
        brand.Children.Add(wordmark); bar.Children.Add(new BackdropSurface { Child=brand,Padding=new Thickness(12,2,12,2),Height=34,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),CornerRadius=new CornerRadius(17) });
        brand.MouseLeftButtonDown += Drag;
        modeSelect = Select(Visualizer.Modes,visual.Mode,118);
        modeSelect.Height=34;
        modeSelect.ToolTip="Visual style · ← / → to switch";
        modeSelect.SelectionChanged += (_,_) => { visual.Mode=modeSelect.SelectedIndex; Save(); };
        Grid.SetColumn(modeSelect,1); bar.Children.Add(modeSelect);
        var actions = new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
        Grid.SetColumn(actions,3); bar.Children.Add(actions);
        settingsButton=Button("⚙", "Settings · S", (_,_)=>ToggleSettings()); actions.Children.Add(settingsButton);
        actions.Children.Add(Button("─", "Minimise", (_,_)=>WindowState=WindowState.Minimized));
        actions.Children.Add(Button("□", "Maximise / restore", (_,_)=>ToggleMaximize()));
        actions.Children.Add(Button("×", "Close · Esc", (_,_)=>Close(),true));
        foreach(var action in actions.Children.OfType<Button>()) { action.Width=34; action.Height=34; action.Margin=new Thickness(4,0,0,0); }
        toolbar.MouseLeftButtonDown+=Drag;
        root.Children.Add(toolbar);

        panel=Glass(new Thickness(0));
        panel.Visibility=Visibility.Collapsed;
        var controls=new StackPanel { Margin=new Thickness(22,17,22,17) }; panel.Child=controls;
        var heading=new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        heading.Children.Add(new TextBlock { Text="MAKE IT YOURS", FontSize=10, FontWeight=FontWeights.SemiBold, Foreground=AccentColor, VerticalAlignment=VerticalAlignment.Center });
        var hint=new TextBlock { Text="← →  style     Space  pause     Ctrl + Alt + P  show / hide", FontSize=10, Foreground=MutedColor, VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(hint,1); heading.Children.Add(hint); controls.Children.Add(heading);
        var tabs=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,10,0,14) };
        var appearanceTab=Button("Appearance", "Colour, brightness and audio", (_,_)=>{}); appearanceTab.Width=108; appearanceTab.FontSize=12;
        appearanceSettingsTab=appearanceTab;
        var edgeTab=Button("Other", "System audio, window and startup", (_,_)=>{}); edgeTab.Width=100; edgeTab.FontSize=12;
        var appearanceTitle=new TextBlock { Text="Appearance" }; appearanceTab.Content=appearanceTitle;
        var otherTitle=new TextBlock { Text="Other" }; edgeTab.Content=otherTitle;
        edgeSettingsTab=edgeTab;
        tabs.Children.Add(appearanceTab); tabs.Children.Add(edgeTab); controls.Children.Add(tabs);
        var appearance=new StackPanel();
        var edgeSettings=new StackPanel { Visibility=Visibility.Collapsed };
        void SelectSettings(bool edges)
        {
            appearance.Visibility=edges?Visibility.Collapsed:Visibility.Visible;
            edgeSettings.Visibility=edges?Visibility.Visible:Visibility.Collapsed;
            appearanceTab.Background=edges?Brushes.Transparent:AccentColor;
            edgeTab.Background=edges?AccentColor:Brushes.Transparent;
            appearanceTab.Foreground=edges?MutedColor:new SolidColorBrush(Color.FromRgb(28,30,34));
            edgeTab.Foreground=edges?new SolidColorBrush(Color.FromRgb(28,30,34)):MutedColor;
            appearanceTab.FontWeight=edges?FontWeights.Normal:FontWeights.SemiBold; edgeTab.FontWeight=edges?FontWeights.SemiBold:FontWeights.Normal;
            appearanceTab.Tag=edges?null:"ActivePage"; edgeTab.Tag=edges?"ActivePage":null;
            appearanceTitle.Foreground=edges?MutedColor:new SolidColorBrush(Color.FromRgb(28,30,34));
            otherTitle.Foreground=edges?new SolidColorBrush(Color.FromRgb(28,30,34)):MutedColor;
        }
        appearanceTab.Click+=(_,_)=>SelectSettings(false); edgeTab.Click+=(_,_)=>SelectSettings(true); SelectSettings(false);
        controls.Children.Add(appearance); controls.Children.Add(edgeSettings);
        var options=new Grid { Margin=new Thickness(0,0,0,10) };
        for(int i=0;i<4;i++) options.ColumnDefinitions.Add(new ColumnDefinition());
        paletteSelect=Select(Visualizer.Palettes,visual.Palette,double.NaN); var palette=paletteSelect; palette.HorizontalAlignment=HorizontalAlignment.Stretch;
        palette.Tag="Up";
        palette.SelectionChanged+=(_,_)=> { visual.Palette=palette.SelectedIndex; Save(); };
        options.Children.Add(Labeled("COLOUR",palette,0));
        sensitivitySlider=Slider(.25,5,audio.Gain,v=>audio.Gain=(float)v);
        brightnessSlider=Slider(.1,1,visual.Intensity,v=>visual.Intensity=v);
        options.Children.Add(Labeled("SENSITIVITY",sensitivitySlider,1));
        options.Children.Add(Labeled("BRIGHTNESS",brightnessSlider,2));
        zoomSlider=Slider(.25,4,visual.Zoom,v=> { visual.Zoom=v; visual.InvalidateVisual(); });
        zoomSlider.ToolTip="Visual scale: 25–400%; 100% is the original size";
        var zoomSettings=(StackPanel)Labeled($"ZOOM · {visual.Zoom:P0}",zoomSlider,3);
        zoomSlider.ValueChanged+=(_,e)=> { ((TextBlock)zoomSettings.Children[0]).Text=$"ZOOM · {e.NewValue:P0}"; zoomSlider.ToolTip=$"Visual scale: {e.NewValue:P0}"; };
        options.Children.Add(zoomSettings);
        appearance.Children.Add(options);
        var bottom=new Grid(); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var devices=new StackPanel();
        deviceLabel=new TextBlock { Text="SYSTEM AUDIO · DEFAULT OUTPUT", FontSize=9, Foreground=MutedColor, Margin=new Thickness(0,0,0,5) }; devices.Children.Add(deviceLabel);
        deviceSelect=Select(["Default output (follows Windows)"],0,double.NaN); deviceSelect.HorizontalAlignment=HorizontalAlignment.Stretch; devices.Children.Add(deviceSelect);
        deviceSelect.Tag="Up";
        deviceSelect.DropDownOpened+=(_,_)=>RefreshDevices();
        deviceSelect.SelectionChanged+=(_,_)=> { if(deviceSelect.SelectedItem is ComboBoxItem item) { prefs.Device=item.Tag as string; audio.Start(prefs.Device); Save(); } };
        bottom.Children.Add(devices);
        previewButton=Button("◇  Preview", "Toggle generated preview audio · D", (_,_)=>ToggleDemo()); previewButton.Width=110; previewButton.Height=deviceSelect.Height; previewButton.VerticalAlignment=VerticalAlignment.Bottom; previewButton.Margin=new Thickness(16,0,0,0); Grid.SetColumn(previewButton,1); bottom.Children.Add(previewButton);
        edgeSettings.Children.Add(bottom);
        var featherControls=BuildFeatherControls();
        linkFeatherToggle=featherControls.Link; featherControlSelect=featherControls.Mode;
        appearance.Children.Add(featherControls.Root);
        startupToggle=new CheckBox { Content="Launch pulsive when I sign in to Windows", Foreground=TextColor, FontSize=12, Margin=new Thickness(0,16,0,0), ToolTip="Starts this copy of pulsive for your Windows account. Keep the executable in its current location." };
        try { startupToggle.IsChecked=StartupRegistration.IsEnabled(); } catch { startupToggle.IsEnabled=false; startupToggle.ToolTip="Windows startup settings are unavailable for this account."; }
        startupToggle.Click+=(_,_)=>
        {
            try { StartupRegistration.SetEnabled(startupToggle.IsChecked==true); }
            catch(Exception ex) { startupToggle.IsChecked=!(startupToggle.IsChecked==true); MessageBox.Show(this,ex.Message,"Could not change startup setting",MessageBoxButton.OK,MessageBoxImage.Information); }
        };
        edgeSettings.Children.Add(startupToggle);
        welcome=new TextBlock { Text="Play audio in any app  ·  Hover for controls", Foreground=MutedColor, FontSize=12, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Bottom, Margin=new Thickness(0,0,0,30), IsHitTestVisible=false };
        root.Children.Add(welcome);
        AddGrips();
        toolbar.Opacity=0; toolbar.IsHitTestVisible=false;
        var windowMenu=new ContextMenu();
        toolbarLockMenuItem=new MenuItem { Header=ToolbarMenus.Label(prefs.ToolbarLocked) };
        toolbarLockMenuItem.Click+=(_,_)=>SetToolbarLocked(!prefs.ToolbarLocked);
        windowMenu.Items.Add(toolbarLockMenuItem);
        backgroundBlurMenuSelect=Select(BackgroundTypes,prefs.BackgroundType,218);
        backgroundBlurMenuSelect.SelectionChanged+=(_,_)=>SetBackgroundType(backgroundBlurMenuSelect.SelectedIndex);
        var backgroundMenu=new StackPanel { Width=218 };
        backgroundMenu.Children.Add(new TextBlock { Text="Visualiser background",FontSize=11,Margin=new Thickness(0,0,0,4) }); backgroundMenu.Children.Add(backgroundBlurMenuSelect);
        backgroundBlurMenuItem=new MenuItem { Header=backgroundMenu,StaysOpenOnClick=true,Padding=new Thickness(10,4,10,4) };
        windowMenu.Items.Add(backgroundBlurMenuItem);
        var blurOptions=new StackPanel { Width=218 };
        var blurLabel=new TextBlock { Text=$"Blur strength  ·  {visualBackdrop.BlurRadius/48:P0}",FontSize=11,Margin=new Thickness(0,0,0,4) };
        backgroundBlurSlider=Slider(0,48,visualBackdrop.BlurRadius,v=> { prefs.BackgroundBlurAmount=v; visualBackdrop.BlurRadius=v; blurLabel.Text=$"Blur strength  ·  {v/48:P0}"; });
        backgroundBlurSlider.ToolTip=$"Strength: {visualBackdrop.BlurRadius/48:P0}";
        backgroundBlurSlider.ValueChanged+=(_,e)=>backgroundBlurSlider.ToolTip=$"Strength: {e.NewValue/48:P0}";
        blurOptions.Children.Add(blurLabel); blurOptions.Children.Add(backgroundBlurSlider);
        windowMenu.Items.Add(new MenuItem { Header=blurOptions,StaysOpenOnClick=true,Focusable=false,Padding=new Thickness(10,4,10,4) });
        ContextMenu=windowMenu;
        InitializeSettingsWindow(appearance,edgeSettings,palette,heading,windowMenu,options,featherControls.Root);
        SetBackgroundType(prefs.BackgroundType);
        InitializePresets(bar,heading);
        windowMenu.Closed+=(_,_)=>backdrops.RefreshNative();
        PreviewMouseRightButtonUp+=(_,e)=>
        {
            OpenContextMenu(PointToScreen(e.GetPosition(this))); e.Handled=true;
        };
        hoverTimer.Tick+=(_,_)=>PollHover(); hoverTimer.Start();
        CompositionTarget.Rendering+=Render;
        PreviewKeyDown+=OnKey;
        SourceInitialized+=(_,_)=> { var source=(HwndSource)PresentationSource.FromVisual(this); source.AddHook(WindowProc); RegisterHotKey(source.Handle,HotkeyId,0x0002|0x0001,0x50); WindowFrame.Suppress(source.Handle); ToolbarMenus.UpdateSystemMenu(source.Handle,prefs.ToolbarLocked,prefs.BackgroundBlur); areaContextMenu=new WindowAreaContextMenu(this,source.Handle,()=>presetDialog?.IsVisible==true||ContextMenu.IsOpen||colorPickers.Any(p=>p.IsPickerOpen)||selectors.Any(s=>s.IsDropDownOpen),OpenContextMenu,CanDragToolbar,BeginToolbarDrag); };
        Loaded+=(_,_)=> { audio.Start(prefs.Device); RefreshDevices(); UpdateDemo(); ToolbarMenus.UpdateJumpList(prefs.ToolbarLocked,prefs.BackgroundBlur); };
        Closing+=(_,_)=> { closing=true; outerGlow?.Close(); settingsWindow.Close(); Save(); hoverTimer.Stop(); areaContextMenu?.Dispose(); CompositionTarget.Rendering-=Render; backdrops.Dispose(); audio.Dispose(); UnregisterHotKey(new WindowInteropHelper(this).Handle,HotkeyId); };
    }

    void AddStyles()
    {
        var buttonStyle=new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty,TextColor));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
        buttonStyle.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(BackdropSurface)); border.Name="Surface";
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(18)); border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(presenter); template.VisualTree=border;
        var hover=new Trigger { Property=IsMouseOverProperty, Value=true }; hover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(36,255,255,255)),"Surface")); template.Triggers.Add(hover);
        var activePage=new Trigger { Property=TagProperty,Value="ActivePage" };
        activePage.Setters.Add(new Setter(Border.BackgroundProperty,AccentColor,"Surface")); template.Triggers.Add(activePage);
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty,template)); Resources.Add(typeof(Button),buttonStyle);
        AddMenuStyles();
        Resources.Add(typeof(CheckBox),(Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
            <StackPanel Orientation="Horizontal" Background="Transparent">
              <Border x:Name="Switch" Width="32" Height="19" CornerRadius="10" Background="#45889AAC" BorderBrush="#60FFFFFF" BorderThickness="1" VerticalAlignment="Center">
                <Border x:Name="Knob" Width="13" Height="13" CornerRadius="7" Margin="2,0" HorizontalAlignment="Left" Background="#F5FFFFFF"/>
              </Border>
              <ContentPresenter Margin="9,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
            </StackPanel>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True"><Setter TargetName="Switch" Property="Background" Value="#7C858E"/><Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Switch" Property="BorderBrush" Value="#E3E5E9"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
        var comboStyle=(Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Pulsive;assembly=Pulsive" TargetType="ComboBox">
          <Setter Property="Foreground" Value="#E9F1FF"/>
          <Setter Property="FontSize" Value="12"/>
          <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
          <Setter Property="ItemContainerStyle">
            <Setter.Value><Style TargetType="ComboBoxItem">
              <Setter Property="Foreground" Value="#E9F1FF"/>
              <Setter Property="Padding" Value="10,7"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
                <Border x:Name="Item" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Item" Property="Background" Value="#28FFFFFF"/></Trigger>
                  <Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="#F1F3F5"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style></Setter.Value>
          </Setter>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
            <Grid>
              <ToggleButton x:Name="DropToggle" Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                    <local:BackdropSurface x:Name="Face" CornerRadius="16">
                    <Path x:Name="Arrow" Data="M 0,0 L 4,4 L 8,0" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ComboBox}}" StrokeThickness="1.4" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" Width="8" Height="4" Stretch="None" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"/>
                  </local:BackdropSurface>
                  <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Background" Value="#40FFFFFF"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name="Selection" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="24,0,24,0" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" TextBlock.TextAlignment="Center"/>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                <local:BackdropSurface Adaptive="False" TintOpacity="0.85" BlurRadius="14" HoverHighlight="False" CornerRadius="20" Padding="8" Margin="0,6,0,0" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                  <ScrollViewer MaxHeight="260"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                </local:BackdropSurface>
              </Popup>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="Tag" Value="Up"><Setter TargetName="PART_Popup" Property="Placement" Value="Top"/></Trigger>
              <Trigger Property="Tag" Value="PresetPicker">
                <Setter TargetName="Selection" Property="Content" Value="{Binding Text, RelativeSource={RelativeSource TemplatedParent}}"/>
                <Setter TargetName="Selection" Property="HorizontalAlignment" Value="Stretch"/>
                <Setter TargetName="Selection" Property="ContentTemplate"><Setter.Value><DataTemplate><TextBlock Text="{Binding}" TextTrimming="CharacterEllipsis" TextAlignment="Center"/></DataTemplate></Setter.Value></Setter>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        Resources.Add(typeof(ComboBox),comboStyle);
        Resources.Add(typeof(Slider),(Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
            <Grid Margin="2,0">
              <Border Height="3" VerticalAlignment="Center" CornerRadius="2" Background="#506D737C"/>
              <Track x:Name="PART_Track">
                <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Width="13" Height="13" Cursor="Hand">
                  <Thumb.Template><ControlTemplate TargetType="Thumb"><Border CornerRadius="7" Background="#E3E5E9" BorderBrush="#FAFBFC" BorderThickness="1"/></ControlTemplate></Thumb.Template>
                </Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
    }
    static Border Glass(Thickness margin)
    {
        return new BackdropSurface { Margin=margin,CornerRadius=new CornerRadius(24),Adaptive=false,TintOpacity=.65,BlurRadius=18,HoverHighlight=false };
    }
    void UpdateFeather()
    {
        prefs.FeatherLeft=Math.Clamp(prefs.FeatherLeft,0,1); prefs.FeatherTop=Math.Clamp(prefs.FeatherTop,0,1);
        prefs.FeatherRight=Math.Clamp(prefs.FeatherRight,0,1); prefs.FeatherBottom=Math.Clamp(prefs.FeatherBottom,0,1);
        prefs.FeatherBoundLeft=Math.Clamp(prefs.FeatherBoundLeft,0,1); prefs.FeatherBoundTop=Math.Clamp(prefs.FeatherBoundTop,0,1);
        prefs.FeatherBoundRight=Math.Clamp(prefs.FeatherBoundRight,0,1); prefs.FeatherBoundBottom=Math.Clamp(prefs.FeatherBoundBottom,0,1);
        visual.SetFeather(new Thickness(prefs.FeatherLeft,prefs.FeatherTop,prefs.FeatherRight,prefs.FeatherBottom),new Thickness(prefs.FeatherBoundLeft,prefs.FeatherBoundTop,prefs.FeatherBoundRight,prefs.FeatherBoundBottom));
    }
    void FeatherChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(updatingFeather) return;
        updatingFeather=true;
        if(prefs.LinkFeather) foreach(var slider in featherSliders) slider.Value=e.NewValue;
        updatingFeather=false; ApplyFeatherSliders();
    }
    void ApplyFeatherSliders()
    {
        if(featherSliders.Count!=4) return;
        prefs.FeatherLeft=featherSliders[0].Value; prefs.FeatherTop=featherSliders[1].Value;
        prefs.FeatherRight=featherSliders[2].Value; prefs.FeatherBottom=featherSliders[3].Value;
        UpdateFeather(); Save();
    }
    ComboBox Select(string[] values,int selected,double width)
    {
        var combo=new ComboBox { Width=width, Height=30, VerticalAlignment=VerticalAlignment.Center, MaxDropDownHeight=280 };
        foreach(var value in values) combo.Items.Add(value); combo.SelectedIndex=selected; selectors.Add(combo); return combo;
    }
    Button Button(string text,string tip,RoutedEventHandler click,bool close=false)
    {
        var button=new Button { Content=text, ToolTip=tip, Width=40, Height=40, Margin=new Thickness(2,0,2,0),FontSize=17, FontWeight=FontWeights.Normal,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center };
        if(text is "↗" or "⚙" or "─" or "□" or "×")
        {
            var icon=new ToolbarIcon { Kind=text };
            icon.SetBinding(Control.ForegroundProperty,new Binding("Foreground") { Source=button }); button.Content=icon;
        }
        button.Click+=click; return button;
    }
    UIElement Labeled(string name,UIElement control,int column)
    {
        var stack=new StackPanel { Margin=new Thickness(column==0?0:16,0,0,0) };
        stack.Children.Add(new TextBlock { Text=name, FontSize=9, Foreground=MutedColor, Margin=new Thickness(0,0,0,7) }); stack.Children.Add(control); Grid.SetColumn(stack,column); return stack;
    }
    Slider Slider(double min,double max,double value,Action<double> changed)
    {
        var slider=new Slider { Minimum=min, Maximum=max, Value=value, Height=30, VerticalAlignment=VerticalAlignment.Center, VerticalContentAlignment=VerticalAlignment.Center, Foreground=AccentColor, IsMoveToPointEnabled=true, ToolTip=value.ToString("0.00") };
        slider.ValueChanged+=(_,e)=> { changed(e.NewValue); slider.ToolTip=e.NewValue.ToString("0.00"); Save(); }; return slider;
    }
    void RefreshDevices()
    {
        string? selected=prefs.Device;
        try
        {
            var available=audio.Devices();
            // Rebuilding only when the actual device list changes preserves an open dropdown.
            if(deviceSelect.Items.Count==available.Count+1 && available.All(d=>deviceSelect.Items.OfType<ComboBoxItem>().Any(i=>(string?)i.Tag==d.Id))) return;
            deviceSelect.Items.Clear();
            var defaultItem=new ComboBoxItem { Content="Default output (follows Windows)", Tag=null }; deviceSelect.Items.Add(defaultItem);
            foreach(var d in available) deviceSelect.Items.Add(new ComboBoxItem { Content=d.Name,Tag=d.Id });
            deviceSelect.SelectedItem=deviceSelect.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>(string?)i.Tag==selected)??defaultItem;
        }
        catch { deviceLabel.Text="NO OUTPUT DEVICE AVAILABLE"; }
    }
    void UpdateTopmostToggle() { settingsTopmostToggle.IsChecked=Topmost; }
    internal void SetBackgroundBlur(bool enabled)
        =>SetBackgroundType(enabled?2:0);
    void UpdateDemo() { previewButton.Foreground=audio.Demo?AccentColor:TextColor; previewButton.Content=audio.Demo?"◇  Preview on":"◇  Preview"; }
    void ToggleDemo() { audio.Demo=!audio.Demo; UpdateDemo(); }
    void ToggleSettings() { if(prefs.ToolbarLocked) return; if(settingsOpen) HideSettings(); else ShowSettings(); }
    internal void SetToolbarLocked(bool locked)
    {
        prefs.ToolbarLocked=locked;
        if(locked)
        {
            foreach(var selector in selectors) selector.IsDropDownOpen=false;
        }
        toolbarLockMenuItem.Header=ToolbarMenus.Label(locked);
        UpdateHoverControls();
        var hwnd=new WindowInteropHelper(this).Handle;
        if(hwnd!=IntPtr.Zero) ToolbarMenus.UpdateSystemMenu(hwnd,locked,prefs.BackgroundBlur);
        ToolbarMenus.UpdateJumpList(locked,prefs.BackgroundBlur);
        Save();
    }
    void Drag(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left || e.Handled) return;
        if(e.ClickCount==2) ToggleMaximize();
        else { if(WindowState==WindowState.Maximized) WindowState=WindowState.Normal; DragMove(); }
    }
    void ToggleMaximize() { MaxHeight=SystemParameters.WorkArea.Height; WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized; }
    void OnKey(object sender,KeyEventArgs e)
    {
        // Let dropdowns and sliders retain their normal keyboard behaviour.
        if(e.OriginalSource is ComboBox || e.OriginalSource is Slider || e.OriginalSource is ComboBoxItem || e.OriginalSource is ButtonBase) return;
        switch(e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Right: modeSelect.SelectedIndex=(visual.Mode+1)%8; break;
            case Key.Left: modeSelect.SelectedIndex=(visual.Mode+7)%8; break;
            case Key.Space: paused=!paused; break;
            case Key.D: ToggleDemo(); break;
            case Key.S: ToggleSettings(); break;
            case Key.T: Topmost=!Topmost; UpdateTopmostToggle(); Save(); break;
        }
    }
    void Render(object? sender,EventArgs e)
    {
        if(closing || WindowState==WindowState.Minimized || !IsVisible) return;
        double now=watch.Elapsed.TotalSeconds,dt=Math.Min(.1,now-lastTime); if(dt<1d/65) return; lastTime=now;
        if(!paused) { audio.Update(now,dt); visual.Time+=dt; visual.InvalidateVisual(); }
        UpdateBorder(); overlayBorder.Update(audio.Level,audio.Bass,paused?0:dt);
        if(prefs.BorderType>=3) outerGlow??=new OuterGlowWindow(this);
        outerGlow?.Update(prefs.BorderType,borderTint,visual.Palette,audio.Level,audio.Bass,paused?0:dt);
        if(now-lastDeviceCheck>2) { lastDeviceCheck=now; audio.CheckDefaultDevice(); }
        deviceLabel.Text=audio.Demo?"GENERATED PREVIEW · SYSTEM AUDIO IS NOT SHOWN":"SYSTEM AUDIO · "+audio.DeviceName.ToUpperInvariant();
        welcome.Visibility=now<12&&!audio.HasAudio&&!audio.Demo&&!settingsOpen?Visibility.Visible:Visibility.Collapsed;
    }
    void PollHover()
    {
        if(!GetCursorPos(out var point)) return;
        bool inside=false;
        if(IsVisible && WindowState!=WindowState.Minimized)
        {
            var local=PointFromScreen(new Point(point.X,point.Y)); inside=(local.X>=0&&local.Y>=0&&local.X<ActualWidth&&local.Y<ActualHeight)||selectors.Any(s=>s.IsDropDownOpen);
        }
        SetHover(inside);
    }
    void SetHover(bool inside)
    {
        if(inside==hovered) return; hovered=inside;
        UpdateHoverControls();
    }
    void OpenContextMenu(Point screenPoint)
    {
        if(closing||!IsVisible||WindowState==WindowState.Minimized) return;
        var local=PointFromScreen(screenPoint);
        toolbarLockMenuItem.Header=ToolbarMenus.Label(prefs.ToolbarLocked);
        ContextMenu.PlacementTarget=this; ContextMenu.Placement=PlacementMode.Relative;
        ContextMenu.HorizontalOffset=local.X; ContextMenu.VerticalOffset=local.Y;
        ContextMenu.IsOpen=true;
    }
    bool CanDragToolbar(Point screenPoint)
    {
        if(!prefs.ToolbarLocked&&!toolbar.IsHitTestVisible) return false;
        var point=toolbar.PointFromScreen(screenPoint);
        if(point.X<0||point.Y<0||point.X>=toolbar.ActualWidth||point.Y>=toolbar.ActualHeight) return false;
        // Hidden controls cannot be clicked, so their whole toolbar row is a
        // drag target while locked. The visualiser below remains excluded.
        if(prefs.ToolbarLocked) return true;
        for(var hit=InputHitTest(PointFromScreen(screenPoint)) as DependencyObject;hit!=null;hit=VisualTreeHelper.GetParent(hit))
            if(hit is ButtonBase or ComboBox or System.Windows.Controls.Slider) return false;
        return true;
    }
    void BeginToolbarDrag(Point screenPoint,int phase)
    {
        if(phase!=0)
        {
            if(toolbarDragging&&!closing&&IsVisible&&WindowState==WindowState.Normal)
            {
                var delta=screenPoint-toolbarDragStart;
                var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
                Left=toolbarDragOrigin.X+delta.X*transform.M11; Top=toolbarDragOrigin.Y+delta.Y*transform.M22;
                if(delta.Length>5) lastToolbarPress=0;
            }
            if(phase==2) toolbarDragging=false;
            return;
        }
        toolbarDragging=false;
        if(closing||!CanDragToolbar(screenPoint)) return;
        long now=Environment.TickCount64;
        if(now-lastToolbarPress<=GetDoubleClickTime()&&(screenPoint-lastToolbarPoint).Length<=5)
        {
            lastToolbarPress=0; ToggleMaximize(); return;
        }
        lastToolbarPress=now; lastToolbarPoint=screenPoint;
        if(WindowState==WindowState.Maximized)
        {
            double fraction=PointFromScreen(screenPoint).X/ActualWidth;
            WindowState=WindowState.Normal;
            var point=PointFromScreen(screenPoint); Left+=point.X-Width*fraction; Top+=point.Y-32;
        }
        Activate(); toolbarDragging=true;
        toolbarDragStart=screenPoint; toolbarDragOrigin=new Point(Left,Top);
    }
    void UpdateHoverControls()
    {
        bool show=hovered&&!prefs.ToolbarLocked;
        toolbar.IsHitTestVisible=show;
        if(prefs.ToolbarLocked)
        {
            toolbar.BeginAnimation(OpacityProperty,null); toolbar.Opacity=0;
        }
        else toolbar.BeginAnimation(OpacityProperty,new DoubleAnimation(show?1:0,TimeSpan.FromMilliseconds(180)));
        foreach(var grip in grips) grip.Visibility=hovered&&WindowState==WindowState.Normal?Visibility.Visible:Visibility.Collapsed;
    }
    void AddGrips()
    {
        foreach(var (horizontal,vertical,cursor) in new[] {
            (HorizontalAlignment.Left,VerticalAlignment.Stretch,Cursors.SizeWE), (HorizontalAlignment.Right,VerticalAlignment.Stretch,Cursors.SizeWE),
            (HorizontalAlignment.Stretch,VerticalAlignment.Top,Cursors.SizeNS),(HorizontalAlignment.Stretch,VerticalAlignment.Bottom,Cursors.SizeNS),
            (HorizontalAlignment.Left,VerticalAlignment.Top,Cursors.SizeNWSE),(HorizontalAlignment.Right,VerticalAlignment.Top,Cursors.SizeNESW),
            (HorizontalAlignment.Left,VerticalAlignment.Bottom,Cursors.SizeNESW),(HorizontalAlignment.Right,VerticalAlignment.Bottom,Cursors.SizeNWSE) })
        {
            var thumb=new Thumb { HorizontalAlignment=horizontal,VerticalAlignment=vertical,Cursor=cursor,Width=horizontal==HorizontalAlignment.Stretch?double.NaN:9,Height=vertical==VerticalAlignment.Stretch?double.NaN:9,Visibility=Visibility.Collapsed };
            if(horizontal==HorizontalAlignment.Stretch) thumb.Margin=new Thickness(9,0,9,0);
            if(vertical==VerticalAlignment.Stretch) thumb.Margin=new Thickness(0,9,0,9);
            var gripTemplate=new ControlTemplate(typeof(Thumb));
            var gripSurface=new FrameworkElementFactory(typeof(Border));
            // Minimum alpha retains layered-window hit testing without painting a white outline.
            gripSurface.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(1,0,0,0)));
            gripTemplate.VisualTree=gripSurface; thumb.Template=gripTemplate;
            thumb.DragDelta+=(_,e)=>
            {
                if(WindowState!=WindowState.Normal) return;
                if(horizontal==HorizontalAlignment.Left) { double width=Math.Max(MinWidth,Width-e.HorizontalChange); Left+=Width-width; Width=width; }
                if(horizontal==HorizontalAlignment.Right) Width=Math.Max(MinWidth,Width+e.HorizontalChange);
                if(vertical==VerticalAlignment.Top) { double height=Math.Max(MinHeight,Height-e.VerticalChange); Top+=Height-height; Height=height; }
                if(vertical==VerticalAlignment.Bottom) Height=Math.Max(MinHeight,Height+e.VerticalChange);
            };
            root.Children.Add(thumb); grips.Add(thumb);
        }
    }
    static Preferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath))??Preferences.CreateDefault();
        }
        catch { return Preferences.CreateDefault(); }
    }
    void Save()
    {
        if(visual is null||applyingPreset) return;
        prefs.Mode=visual.Mode; prefs.Palette=visual.Palette; prefs.Gain=audio.Gain; prefs.Intensity=visual.Intensity; prefs.Zoom=visual.Zoom; prefs.Topmost=Topmost;
        prefs.Width=WindowState==WindowState.Normal?Width:RestoreBounds.Width; prefs.Height=WindowState==WindowState.Normal?Height:RestoreBounds.Height;
        TrackPresetChanges();
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath,JsonSerializer.Serialize(prefs)); } catch { /* Preferences must never interrupt the overlay. */ }
    }
    IntPtr WindowProc(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(msg==0x001A) backdrops.RefreshEffectsPolicy();
        if((uint)msg==ToolbarMenus.SetLockMessage)
        {
            SetToolbarLocked(wp!=IntPtr.Zero); handled=true; return IntPtr.Zero;
        }
        if((uint)msg==ToolbarMenus.SetBlurMessage) { SetBackgroundBlur(wp!=IntPtr.Zero); handled=true; return IntPtr.Zero; }
        if(msg==0x0112&&(wp.ToInt64()&0xFFF0)==ToolbarMenus.BlurCommand) { SetBackgroundBlur(!prefs.BackgroundBlur); handled=true; return IntPtr.Zero; }
        if(msg==0x0112&&(wp.ToInt64()&0xFFF0)==ToolbarMenus.SystemCommand)
        {
            SetToolbarLocked(!prefs.ToolbarLocked); handled=true; return IntPtr.Zero;
        }
        if(msg==0x0312 && wp.ToInt32()==HotkeyId) { if(IsVisible&&WindowState!=WindowState.Minimized) Hide(); else { Show(); WindowState=WindowState.Normal; Activate(); } handled=true; }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
}
