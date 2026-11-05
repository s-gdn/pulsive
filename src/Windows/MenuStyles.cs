using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Pulsive;

public sealed partial class MainWindow
{
    void AddMenuStyles()
    {
        // Shared by dropdown popups, settings pages and library lists.
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Setter Property="Width" Value="10"/><Setter Property="MinWidth" Value="0"/><Setter Property="MinHeight" Value="0"/><Setter Property="Background" Value="Transparent"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
            <Grid Background="Transparent" Margin="0,3">
              <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="Vertical" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}">
                <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb MinHeight="22"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#809CA0A6" CornerRadius="3" Margin="3,0,2,0"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
        Resources.Add("PulsiveScrollViewer",(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollViewer">
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollViewer">
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" CanContentScroll="{TemplateBinding CanContentScroll}" Margin="{TemplateBinding Padding}"/>
              <ScrollBar x:Name="PART_VerticalScrollBar" Grid.Column="1" Width="10" Orientation="Vertical" Style="{DynamicResource {x:Type ScrollBar}}" Minimum="0" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}" Value="{Binding VerticalOffset,RelativeSource={RelativeSource TemplatedParent},Mode=OneWay}" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}"/>
            </Grid>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
        Resources.Add(typeof(ContextMenu),(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Pulsive;assembly=Pulsive" TargetType="ContextMenu">
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="Foreground" Value="White"/>
          <Setter Property="HasDropShadow" Value="False"/>
          <Setter Property="FontSize" Value="12"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu">
            <local:BackdropSurface CornerRadius="20" Padding="8" BlurRadius="14" TintOpacity="0.85" Adaptive="False" HoverHighlight="False">
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
            </local:BackdropSurface>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
        Resources.Add(typeof(MenuItem),(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="MenuItem">
          <Setter Property="Foreground" Value="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ContextMenu}}"/>
          <Setter Property="Padding" Value="10,10"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
            <Border x:Name="Row" CornerRadius="11" Padding="{TemplateBinding Padding}" Background="Transparent">
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="24"/></Grid.ColumnDefinitions>
                <ContentPresenter ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                <TextBlock x:Name="Check" Grid.Column="1" Text="✓" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Center" FontSize="14"/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="#20888888"/></Trigger>
              <Trigger Property="IsChecked" Value="True"><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
    }
}
