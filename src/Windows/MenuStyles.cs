using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Pulsive;

public sealed partial class MainWindow
{
    void AddMenuStyles()
    {
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
