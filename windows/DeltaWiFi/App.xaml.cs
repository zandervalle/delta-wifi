//
//  App.xaml.cs
//  DeltaWiFi
//
//  DELTA Wi-Fi Analyzer für Windows: ein Fenster je Projekt. Eine `.wlanprojekt`-Datei
//  als Argument (z. B. per „Öffnen mit“) wird beim Start geladen.
//

using System.Windows;
using DeltaWiFi.Views;

namespace DeltaWiFi;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var path = e.Args.FirstOrDefault(File.Exists);
        new MainWindow(path).Show();
    }
}
