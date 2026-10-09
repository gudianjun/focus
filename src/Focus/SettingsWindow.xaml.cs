using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Focus;

public partial class SettingsWindow : Window
{
    const string NoSound = "（无）";

    readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        AutoStartBox.IsChecked = settings.AutoStart;
        HotKeyBox.Text = settings.HotKey;
        AutoCompactBox.IsChecked = settings.AutoCompact;
        OpacitySlider.Value = settings.FocusOpacity;

        SoundBox.Items.Add(NoSound);
        foreach (var sound in Sounds.List())
            SoundBox.Items.Add(sound);
        if (settings.Sound.Length > 0 && !SoundBox.Items.Contains(settings.Sound))
            SoundBox.Items.Add(settings.Sound);
        SoundBox.SelectedItem = settings.Sound.Length == 0 ? NoSound : settings.Sound;
    }

    string SelectedSound => SoundBox.SelectedItem is string s && s != NoSound ? s : "";

    void HotKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (HotKey.IsModifierKey(key))
            return;
        if (modifiers == ModifierKeys.None)
        {
            if (key is Key.Back or Key.Delete)
                HotKeyBox.Text = "";
            return;
        }
        HotKeyBox.Text = HotKey.Format(modifiers, key);
    }

    void ClearHotKey_Click(object sender, RoutedEventArgs e) => HotKeyBox.Text = "";

    void PreviewSound_Click(object sender, RoutedEventArgs e) => Sounds.Play(SelectedSound);

    void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        OpacityText.Text = $"{e.NewValue:P0}";

    void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataDirectory);
        Process.Start(new ProcessStartInfo(AppSettings.DataDirectory) { UseShellExecute = true });
    }

    void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoStart = AutoStartBox.IsChecked == true;
        _settings.HotKey = HotKeyBox.Text;
        _settings.Sound = SelectedSound;
        _settings.FocusOpacity = Math.Round(OpacitySlider.Value, 2);
        _settings.AutoCompact = AutoCompactBox.IsChecked == true;
        DialogResult = true;
    }
}
