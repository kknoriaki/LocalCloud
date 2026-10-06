using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace LocalCloud.Desktop;
sealed class MainWindow : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly string origin; readonly Func<bool> exiting;
    readonly Panel fallback = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 249, 252) };
    readonly Label status = new() { Dock = DockStyle.Top, Height = 130, TextAlign = ContentAlignment.MiddleCenter, Text = "Запускаем ваше домашнее облако…" };
    bool ready; string target; FormWindowState previousState; Rectangle previousBounds;
    public MainWindow(int port, Func<bool> exiting)
    {
        this.exiting = exiting; origin = $"http://localhost:{port}"; target = origin;
        Text = "LocalCloud"; ClientSize = new Size(1280, 860); MinimumSize = new Size(860, 620); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11); Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); AutoScaleMode = AutoScaleMode.Dpi;
        var open = new Button { Text = "Открыть в браузере", Width = 210, Height = 42, Top = 150, Left = 40 };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        fallback.Controls.Add(open); fallback.Controls.Add(status); Controls.Add(web); Controls.Add(fallback); fallback.BringToFront();
        Shown += async (_, _) => await Initialize();
        FormClosing += (_, e) => { if (!exiting()) { e.Cancel = true; Hide(); } };
    }
    async Task Initialize()
    {
        if (ready) return;
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCloud", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (IsDisposed) return; await web.EnsureCoreWebView2Async(environment); if (IsDisposed) return;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.NavigationStarting += (_, e) => { if (!Local(e.Uri)) { e.Cancel = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } };
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; if (Local(e.Uri)) web.CoreWebView2.Navigate(e.Uri); };
            web.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) { fallback.Hide(); web.Show(); } else { status.Text = "Сервер пока не ответил. Откройте меню LocalCloud в трее и проверьте, запущен ли сервер."; fallback.Show(); fallback.BringToFront(); } };
            web.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => {
                if (web.CoreWebView2.ContainsFullScreenElement) { previousState = WindowState; previousBounds = Bounds; WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; }
                else { FormBorderStyle = FormBorderStyle.Sizable; WindowState = FormWindowState.Normal; Bounds = previousBounds; WindowState = previousState; }
            };
            ready = true; web.CoreWebView2.Navigate(target);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        { if (!IsDisposed) status.Text = "Не удалось открыть встроенное окно. LocalCloud продолжает работать — откройте его в браузере. Для встроенного окна нужен Microsoft Edge WebView2 Runtime."; }
    }
    bool Local(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Authority) == origin;
    public void OpenPage(string? path = null)
    { target = origin + (path ?? ""); Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); if (ready && path != null) web.CoreWebView2.Navigate(target); }
}
